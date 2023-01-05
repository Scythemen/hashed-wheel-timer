using System.Threading.Tasks;

namespace Cube.Timer
{
    /// <summary>
    /// Represents a timer task which will be executed in the future.
    /// </summary>
    public interface ITimerTask
    {
        /// <summary>
        /// If it takes a long time to complete the task, consider to start a new thread. 
        /// </summary>
        /// <returns></returns>
        Task RunAsync();
    }
}