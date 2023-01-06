using System;

namespace Cube.Timer
{
    internal sealed class TaskEntry : IDisposable
    {
        public long Deadline { get; private set; }
        public long WheelIndex { get; private set; }
        public long RemainingRounds { get; private set; }
        public ITimerTask TimerTask { get; private set; }
        public TimerTaskHandle TimerTaskHandle { get; private set; }

        public TaskEntry Next { get; set; }

        public TaskEntry(TimerTaskHandle timerTaskHandler, long deadline, long remainingRounds, long slotIndex)
        {
            this.TimerTask = timerTaskHandler.TimerTask;
            this.TimerTaskHandle = timerTaskHandler;
            this.Deadline = deadline;
            this.RemainingRounds = remainingRounds;
            this.WheelIndex = slotIndex;
        }

        public void DecreaseRemainingRounds()
        {
            this.RemainingRounds--;
        }

        public override string ToString()
        {
            return string.Format("[{0}: wheelIndex={1}, deadline={2}, remainingRounds={3}]",
                nameof(TaskEntry), WheelIndex, Deadline, RemainingRounds);
        }

        public void Dispose()
        {
            Next = null;
            TimerTask = null;
            TimerTaskHandle = null;
        }

    }
}