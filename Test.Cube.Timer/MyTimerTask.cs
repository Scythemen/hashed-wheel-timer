using Cube.Timer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test.Cube.Timer
{
    internal class MyTimerTask : ITimerTask
    {
        public string Id { get; private set; }

        public MyTimerTask()
        {
            Id = DateTime.Now.Ticks.ToString();
        }

        public Task RunAsync()
        {
            Debug.WriteLine($"[{Id}], {DateTime.Now.ToString("HH:mm:ss")}, {nameof(MyTimerTask)} do work.");

            return Task.CompletedTask;
        }

    }
}
