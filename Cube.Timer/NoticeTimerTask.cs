using System.Threading.Tasks;

namespace Cube.Timer
{
    internal sealed class NoticeTimerTask : ITimerTask
    {
        private object _obj;
        public object Notice => _obj;

        public NoticeTimerTask(object obj)
        {
            _obj = obj;
        }

        public Task RunAsync()
        {
            return Task.CompletedTask;
        }
    }
}