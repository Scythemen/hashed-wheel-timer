using System;

namespace Cube.Timer
{
    /// <summary>
    /// Represents the handle of the timer task
    /// </summary>
    public sealed class TimerTaskHandle
    {
        private static readonly int STATUS_INIT = 0;
        private static readonly int STATUS_CANCELLED = 1;

        private int _status;
        private readonly long _expireAt;
        private readonly ITimerTask _timerTask;

        /// <summary>
        /// Get the timer task which will be executed in the future.
        /// </summary>
        public ITimerTask TimerTask => _timerTask;

        /// <summary>
        ///   Gets a value that indicates whether the task is expired.
        /// </summary>
        public bool Expired => DateTime.UtcNow.Ticks >= _expireAt;

        /// <summary>
        ///   Gets a value that indicates whether the task has been cancelled.
        /// </summary>
        public bool Cancelled => _status == STATUS_CANCELLED;

        /// <summary>
        /// the handle of timer task 
        /// </summary>
        /// <param name="timerTask">the timer task</param>
        /// <param name="expireAt">the task will expire at the future </param>
        public TimerTaskHandle(ITimerTask timerTask, long expireAt)
        {
            _timerTask = timerTask;
            _expireAt = expireAt;
            _status = STATUS_INIT;
        }

        /// <summary>
        /// Cancel the task
        /// </summary>
        public void Cancel()
        {
            _status = STATUS_CANCELLED;
        }

    }
}