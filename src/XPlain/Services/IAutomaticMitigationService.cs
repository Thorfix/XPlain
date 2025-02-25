using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface IAutomaticMitigationService
    {
        Task<bool> ApplyMitigationsAsync();
        Task<List<MitigationAction>> GetActiveMitigationsAsync();
        Task<List<MitigationAction>> GetMitigationHistoryAsync(TimeSpan period);
        Task<bool> EnableAutomaticMitigationsAsync(bool enable);
        Task<bool> IsAutomaticMitigationEnabledAsync();
    }

    public class MitigationAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Type { get; set; }
        public string Description { get; set; }
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public DateTime? ExpiresAt { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        public string ResultStatus { get; set; } = "Pending";
    }
}