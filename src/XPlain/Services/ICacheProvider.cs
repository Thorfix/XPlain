using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface ICacheProvider
    {
        Task<bool> IsKeyFresh(string key);
        Task PreWarmKey(string key);
        Task AddEventListener(ICacheEventListener listener);
        Task RemoveEventListener(ICacheEventListener listener);
    }
}