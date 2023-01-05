using System;
using System.Threading.Tasks;

namespace Cube.Timer
{
    internal sealed class LambdaTimerTask : ITimerTask
    {
        private readonly Action action0;
        private readonly Action<object> action1;
        private readonly object args;

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
