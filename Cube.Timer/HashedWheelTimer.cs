using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cube.Timer
{
    /// <summary>
    /// A Timer optimized for approximated I/O timeout scheduling.
    /// </summary>
    public class HashedWheelTimer : IDisposable
    {
        private readonly ILogger logger;

        private static readonly int MAX_WHEEL_CAPACITY = 1 << 30;

        private readonly Bucket[] wheel;
        private readonly int mask;
        private readonly long MAX_PENDING_TIMER_TASKS;

        private readonly long tickDuration;
        private readonly long baseTime = DateTime.UtcNow.Ticks; //  1 ns = 10 ticks
        private long tick = 0;

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
                throw new ArgumentOutOfRangeException(nameof(ticksPerWheel), $"expected: 0 <  value < {MAX_WHEEL_CAPACITY} ");
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

            if (tickDuration.TotalMilliseconds < 1 || tickDuration.TotalMilliseconds > (long.MaxValue / 10000 / wheel.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(tickDuration), $"expected milliseconds: 0 <  value < {long.MaxValue / 10000 / wheel.Length} ");
            }

            this.tickDuration = tickDuration.Ticks;

            this.logger.LogDebug("Initialized, {0}", this);

            // Running a single thread as the worker-thread in background to spin the wheel.
            // We don't use Task.Run() to get the thread which in the threadPool,
            // because the jobs-queue of threadPool could be filled by many jobs.
            // After a Task.Delay() to wait to next tick, the thread may not back to work on time.
            // So *LongRunning* is necessary, to create a new thread.
            workerThreadTask = Task.Factory.StartNew(SpinTheWheel, cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        }


        private static Bucket[] CreateWheel(int ticksPerWheel)
        {
            ticksPerWheel = NormalizeTicksPerWheel(ticksPerWheel);
            var wheel = new Bucket[ticksPerWheel];
            for (var i = 0; i < wheel.Length; i++)
            {
                wheel[i] = new Bucket();
            }

            return wheel;
        }


        private readonly ConcurrentQueue<BucketNode> newTaskQueue = new ConcurrentQueue<BucketNode>();

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
                throw new InvalidOperationException($"failed to add new task, {nameof(HashedWheelTimer)} has been stopped");
            }

            Interlocked.Increment(ref pendingTasks);
            if (MAX_PENDING_TIMER_TASKS > 0 && pendingTasks > MAX_PENDING_TIMER_TASKS)
            {
                Interlocked.Decrement(ref pendingTasks);
                throw new InvalidOperationException($"failed to add new task, approching limit,  {nameof(MAX_PENDING_TIMER_TASKS)}={MAX_PENDING_TIMER_TASKS}.");
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
            var bucketIndex = (int)(ticks & mask);

            var node = new BucketNode(handle, deadline, rounds, bucketIndex);

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
        /// <returns>return all the unprocessed tasks</returns>
        public async Task<IEnumerable<TimerTaskHandle>> Stop()
        {
            logger.LogTrace($"Stoping {nameof(HashedWheelTimer)} ...");

            if (cancellationTokenSource == null || cancellationTokenSource.Token.IsCancellationRequested)
            {
                return Enumerable.Empty<TimerTaskHandle>();
            }

            try
            {
                cancellationTokenSource.Cancel();

                if (workerThreadTask != null && !workerThreadTask.IsCompleted)
                {
                    logger.LogTrace("Waiting for the worker thread...");
                    // waiting for dumping all the unprocessed tasks 

                    //NETCOREAPP3_1 || NET5_0 || NETSTANDARD2_1 || NETSTANDARD2_0
#if (NETSTANDARD || NETFRAMEWORK || NETCOREAPP || NET5_0)
                    await workerThreadTask.TimeoutAfter(TimeSpan.FromSeconds(5));
#else // (NET6_0 || NET7_0)
                    await workerThreadTask.WaitAsync(TimeSpan.FromSeconds(5)); // from .net 6
#endif

                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "stop and dumping unprocessed tasks timeout");
            }

            logger.LogDebug($"Stoping {nameof(HashedWheelTimer)} ... done");

            return unprocessedTaskQueue;

        }


        private Queue<TimerTaskHandle> unprocessedTaskQueue = new Queue<TimerTaskHandle>();

        private void SpinTheWheel()
        {
            this.logger.LogTrace("Start spinning the wheel...   ManagedThreadId={0}", Thread.CurrentThread.ManagedThreadId);

            CancellationToken token = cancellationTokenSource.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (this.logger.IsEnabled(LogLevel.Trace))
                    {
                        this.logger.LogTrace("{0} >>> ticking --------- {1} -- ManagedThreadId={2} -- IsThreadPoolThread={3}  ",
                            tick, DateTime.Now.ToString("HH:mm:ss.fff"), Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.IsThreadPoolThread);
                    }

                    // firstly transfer new task from queue to buckets
                    var count = TransferNewTaskToBuckets(token);
                    logger.LogTrace("{0} >>> Transfer new tasks from queue to buckets, count = {1}", tick, count);

                    // remove the expired nodes from bucket to unprocess task queue
                    var idx = (int)(tick & mask);
                    count = wheel[idx].RemoveExpiredNodes(ref unprocessedTaskQueue);

                    this.logger.LogTrace("{0} >>> remove the expired nodes from bucket, count = {1}", tick, count);

                    // process task
                    while (!token.IsCancellationRequested && unprocessedTaskQueue.Count > 0) // count of queue is O(1).
                    {
                        //   if (!unprocessedTaskQueue.TryDequeue(out TimerTaskHandle handle)) // from .net 6.0
                        TimerTaskHandle handle = unprocessedTaskQueue.Dequeue();
                        if (handle == null)
                        {
                            break;
                        }

                        Interlocked.Decrement(ref pendingTasks);

                        if (!handle.Cancelled)
                        {
                            _ = handle.TimerTask.RunAsync();
                        }
                    }

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

            logger.LogTrace("Exit loop, dumping unprocessed tasks... ");

            for (int i = (int)(tick & mask); i < wheel.Length; i++)
            {
                wheel[i].RemoveAllNodes(ref unprocessedTaskQueue);
            }

            while (true)
            {
                if (!newTaskQueue.TryDequeue(out BucketNode node))
                {
                    break;
                }
                unprocessedTaskQueue.Enqueue(node.TimerTaskHandle);
            }

            Interlocked.Exchange(ref pendingTasks, 0);

        }

        private int TransferNewTaskToBuckets(CancellationToken cancellationToken)
        {
            int count = 0;

            // to prevent the worker thread stuck in adding new task loop.
            for (var i = 0; i < 60_000; i++)
            {
                if (!newTaskQueue.TryDequeue(out var node) || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (node.TimerTaskHandle.Cancelled)
                {
                    logger.LogTrace(">>> task was cancelled: {0}", node);
                    Interlocked.Decrement(ref pendingTasks);
                    continue;
                }

                var bucket = wheel[node.BucketIndex];
                bucket.AddNode(node);
                count++;
            }

            return count;
        }


        // get max power of 2
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
            cancellationTokenSource?.Dispose();
        }

        public override string ToString()
        {
            return string.Format("[{0}: tickDuration={1}, ticksPerWheel={2}, maxPendingTimerTasks={3}]",
                nameof(HashedWheelTimer), this.tickDuration, this.wheel.Length, this.MAX_PENDING_TIMER_TASKS);
        }

    }
}