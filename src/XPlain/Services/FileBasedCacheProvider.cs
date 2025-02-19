using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class FileBasedCacheProvider : ICacheProvider
    {
        private readonly CacheSettings _settings;
        private readonly string _cacheDirectory;

        public FileBasedCacheProvider(IOptions<CacheSettings> settings)
        {
            _settings = settings.Value;
            _cacheDirectory = _settings.CacheDirectory ?? Path.Combine(AppContext.BaseDirectory, "cache");
            Directory.CreateDirectory(_cacheDirectory);
        }

        private string GetFilePath(string key)
        {
            var safeKey = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(key));
            return Path.Combine(_cacheDirectory, $"{safeKey}.json");
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            if (!_settings.CacheEnabled) return null;

            var filePath = GetFilePath(key);
            if (!File.Exists(filePath)) return null;

            try
            {
                var cacheEntry = await JsonSerializer.DeserializeAsync<CacheEntry<T>>(
                    File.OpenRead(filePath));

                if (cacheEntry == null || cacheEntry.IsExpired)
                {
                    await RemoveAsync(key);
                    return null;
                }

                return cacheEntry.Value;
            }
            catch
            {
                await RemoveAsync(key);
                return null;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
        {
            if (!_settings.CacheEnabled) return;

            var filePath = GetFilePath(key);
            var cacheEntry = new CacheEntry<T>
            {
                Value = value,
                ExpirationTime = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromHours(_settings.CacheExpirationHours))
            };

            using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, cacheEntry);
        }

        public Task RemoveAsync(string key)
        {
            var filePath = GetFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key)
        {
            if (!_settings.CacheEnabled) return Task.FromResult(false);
            return Task.FromResult(File.Exists(GetFilePath(key)));
        }

        private class CacheEntry<T>
        {
            public T Value { get; set; } = default!;
            public DateTime ExpirationTime { get; set; }
            public bool IsExpired => DateTime.UtcNow > ExpirationTime;
        }
    }
}