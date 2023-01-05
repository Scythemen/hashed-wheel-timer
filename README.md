# hashed-wheel-timer
A .NET implementation of Timer, which optimized for approximated I/O timeout scheduling, also called Approximated Timer.

Inspired by Netty [HashedWheelTimer](https://github.com/netty/netty/blob/4.1/common/src/main/java/io/netty/util/HashedWheelTimer.java)

# Install

NuGet package

```shell
https://www.nuget.org/packages/Cube.Timer
```

# Usage

*More details in the test-project*

```csharp 
// constructor
public HashedWheelTimer(
    TimeSpan tickDuration,
    int ticksPerWheel = 512,
    long maxPendingTimerTasks = 0,
    ILogger<HashedWheelTimer> logger = null)
```

Create an instance of timer, then add new `timer-task`.

```csharp

var timer = new HashedWheelTimer(logger);

// add a new task with lambda expression, delay 1357ms.
var handle = timer.AddTask(1357, () =>
{
    Debug.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff")} : do work. ");
});

// check the status of task
if(handle.Cancelled || handle.Expired) {  }

// task can be cancelled, call the method.
handle.Cancel();

// reuse the timerTask
timer.AddTask(2000, handle.TimerTask);

// add a new task with lambda expression, passing parameter=999
timer.AddTask(TimeSpan.FromMilliseconds(1234), (prm) =>
{
    Debug.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff")} : do work. parameter={prm}");
}, 999);


// add a new task which instant class implements ITimerTask
timer.AddTask(4357, new MyTimerTask());

 
// stop the timer, and get the unprocessed tasks.
IEnumerable<TimerTaskHandle> unprocessedTasks = await timer.Stop();

```

Implement the ITimerTask

```csharp
public interface ITimerTask
{
    /// <summary>
    /// If it takes a long time to complete the task, consider to start a new thread. 
    /// </summary>
    /// <returns></returns>
    Task RunAsync();
}


public class MyTimerTask : ITimerTask
{
    public Task RunAsync()
    {
        Debug.WriteLine($"do work.");
        return Task.CompletedTask;
    }
}

```


