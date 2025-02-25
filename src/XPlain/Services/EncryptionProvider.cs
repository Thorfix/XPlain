using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class EncryptionProvider : IEncryptionProvider
    {
        private readonly Dictionary<string, DateTime> _keyRotationSchedule = new Dictionary<string, DateTime>();
        private readonly List<string> _activeKeyIds = new List<string>();
        
        public bool IsEnabled { get; private set; }
        public string CurrentKeyId { get; private set; }
        public DateTime CurrentKeyCreatedAt { get; private set; }
        public DateTime NextScheduledRotation { get; private set; }
        public bool AutoRotationEnabled { get; set; }

        public EncryptionProvider(IOptions<CacheSettings> settings = null)
        {
            // Default initialization with no encryption
            IsEnabled = settings?.Value?.EncryptionEnabled ?? false;
            CurrentKeyId = "default-key-1";
            CurrentKeyCreatedAt = DateTime.UtcNow;
            NextScheduledRotation = DateTime.UtcNow.AddDays(30);
            AutoRotationEnabled = false;
            
            _activeKeyIds.Add(CurrentKeyId);
            _keyRotationSchedule[CurrentKeyId] = NextScheduledRotation;
        }

        public IEnumerable<string> GetActiveKeyIds()
        {
            return _activeKeyIds;
        }

        public Dictionary<string, DateTime> GetKeyRotationSchedule()
        {
            return _keyRotationSchedule;
        }
    }
}