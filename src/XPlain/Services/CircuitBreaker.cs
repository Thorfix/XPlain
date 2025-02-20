namespace XPlain.Services;

public class CircuitBreaker
{
    private readonly object _lock = new();
    private readonly double _failureThreshold;
    private readonly int _resetTimeoutMs;
    private CircuitState _state = CircuitState.Closed;
    private DateTime _lastStateChange = DateTime.UtcNow;
    private int _totalRequests;
    private int _failedRequests;
    private readonly Queue<DateTime> _recentFailures = new();

    public CircuitBreaker(double failureThreshold, int resetTimeoutMs)
    {
        _failureThreshold = failureThreshold;
        _resetTimeoutMs = resetTimeoutMs;
    }

    public bool CanProcess()
    {
        lock (_lock)
        {
            CleanupOldFailures();

            switch (_state)
            {
                case CircuitState.Closed:
                    return true;
                case CircuitState.Open:
                    if ((DateTime.UtcNow - _lastStateChange).TotalMilliseconds >= _resetTimeoutMs)
                    {
                        _state = CircuitState.HalfOpen;
                        _lastStateChange = DateTime.UtcNow;
                        return true;
                    }
                    return false;
                case CircuitState.HalfOpen:
                    return true;
                default:
                    return false;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Closed;
                _lastStateChange = DateTime.UtcNow;
                _totalRequests = 0;
                _failedRequests = 0;
                _recentFailures.Clear();
            }
            _totalRequests++;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _totalRequests++;
            _failedRequests++;
            _recentFailures.Enqueue(DateTime.UtcNow);

            if (_state == CircuitState.HalfOpen ||
                (_state == CircuitState.Closed && (double)_failedRequests / _totalRequests >= _failureThreshold))
            {
                _state = CircuitState.Open;
                _lastStateChange = DateTime.UtcNow;
            }
        }
    }

    private void CleanupOldFailures()
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-_resetTimeoutMs);
        while (_recentFailures.Count > 0 && _recentFailures.Peek() < cutoff)
        {
            _recentFailures.Dequeue();
            if (_failedRequests > 0) _failedRequests--;
            if (_totalRequests > 0) _totalRequests--;
        }
    }

    private enum CircuitState
    {
        Closed,
        Open,
        HalfOpen
    }
}