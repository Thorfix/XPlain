using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public class CircuitBreaker
    {
        private readonly int _maxFailures;
        private readonly TimeSpan _resetTimeout;
        private int _failureCount;
        private DateTime _lastFailure = DateTime.MinValue;
        private readonly object _syncLock = new();
        private bool _isOpen;

        public CircuitBreaker(int maxFailures = 3, TimeSpan? resetTimeout = null)
        {
            _maxFailures = maxFailures;
            _resetTimeout = resetTimeout ?? TimeSpan.FromMinutes(5);
        }

        public CircuitBreaker(double failureThreshold, int resetTimeoutMs)
        {
            _maxFailures = (int)(1.0 / (1.0 - failureThreshold));
            _resetTimeout = TimeSpan.FromMilliseconds(resetTimeoutMs);
        }

        public bool CanProcess()
        {
            lock (_syncLock)
            {
                if (_isOpen && DateTime.UtcNow - _lastFailure > _resetTimeout)
                {
                    // Auto-reset after timeout
                    _isOpen = false;
                    _failureCount = 0;
                }
                return !_isOpen;
            }
        }

        public bool IsAllowed()
        {
            return CanProcess();
        }

        public void RecordSuccess()
        {
            lock (_syncLock)
            {
                _failureCount = 0;
                _isOpen = false;
            }
        }

        public void RecordFailure()
        {
            lock (_syncLock)
            {
                _failureCount++;
                _lastFailure = DateTime.UtcNow;
                if (_failureCount >= _maxFailures)
                {
                    _isOpen = true;
                }
            }
        }

        public void OnSuccess()
        {
            RecordSuccess();
        }

        public void OnFailure()
        {
            RecordFailure();
        }
    }
}