using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cube.Timer
{
    /// <summary>
    /// A Timer optimized for approximated I/O timeout scheduling.
    /// </summary>
    public class HashedWheelTimer : IDisposable
    {
        //
        // 1. Running a single thread as the worker-thread in background to spin the wheel.
        //   We don't use Task.Run() to get the thread which in the threadPool,
        //   because the jobs-queue of threadPool could be filled by many jobs just in case.
        //   After a Task.Delay() to wait to next tick, the thread may not back to work on time.
        //   So *LongRunning* is necessary, to create a new thread.
        //
        // 2. Call the method timerTaskHandle.Cancel() to cancel the task,
        //    and we don't delete it from the taskList immediately, but just marked it's cancelled. 
        //   When the task is expired and to be executing, then check the status of  *Cancelled*.
        //   This gonna needs more memory, but saves CPU times for searching and delete the task.
        //  especially the timer is handling a large number of tasks.
        //

        private readonly ILogger logger;

        private static readonly int MAX_WHEEL_CAPACITY = 1 << 30;

        private readonly TaskList[] wheel;
        private readonly int mask;
        private readonly long MAX_PENDING_TIMER_TASKS;

        private readonly long tickDuration;
        private readonly long baseTime = DateTime.UtcNow.Ticks; //  1ms = 10000 ticks
        private long tick = 0;

        private bool gatherUnprocessedTasks = false;
        private long pendingTasks = 0;
        private readonly Task workerThreadTask;
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();


        /// <summary>
        ///   Gets a value that indicates the total of pending tasks.
        /// </summary>
        public long PendingTasks => pendingTasks;

        /// <summary>
        ///   Gets a value that indicates the timer is running.
        /// </summary>
        public bool IsRunning => cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested;

        /// <summary>
        /// Create timer with default parameter value: <br/>
        /// tickDuration = 100ms <br/>
        /// ticksPerWheel = 512 <br/>
        /// maxPendingTimerTasks = 0, unlimited. <br/>
        /// </summary>
        public HashedWheelTimer()
            : this(TimeSpan.FromMilliseconds(100), 512, 0, null)
        {
        }

        /// <summary>
        /// Create timer with default parameter value: <br/>
        /// tickDuration = 100ms <br/>
        /// ticksPerWheel = 512 <br/>
        /// maxPendingTimerTasks = 0, unlimited. <br/>
        /// </summary>
        /// <param name="logger">if necessary, set the logger, default value is null</param>
        public HashedWheelTimer(ILogger<HashedWheelTimer> logger)
            : this(TimeSpan.FromMilliseconds(100), 512, 0, logger)
        {
        }

        /// <summary>
        /// Create timer with default parameter value: <br/>
        /// ticksPerWheel = 512 <br/>
        /// maxPendingTimerTasks = 0, unlimited. <br/>
        /// </summary>
        /// <param name="tickDuration">tick duration</param>
        /// <param name="logger">if necessary, set the logger, default value is null</param>
        public HashedWheelTimer(TimeSpan tickDuration, ILogger<HashedWheelTimer> logger)
            : this(tickDuration, 512, 0, logger)
        {
        }

        /// <summary>
        /// Create timer
        /// </summary>
        /// <param name="tickDuration">tick duration</param>
        /// <param name="ticksPerWheel">ticksPerWheel</param>
        /// <param name="maxPendingTimerTasks">maxPendingTimerTasks</param>
        /// <param name="logger">if necessary, set the logger, default value is null</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public HashedWheelTimer(
            TimeSpan tickDuration,
            int ticksPerWheel = 512,
            long maxPendingTimerTasks = 0,
            ILogger<HashedWheelTimer> logger = null)
        {
            if (ticksPerWheel < 1 || ticksPerWheel > MAX_WHEEL_CAPACITY)
            {
                throw new ArgumentOutOfRangeException(nameof(ticksPerWheel),
                    $"expected: 0 <  value < {MAX_WHEEL_CAPACITY} ");
            }

            if (logger == null)
            {
                this.logger = NullLogger.Instance;
            }
            else
            {
                this.logger = logger;
            }

            this.MAX_PENDING_TIMER_TASKS = maxPendingTimerTasks;

            wheel = CreateWheel(ticksPerWheel);
            mask = wheel.Length - 1;

            if (tickDuration.TotalMilliseconds < 1 ||
                tickDuration.TotalMilliseconds > (long.MaxValue / 10000 / wheel.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(tickDuration),
                    $"expected milliseconds: 0 <  value < {long.MaxValue / 10000 / wheel.Length} ");
            }

            this.tickDuration = tickDuration.Ticks;

            this.logger.LogDebug("Initialized, {0}", this);

            workerThreadTask = Task.Factory.StartNew(SpinTheWheel, cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }


        /// <summary>
        /// The delegate to handle the notices.
        /// </summary>
        public delegate void NoticeCallback(IList<object> notices);

        private NoticeCallback noticeCallback;

        /// <summary>
        /// Set the notice callback.
        /// </summary>
        /// <param name="callback"></param>
        public void SetNoticeCallback(NoticeCallback callback)
        {
            noticeCallback = callback;
        }

        /// <summary>
        /// Add a notice task for one-time execution.
        /// </summary>
        /// <param name="notice">the notice</param>
        /// <param name="delayMilliseconds">delay milliseconds</param>
        /// <returns>a handle which is associated with the specified timer task</returns>
        public TimerTaskHandle AddNotice(int delayMilliseconds, object notice)
        {
            return AddNotice(TimeSpan.FromMilliseconds(delayMilliseconds), notice);
        }

        /// <summary>
        /// Add a notice task for one-time execution.
        /// </summary>
        /// <param name="notice">the notice</param>
        /// <param name="delay">delay time</param>
        /// <returns>a handle which is associated with the specified timer task</returns>
        public TimerTaskHandle AddNotice(TimeSpan delay, object notice)
        {
            if (notice == null)
            {
                throw new ArgumentNullException(nameof(notice));
            }

            if (cancellationTokenSource != default && cancellationTokenSource.Token.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"failed to add notice, {nameof(HashedWheelTimer)} has been stopped");
            }

            Interlocked.Increment(ref pendingTasks);
            if (MAX_PENDING_TIMER_TASKS > 0 && pendingTasks > MAX_PENDING_TIMER_TASKS)
            {
                Interlocked.Decrement(ref pendingTasks);
                throw new InvalidOperationException(
                    $"failed to add notice, approaching limit,  {nameof(MAX_PENDING_TIMER_TASKS)}={MAX_PENDING_TIMER_TASKS}.");
            }

            var deadline = DateTime.UtcNow.Ticks - baseTime + delay.Ticks;
            if (deadline < 0 && delay.Ticks > 0)
            {
                // Guard against overflow.
                deadline = long.MaxValue;
            }

            var handle = new TimerTaskHandle(notice, baseTime + deadline);

            var calculated = deadline / tickDuration;
            var rounds = (calculated - tick) / wheel.Length;

            var ticks = Math.Max(calculated, tick); // Ensure we don't schedule for past.
            var slotIndex = (int)(ticks & mask);

            var node = new TaskEntry(handle, deadline, rounds, slotIndex);

            logger.LogTrace("Add notice to queue, {0}", node);

            newTaskQueue.Enqueue(node);

            return handle;
        }


        private static TaskList[] CreateWheel(int ticksPerWheel)
        {
            ticksPerWheel = NormalizeTicksPerWheel(ticksPerWheel);
            var wheel = new TaskList[ticksPerWheel];
            for (var i = 0; i < wheel.Length; i++)
            {
                wheel[i] = new TaskList();
            }

            return wheel;
        }


        private readonly ConcurrentQueue<TaskEntry> newTaskQueue = new ConcurrentQueue<TaskEntry>();

        /// <summary>
        /// Add a timer task for one-time execution.
        /// </summary>
        /// <param name="task">the instance of ITimerTask</param>
        /// <param name="delay">delay time</param>
        /// <returns>a handle which is associated with the specified timer task</returns>
        public TimerTaskHandle AddTask(TimeSpan delay, ITimerTask task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (cancellationTokenSource != default && cancellationTokenSource.Token.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"failed to add new task, {nameof(HashedWheelTimer)} has been stopped");
            }

            Interlocked.Increment(ref pendingTasks);
            if (MAX_PENDING_TIMER_TASKS > 0 && pendingTasks > MAX_PENDING_TIMER_TASKS)
            {
                Interlocked.Decrement(ref pendingTasks);
                throw new InvalidOperationException(
                    $"failed to add new task, approching limit,  {nameof(MAX_PENDING_TIMER_TASKS)}={MAX_PENDING_TIMER_TASKS}.");
            }

            var deadline = DateTime.UtcNow.Ticks - baseTime + delay.Ticks;
            if (deadline < 0 && delay.Ticks > 0)
            {
                // Guard against overflow.
                deadline = long.MaxValue;
            }

            var handle = new TimerTaskHandle(task, baseTime + deadline);

            var calculated = deadline / tickDuration;
            var rounds = (calculated - tick) / wheel.Length;

            var ticks = Math.Max(calculated, tick); // Ensure we don't schedule for past.
            var slotIndex = (int)(ticks & mask);

            var node = new TaskEntry(handle, deadline, rounds, slotIndex);

            logger.LogTrace("Add new task to queue, {0}", node);

            newTaskQueue.Enqueue(node);

            return handle;
        }

        /// <summary>
        /// Add a timer task for one-time execution.
        /// </summary>
        /// <param name="delayMilliseconds">delay milliseconds</param>
        /// <param name="task">the instance of ITimerTask</param>
        /// <returns></returns>
        public TimerTaskHandle AddTask(int delayMilliseconds, ITimerTask task)
        {
            return this.AddTask(TimeSpan.FromMilliseconds(delayMilliseconds), task);
        }

        /// <summary>
        /// Add a timer task for one-time execution.
        /// </summary>
        /// <param name="delay">delay time</param>
        /// <param name="action">lambda expression</param>
        /// <returns></returns>
        public TimerTaskHandle AddTask(TimeSpan delay, Action action)
        {
            var task = new LambdaTimerTask(action);
            return this.AddTask(delay, task);
        }

        /// <summary>
        /// Add a timer task for one-time execution.
        /// </summary>
        /// <param name="delayMilliseconds">delay milliseconds</param>
        /// <param name="action">lambda expression</param>
        /// <returns></returns>
        public TimerTaskHandle AddTask(int delayMilliseconds, Action action)
        {
            var task = new LambdaTimerTask(action);
            return this.AddTask(TimeSpan.FromMilliseconds(delayMilliseconds), task);
        }

        /// <summary>
        /// Add a timer task for one-time execution.
        /// </summary>
        /// <param name="delay">delay time</param>
        /// <param name="action">lambda expression</param>
        /// <param name="args">pass the parameter</param>
        /// <returns></returns>
        public TimerTaskHandle AddTask(TimeSpan delay, Action<object> action, object args)
        {
            var task = new LambdaTimerTask(action, args);
            return this.AddTask(delay, task);
        }

        /// <summary>
        /// Add a timer task for one-time execution.
        /// </summary>
        /// <param name="delayMilliseconds">delay milliseconds</param>
        /// <param name="action">lambda expression</param>
        /// <param name="args">pass the parameter</param>
        /// <returns></returns>
        public TimerTaskHandle AddTask(int delayMilliseconds, Action<object> action, object args)
        {
            var task = new LambdaTimerTask(action, args);
            return this.AddTask(TimeSpan.FromMilliseconds(delayMilliseconds), task);
        }

        /// <summary>
        /// stop the timer
        /// </summary>
        /// <param name="gatherUnprocessedTasks">gather upprocessed tasks</param>
        /// <returns>return empty list if gatherUnprocessedTasks = false, otherwise return all the unprocessed tasks. </returns>
        public async Task<IList<TimerTaskHandle>> Stop(bool gatherUnprocessedTasks = false)
        {
            logger.LogTrace($"Stoping {nameof(HashedWheelTimer)} ... gatherUnprocessedTasks={gatherUnprocessedTasks}");

            lock (newTaskQueue)
            {
                if (cancellationTokenSource == null || cancellationTokenSource.Token.IsCancellationRequested)
                {
                    return new List<TimerTaskHandle>();
                }
                else
                {
                    cancellationTokenSource.Cancel();
                }
            }

            this.gatherUnprocessedTasks = gatherUnprocessedTasks;

            if (this.gatherUnprocessedTasks)
            {
                try
                {
                    if (workerThreadTask != null && !workerThreadTask.IsCompleted)
                    {
                        logger.LogTrace("Waiting for the worker thread...");

#if (NET6_0 || NET7_0)
                        await workerThreadTask.WaitAsync(TimeSpan.FromSeconds(5));
#else
                        await workerThreadTask.TimeoutAfter(TimeSpan.FromSeconds(5));
#endif
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "stop timer");
                }
            }

            logger.LogDebug($"Stoping {nameof(HashedWheelTimer)} ... done");

            return unprocessedTasks;
        }


        private readonly List<TimerTaskHandle> unprocessedTasks = new List<TimerTaskHandle>();

        private void SpinTheWheel()
        {
            this.logger.LogTrace("Start spinning the wheel...   ManagedThreadId={0}",
                Thread.CurrentThread.ManagedThreadId);

            Exception unhandleException = null;
            CancellationToken token = cancellationTokenSource.Token;
            try
            {
                IList<object> expiredNotices = new List<object>();

                while (!token.IsCancellationRequested)
                {
                    if (this.logger.IsEnabled(LogLevel.Trace))
                    {
                        this.logger.LogTrace(
                            "{0} >>> ticking --------- {1} -- ManagedThreadId={2} -- IsThreadPoolThread={3}  ",
                            tick, DateTime.Now.ToString("HH:mm:ss.fff"), Thread.CurrentThread.ManagedThreadId,
                            Thread.CurrentThread.IsThreadPoolThread);
                    }

                    TransferNewTaskToWheel(token);

                    // remove the expired tasks
                    var idx = (int)(tick & mask);
                    (var expiredTotal, var expiredTasks, var totalNotices) = wheel[idx].RemoveExpiredTasks();

                    this.logger.LogTrace("{0} >>> remove the expired tasks, count = {1}", tick, expiredTasks.Length);

                    // process expired tasks
                    expiredNotices.Clear();
                    Interlocked.Add(ref pendingTasks, -expiredTotal);
                    for (int i = 0; i < expiredTotal; i++)
                    {
                        if (!expiredTasks[i].TimerTaskHandle.Cancelled)
                        {
                            if (expiredTasks[i].TimerTaskHandle.Notice != null)
                            {
                                expiredNotices.Add(expiredTasks[i].TimerTaskHandle.Notice);
                            }
                            else
                            {
                                _ = expiredTasks[i].TimerTask.RunAsync();
                            }
                        }

                        // transfer the rest
                        if (token.IsCancellationRequested && gatherUnprocessedTasks)
                        {
                            unprocessedTasks.Add(expiredTasks[i].TimerTaskHandle);
                        }
                    }

                    // handle the notices
                    if (expiredNotices.Count > 0)
                    {
                        noticeCallback?.Invoke(expiredNotices);
                    }

                    // free the array, very important.
                    ArrayPool<TaskEntry>.Shared.Return(expiredTasks);

                    // wait to next tick
                    var deadline = baseTime + tickDuration + tick * tickDuration - 1;
                    var sleep = (int)((deadline - DateTime.UtcNow.Ticks) / 10000); // milliseconds
                    if (sleep > 0)
                    {
                        this.logger.LogTrace("{0} >>> Sleep and wait to next tick, {1}ms. ", tick, sleep);
                        Thread.Sleep(sleep);
                    }

                    tick++;
                } // end of while
            }
            catch (TaskCanceledException ex1)
            {
                //   Task was canceled before running.
                logger.LogDebug(ex1.Message);
            }
            catch (OperationCanceledException ex2)
            {
                //  Task was canceled while running.
                logger.LogDebug(ex2.Message);
            }
            catch (Exception ex)
            {
                unhandleException = ex;
                cancellationTokenSource.Cancel();

                logger.LogError(ex, "Check the execution of the timer task.");
            }

            if (gatherUnprocessedTasks)
            {
                GatheringUnprocessedTasks();
            }

            Interlocked.Exchange(ref pendingTasks, 0);

            logger.LogDebug("Worker thread exit, the wheel stops spinning.");

            if (unhandleException != null)
            {
                throw unhandleException;
            }
        }

        private void GatheringUnprocessedTasks()
        {
            logger.LogTrace("Gathering unprocessed tasks... ");

            for (int i = (int)(tick & mask); i < wheel.Length; i++)
            {
                (var total, var list) = wheel[i].RemoveAllTasks();
                for (int k = 0; k < total; k++)
                {
                    unprocessedTasks.Add(list[k]);
                }

                ArrayPool<TimerTaskHandle>.Shared.Return(list);
            }

            while (true)
            {
                if (!newTaskQueue.TryDequeue(out TaskEntry node))
                {
                    break;
                }

                unprocessedTasks.Add(node.TimerTaskHandle);
            }
        }

        private int TransferNewTaskToWheel(CancellationToken cancellationToken)
        {
            int count = 0;

            // to prevent the worker thread stuck in adding new task loop.
            for (var i = 0; i < 60_000; i++)
            {
                if (!newTaskQueue.TryDequeue(out var entry) || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (entry.TimerTaskHandle.Cancelled)
                {
                    logger.LogTrace("{0} >>> task was cancelled: {0}", tick, entry);
                    Interlocked.Decrement(ref pendingTasks);
                    continue;
                }

                wheel[entry.WheelIndex].AddTask(entry);

                count++;
            }

            logger.LogTrace("{0} >>> Transfer new tasks, count = {1}", tick, count);

            return count;
        }


        // get upper power of 2
        private static int NormalizeTicksPerWheel(int ticksPerWheel)
        {
            if (ticksPerWheel >= MAX_WHEEL_CAPACITY)
            {
                return MAX_WHEEL_CAPACITY;
            }

            if (ticksPerWheel <= 1)
            {
                return 1;
            }

            int n = ticksPerWheel - 1;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            n = n + 1;
            return n;
        }

        public void Dispose()
        {
            cancellationTokenSource?.Cancel();
            for (int i = 0; i < wheel.Length; i++)
            {
                wheel[i].Dispose();
                wheel[i] = null;
            }
        }

        public override string ToString()
        {
            return string.Format("[{0}: tickDuration={1}, ticksPerWheel={2}, maxPendingTimerTasks={3}]",
                nameof(HashedWheelTimer), this.tickDuration, this.wheel.Length, this.MAX_PENDING_TIMER_TASKS);
        }
    }
}