using System;
using System.Threading;

namespace XPlain.Services
{
    public enum CircuitState
    {
        Closed,     // Normal operation - requests flow through
        Open,       // Failing - requests are blocked
        HalfOpen    // Testing - limited requests allowed
    }

    public class CircuitBreaker
    {
        private readonly int _maxFailures;
        private readonly TimeSpan _resetTimeout;
        private volatile CircuitState _state = CircuitState.Closed;
        private int _failureCount;
        private DateTime _lastStateChange = DateTime.UtcNow;
        private DateTime _nextRetryTime = DateTime.MaxValue;
        private readonly object _syncLock = new();

        public CircuitBreaker(
            int maxFailures = 3, 
            TimeSpan? resetTimeout = null)
        {
            _maxFailures = maxFailures > 0 ? maxFailures : throw new ArgumentOutOfRangeException(nameof(maxFailures));
            _resetTimeout = resetTimeout ?? TimeSpan.FromMinutes(1);
        }

        public CircuitBreaker(
            double failureThreshold = 0.7,
            int resetTimeoutMs = 30000)
        {
            _maxFailures = (int)Math.Max(1, Math.Ceiling(10 * failureThreshold));
            _resetTimeout = TimeSpan.FromMilliseconds(resetTimeoutMs);
        }

        public CircuitState CurrentState => _state;
        public int FailureCount => _failureCount;
        public DateTime LastStateChange => _lastStateChange;
        public DateTime NextRetryTime => _nextRetryTime;

        public bool IsAllowed()
        {
            return CanProcess();
        }

        public bool CanProcess()
        {
            lock (_syncLock)
            {
                switch (_state)
                {
                    case CircuitState.Closed:
                        return true;

                    case CircuitState.Open:
                        if (DateTime.UtcNow >= _nextRetryTime)
                        {
                            TransitionToHalfOpen();
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
            lock (_syncLock)
            {
                switch (_state)
                {
                    case CircuitState.HalfOpen:
                        Reset();
                        break;
                }
            }
        }

        public void RecordFailure()
        {
            lock (_syncLock)
            {
                switch (_state)
                {
                    case CircuitState.Closed:
                        _failureCount++;
                        if (_failureCount >= _maxFailures)
                        {
                            TransitionToOpen();
                        }
                        break;

                    case CircuitState.HalfOpen:
                        TransitionToOpen();
                        break;
                }
            }
        }

        public void OnFailure()
        {
            RecordFailure();
        }

        public void OnSuccess()
        {
            RecordSuccess();
        }

        private void TransitionToOpen()
        {
            _state = CircuitState.Open;
            _lastStateChange = DateTime.UtcNow;
            _nextRetryTime = DateTime.UtcNow.Add(_resetTimeout);
        }

        private void TransitionToHalfOpen()
        {
            _state = CircuitState.HalfOpen;
            _lastStateChange = DateTime.UtcNow;
        }

        private void Reset()
        {
            _state = CircuitState.Closed;
            _failureCount = 0;
            _lastStateChange = DateTime.UtcNow;
            _nextRetryTime = DateTime.MaxValue;
        }
    }
}