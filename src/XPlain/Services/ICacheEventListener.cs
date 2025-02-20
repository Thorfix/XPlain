using System;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ICacheEventListener
    {
        Task OnCacheAccess(string key, double responseTime, bool isHit);
        Task OnCacheEviction(string key);
        Task OnCachePreWarm(string key, bool success);
    }
}