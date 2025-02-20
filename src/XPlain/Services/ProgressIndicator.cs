namespace XPlain.Services;

public class ProgressIndicator : IDisposable
{
    private readonly Timer _timer;
    private readonly string[] _frames;
    private int _currentFrame;
    private bool _isFirst = true;
    private readonly int _updateIntervalMs;
    private bool _disposed;

    public ProgressIndicator(int updateIntervalMs = 100)
    {
        _updateIntervalMs = updateIntervalMs;
        _frames = new[] { "⣾", "⣽", "⣻", "⢿", "⡿", "⣟", "⣯", "⣷" };
        _timer = new Timer(UpdateFrame, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_disposed) return;
        _timer.Change(0, _updateIntervalMs);
    }

    public void Stop()
    {
        if (_disposed) return;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        ClearLastFrame();
    }

    private void UpdateFrame(object? state)
    {
        if (_disposed || Console.IsOutputRedirected) return;

        try
        {
            if (_isFirst)
            {
                _isFirst = false;
                Console.Write(_frames[0]);
            }
            else
            {
                Console.Write("\b");
                Console.Write(_frames[_currentFrame]);
            }

            _currentFrame = (_currentFrame + 1) % _frames.Length;
        }
        catch
        {
            // Ignore console errors
        }
    }

    private void ClearLastFrame()
    {
        if (Console.IsOutputRedirected) return;

        try
        {
            if (!_isFirst)
            {
                Console.Write("\b \b");
            }
        }
        catch
        {
            // Ignore console errors
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        Stop();
        _timer.Dispose();
    }
}