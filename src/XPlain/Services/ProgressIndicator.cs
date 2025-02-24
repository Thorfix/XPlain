using System;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class ProgressIndicator : IDisposable
    {
        private readonly string[] _indicators;
        private readonly int _delay;
        private readonly CancellationTokenSource _cts;
        private Task _task;
        private bool _isDisposed;

        public ProgressIndicator(string[] indicators = null, int delay = 100)
        {
            _indicators = indicators ?? new[] { "⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷" };
            _delay = delay;
            _cts = new CancellationTokenSource();
        }

        public void Start()
        {
            if (_task != null) return;

            _task = Task.Run(async () =>
            {
                if (Console.IsOutputRedirected) return;

                var index = 0;
                while (!_cts.Token.IsCancellationRequested)
                {
                    var indicator = _indicators[index++ % _indicators.Length];
                    Console.Write(indicator);
                    await Task.Delay(_delay, _cts.Token);
                    Console.Write("\b \b");
                }
            });
        }

        public void Stop()
        {
            _cts.Cancel();
            if (_task != null && !_task.IsCompleted)
            {
                _task.Wait(500); // Wait for task to complete, but don't wait forever
            }
            _task = null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Stop();
            _cts.Dispose();
            _isDisposed = true;
        }
    }
}