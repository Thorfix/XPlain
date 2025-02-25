using System;
using System.Threading;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public class ProgressIndicator : IDisposable
    {
        private readonly string[] _frames = { "⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷" };
        private int _frameIndex = 0;
        private Timer _timer;
        private bool _isRunning = false;
        private readonly object _lock = new object();

        public ProgressIndicator()
        {
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;

                _isRunning = true;
                _timer = new Timer(UpdateFrame, null, 0, 100);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;

                _timer?.Dispose();
                _timer = null;
                _isRunning = false;

                if (!Console.IsOutputRedirected)
                {
                    Console.Write("\b \b");
                }
            }
        }

        private void UpdateFrame(object state)
        {
            if (Console.IsOutputRedirected) return;

            lock (_lock)
            {
                if (!_isRunning) return;

                int currentLeft = Console.CursorLeft;
                if (currentLeft > 0)
                {
                    Console.Write("\b");
                }
                Console.Write(_frames[_frameIndex]);
                _frameIndex = (_frameIndex + 1) % _frames.Length;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}