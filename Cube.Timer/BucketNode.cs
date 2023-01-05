using System;

namespace Cube.Timer
{
    internal sealed class BucketNode : IDisposable
    {
        public long Deadline { get; private set; }
        public long BucketIndex { get; private set; }
        public long RemainingRounds { get; private set; }
        public ITimerTask TimerTask { get; private set; }
        public TimerTaskHandle TimerTaskHandle { get; private set; }

        public BucketNode Next { get; set; }
        public BucketNode Prev { get; set; }


        public BucketNode(TimerTaskHandle timerTaskHandler, long deadline, long remainingRounds, long bucketIndex)
        {
            this.TimerTask = timerTaskHandler.TimerTask;
            this.TimerTaskHandle = timerTaskHandler;
            this.Deadline = deadline;
            this.RemainingRounds = remainingRounds;
            this.BucketIndex = bucketIndex;
        }

        public void DecreaseRemainingRounds()
        {
            this.RemainingRounds--;
        }

        public override string ToString()
        {
            return string.Format("[{0}: bucketIndex={1}, deadline={2}, remainingRounds={3}]", 
                nameof(BucketNode), BucketIndex, Deadline, RemainingRounds);
        }

        public void Dispose()
        {
            Next = null;
            Prev = null;
            TimerTask = null;
            TimerTaskHandle = null;
        }

    }
}