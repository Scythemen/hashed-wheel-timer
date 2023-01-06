using System;
using System.Threading.Tasks;

namespace Cube.Timer
{
    internal sealed class LambdaTimerTask : ITimerTask
    {
        private readonly Action action0 = null;
        private readonly Action<object> action1 = null;
        private readonly object args = null;

        public LambdaTimerTask(Action action)
        {
            this.action0 = action;
        }

        public LambdaTimerTask(Action<object> action, object args)
        {
            this.action1 = action;
            this.args = args;
        }

        public Task RunAsync()
        {
            action0?.Invoke();
            action1?.Invoke(this.args);
            return Task.CompletedTask;
        }

    }

}
