using System;

namespace XPlain.Services
{
    public class CircuitBreaker
    {
        private readonly int _maxFailures;
        private readonly TimeSpan _resetTimeout;
        private int _failureCount;
        private DateTime _lastFailureTime;
        private DateTime _lastStateChange;
        private CircuitState _state;

        public enum CircuitState
        {
            Closed,
            Open,
            HalfOpen
        }

        public CircuitState CurrentState => _state;
        public DateTime LastStateChange => _lastStateChange;
        public int FailureCount => _failureCount;
        public DateTime NextRetryTime => _lastFailureTime.Add(_resetTimeout);

        public CircuitBreaker(int maxFailures, TimeSpan resetTimeout)
        {
            _maxFailures = maxFailures;
            _resetTimeout = resetTimeout;
            _state = CircuitState.Closed;
            _lastStateChange = DateTime.UtcNow;
        }

        public CircuitBreaker(int maxFailures, int resetTimeoutMs) 
            : this(maxFailures, TimeSpan.FromMilliseconds(resetTimeoutMs))
        {
        }

        public CircuitBreaker(double failureThreshold, int resetTimeoutMs)
            : this((int)(failureThreshold * 10), TimeSpan.FromMilliseconds(resetTimeoutMs))
        {
        }

        public bool IsAllowed()
        {
            return CanProcess();
        }

        public bool CanProcess()
        {
            if (_state == CircuitState.Open)
            {
                // Check if the timeout has elapsed since the last failure
                if (DateTime.UtcNow - _lastFailureTime > _resetTimeout)
                {
                    // Move to half-open state to test if the issue is resolved
                    SetState(CircuitState.HalfOpen);
                    return true;
                }
                return false;
            }
            
            return true;
        }

        public void OnSuccess()
        {
            RecordSuccess();
        }

        public void RecordSuccess()
        {
            if (_state == CircuitState.HalfOpen)
            {
                // Reset the failure count and return to closed state on success
                _failureCount = 0;
                SetState(CircuitState.Closed);
            }
        }

        public void OnFailure()
        {
            RecordFailure();
        }

        public void RecordFailure()
        {
            _lastFailureTime = DateTime.UtcNow;
            
            if (_state == CircuitState.HalfOpen)
            {
                // Immediately trip back to open state on a failure in half-open state
                SetState(CircuitState.Open);
                return;
            }
            
            _failureCount++;
            if (_failureCount >= _maxFailures)
            {
                SetState(CircuitState.Open);
            }
        }
        
        private void SetState(CircuitState newState)
        {
            if (_state != newState)
            {
                _state = newState;
                _lastStateChange = DateTime.UtcNow;
            }
        }
    }
}