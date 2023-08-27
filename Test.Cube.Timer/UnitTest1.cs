using Cube.Timer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Test.Cube.Timer
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void AddNewTask()
        {
            var loggerFactory = LoggerFactory.Create((builder) =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<HashedWheelTimer>();

            var timer = new HashedWheelTimer(logger);

            timer.AddTask(1357, () =>
              {
                  Debug.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff")} : do work. thread-id={Thread.CurrentThread.ManagedThreadId} ");
              });

            timer.AddTask(TimeSpan.FromMilliseconds(1234), (prm) =>
            {
                Debug.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff")} : do work. parameter={prm}, thread-id={Thread.CurrentThread.ManagedThreadId} ");
            }, 999);

            var t = Task.Run(async () =>
            {
                await Task.Delay(1000);

                while (timer.IsRunning && timer.PendingTasks != 0)
                {
                    Debug.WriteLine($">> PendingTasks : {timer.PendingTasks} ");
                    await Task.Delay(1000);
                }
            });

            t.Wait();

        }

        [Test]
        public void AddNotice()
        {
            var loggerFactory = LoggerFactory.Create((builder) =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<HashedWheelTimer>();

            var timer = new HashedWheelTimer(TimeSpan.FromSeconds(1),512,0, logger);
            
            timer.SetNoticeCallback((list) =>
                {
                    foreach (var obj in list)
                    {
                        if (obj==null)
                        {
                            break;
                        }
                         logger.LogDebug(" +++++ notice callback {}",obj);
                    }
                });


                timer.AddNotice(1357, 1357);

                timer.AddNotice(TimeSpan.FromMilliseconds(1234), 1234);

                var t = Task.Run(async () =>
                {
                    await Task.Delay(1000);

                    while (timer.IsRunning && timer.PendingTasks != 0)
                    {
                        logger.LogDebug($">> PendingTasks : {timer.PendingTasks} ");
                        await Task.Delay(1000);
                    }
                });

            t.Wait();

        }
        
        [Test]
        public void AddNotice2()
        {
            var loggerFactory = LoggerFactory.Create((builder) =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<HashedWheelTimer>();

            Random random = new Random(DateTime.Now.Millisecond);
            var source = new int[1024];
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = random.Next(DateTime.Now.Millisecond);
            }
            
              var result = new List<int>( );
                    
            var timer = new HashedWheelTimer(TimeSpan.FromSeconds(1),512,0, logger);
            
            timer.SetNoticeCallback((notices) =>
            {
                foreach (var obj in notices)
                {
                    if (obj==null)
                    {
                        break;
                    }
                    result.Add(Convert.ToInt32(obj));
                    logger.LogDebug(" +++++ notice callback {}",obj);
                }
            });

            for (int i = 0; i < source.Length; i++)
            {
                timer.AddNotice(random.Next(1000, 10 * 1000),source[i]);
            }
          

            var t = Task.Run(async () =>
            {
                await Task.Delay(1000);

                while (timer.IsRunning && timer.PendingTasks != 0)
                {
                    logger.LogDebug($">> PendingTasks : {timer.PendingTasks} ");
                    await Task.Delay(1000);
                }
            });

            t.Wait();

            Assert.IsTrue( source.Length==result.Count);

            
        }
        

        [Test]
        public void TracingLogger()
        {
            var loggerFactory = LoggerFactory.Create((builder) =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<HashedWheelTimer>();

            var timer = new HashedWheelTimer(logger);

            // add a new task with lambda expression
            var handle = timer.AddTask(1357, () =>
           {
               Debug.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff")} : do work. ");
           });
            // if cancel the task
            handle.Cancel();

            Thread.Sleep(1000);

            Assert.IsTrue(timer.PendingTasks == 0);

            // add a new task with lambda expression, passing parameter
            timer.AddTask(TimeSpan.FromMilliseconds(1234), (prm) =>
             {
                 Debug.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff")} : do work. parameter={prm}");
             }, 999);

            // add a new task which implement ITimerTask
            var handle2 = timer.AddTask(4357, new MyTimerTask());
            if (handle2.Cancelled)
            {
                // if MyTimerTask has been cancelled
            }

            // reuse the timerTask and specify the delayMilliseconds=2000
            timer.AddTask(2000, handle2.TimerTask);

            // stop the timer, and get the unprocessed tasks.
            var unprocessedTasks = timer.Stop(true).Result;

            Assert.IsTrue(unprocessedTasks.Count() == 3);

            Assert.IsFalse(timer.IsRunning);
            Assert.IsTrue(timer.PendingTasks == 0);

        }

        [Test]
        public void TestCtor()
        {
            var timer = new HashedWheelTimer(tickDuration: TimeSpan.FromMilliseconds(100), ticksPerWheel: 512, maxPendingTimerTasks: 0);

            var timer2 = new HashedWheelTimer();

            var timer3 = new HashedWheelTimer(tickDuration: TimeSpan.FromMilliseconds(100));

            var loggerFactory = LoggerFactory.Create((builder) =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<HashedWheelTimer>();

            var timer4 = new HashedWheelTimer(logger);

            var rand = new Random(DateTime.Now.Millisecond);

            Thread.Sleep((int)(rand.NextDouble() * 100));

            Assert.IsTrue(timer.IsRunning);
            Assert.IsTrue(timer2.IsRunning);
            Assert.IsTrue(timer3.IsRunning);
            Assert.IsTrue(timer4.IsRunning);

            timer.Stop().Wait();
            timer2.Stop().Wait();
            timer3.Stop().Wait();
            timer4.Stop().Wait();

            timer.AddTask(100, () =>
            {
                // failed to add new task
            });
        }

        [Test]
        public void TestPrecision()
        {
            int workerThreads, completionPortThreads;

            ThreadPool.GetMaxThreads(out workerThreads, out completionPortThreads);
            Debug.WriteLine("GetMaxThreads£º worker threads={0}, IO threads={1} ", workerThreads, completionPortThreads);

            ThreadPool.GetMinThreads(out workerThreads, out completionPortThreads);
            Debug.WriteLine("GetMinThreads£º worker threads={0}, IO threads={1} ", workerThreads, completionPortThreads);

            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);
            Debug.WriteLine("GetAvailableThreads£º worker threads={0}, IO threads={1} ", workerThreads, completionPortThreads);

            //// -------
            //ThreadPool.SetMaxThreads(80, 512);

            //ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);
            //     Debug.WriteLine("GetAvailableThreads£º worker threads={0}, IO threads={1} ", workerThreads, completionPortThreads);


            var loggerFactory = LoggerFactory.Create((builder) =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddDebug();
                //    builder.AddConsole();
            });

            var logger = loggerFactory.CreateLogger<HashedWheelTimer>();

            Debug.WriteLine($"unit test, thread-id={Thread.CurrentThread.ManagedThreadId}");

            var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(30), logger);

            var counter = 0L;

            var perThread = 500_000;
            var threads = 8;

            for (int k = 0; k < threads; k++)
            {
                var tmp = k;
                Task.Run(async () =>
                {
                    var rand = new Random(tmp);
                    for (int i = 0; i < perThread; i++)
                    {
                        if (i % 50 == 0)
                        {
                            await Task.Delay(10);
                        }

                        int t = (int)(rand.NextDouble() * 100);

                        string msg = $"thread-id={Thread.CurrentThread.ManagedThreadId}, t={t} , index={i} ";

                        timer.AddTask(t, (info) =>
                        {
                            Interlocked.Increment(ref counter);
                            //Debug.WriteLine("run timeout: " + info);
                        }, msg);
                    }
                });
            }


            var t = Task.Run(async () =>
            {
                await Task.Delay(1000);
                var cc = 0;

                while (timer.IsRunning && timer.PendingTasks != 0)
                {
                    cc++;
                    Debug.WriteLine($">> PendingTasks : {timer.PendingTasks}, counter = {counter}");
                    await Task.Delay(1000);

                    //if (cc >= 20)
                    //{
                    //    var result = await timer.Stop();
                    //    Debug.WriteLine($"stop-----{result?.Count()}");
                    //}
                }

                await timer.Stop(true);

            });

            t.Wait();

            Assert.IsTrue(timer.PendingTasks == 0 && counter == perThread * threads);


        }

        [Test]
        public void TestExecuteException()
        {
            var loggerFactory = LoggerFactory.Create((builder) =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<HashedWheelTimer>();

            var timer = new HashedWheelTimer(logger);

            timer.AddTask(1000, () =>
            {
                Debug.WriteLine($" before do work. ");
                // devide by zero
                int a = 9;
                int b = 8;
                int c = a / (b - 8);
                Debug.WriteLine($"  {a} ");
            });

            timer.AddTask(3000, () =>
                       {
                           Debug.WriteLine($" do work. ");
                       });


            while (timer.PendingTasks > 0)
            {
                Thread.Sleep(600);
            }


        }

 

    }
}