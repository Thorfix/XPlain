using System;
using System.Collections.Generic;

namespace XPlain.Services
{
    public interface IEncryptionProvider
    {
        bool IsEnabled { get; }
        string CurrentKeyId { get; }
        DateTime CurrentKeyCreatedAt { get; }
        DateTime NextScheduledRotation { get; }
        bool AutoRotationEnabled { get; set; }
        
        IEnumerable<string> GetActiveKeyIds();
        Dictionary<string, DateTime> GetKeyRotationSchedule();
    }
}