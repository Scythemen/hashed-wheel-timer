using Cube.Timer;

namespace Test.Cube.Timer;

public class HeavyTimerTask : ITimerTask
{
    public Task RunAsync()
    {
        // Console.WriteLine(
        //     $"threadId={Thread.CurrentThread.ManagedThreadId}, fromThreadPool={Thread.CurrentThread.IsThreadPoolThread} ,{DateTime.Now.ToString("HH:mm:ss.fff")}, HeavyTimerTask Start. ");
        //
        // Task.Delay(7000).Wait();
        //
        // Console.WriteLine(
        //     $"threadId={Thread.CurrentThread.ManagedThreadId}, fromThreadPool={Thread.CurrentThread.IsThreadPoolThread} ,{DateTime.Now.ToString("HH:mm:ss.fff")}, HeavyTimerTask Finished. ");
        //
        // return Task.CompletedTask;

        return Task.Run(() =>
        {
            Console.WriteLine(
                $"threadId={Thread.CurrentThread.ManagedThreadId}, fromThreadPool={Thread.CurrentThread.IsThreadPoolThread} ,{DateTime.Now.ToString("HH:mm:ss.fff")}, HeavyTimerTask Start. ");

            Task.Delay(7000).Wait();

            Console.WriteLine(
                $"threadId={Thread.CurrentThread.ManagedThreadId}, fromThreadPool={Thread.CurrentThread.IsThreadPoolThread} ,{DateTime.Now.ToString("HH:mm:ss.fff")}, HeavyTimerTask Finished. ");
        });
    }
}