using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Options;
using XPlain.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace XPlain.Services
{
    public class FileBasedCacheProvider : ICacheProvider
    {
        private readonly CacheSettings _settings;
        private readonly string _cacheDirectory;
        private long _cacheHits;
        private long _cacheMisses;
        private readonly object _statsLock = new();
        private readonly ConcurrentDictionary<string, string> _keyToFileMap = new();
        private readonly ILLMProvider _llmProvider;

        public FileBasedCacheProvider(IOptions<CacheSettings> settings, ILLMProvider llmProvider)
        {
            _settings = settings.Value;
            _llmProvider = llmProvider;
            _cacheDirectory = _settings.CacheDirectory ?? Path.Combine(AppContext.BaseDirectory, "cache");
            Directory.CreateDirectory(_cacheDirectory);
        }

        private string GetFilePath(string key)
        {
            return _keyToFileMap.GetOrAdd(key, k =>
            {
                var safeKey = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(k)));
                return Path.Combine(_cacheDirectory, $"{safeKey}.json");
            });
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            if (!_settings.CacheEnabled) return null;

            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                IncrementMisses();
                return null;
            }

            try
            {
                var cacheEntry = await JsonSerializer.DeserializeAsync<CacheEntry<T>>(
                    File.OpenRead(filePath));

                if (cacheEntry == null || cacheEntry.IsExpired)
                {
                    await RemoveAsync(key);
                    IncrementMisses();
                    return null;
                }

                IncrementHits();
                return cacheEntry.Value;
            }
            catch
            {
                await RemoveAsync(key);
                IncrementMisses();
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
                ExpirationTime = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromHours(_settings.CacheExpirationHours)),
                CodeHash = await CalculateCodeHashAsync()
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
                _keyToFileMap.TryRemove(key, out _);
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key)
        {
            if (!_settings.CacheEnabled) return Task.FromResult(false);
            return Task.FromResult(File.Exists(GetFilePath(key)));
        }

        public async Task InvalidateOnCodeChangeAsync(string newCodeHash)
        {
            if (!_settings.CacheEnabled) return;

            var cacheFiles = Directory.GetFiles(_cacheDirectory, "*.json");
            foreach (var file in cacheFiles)
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var cacheEntry = await JsonSerializer.DeserializeAsync<CacheEntry<object>>(stream);
                    
                    if (cacheEntry?.CodeHash != newCodeHash)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // If we can't read the file, delete it
                    File.Delete(file);
                }
            }
        }

        public async Task WarmupCacheAsync(string[] frequentQuestions, string codeContext)
        {
            if (!_settings.CacheEnabled) return;

            foreach (var question in frequentQuestions)
            {
                var key = GetCacheKey(question, codeContext);
                if (!await ExistsAsync(key))
                {
                    var response = await _llmProvider.GetCompletionAsync(
                        $"I have the following code:\n\n{codeContext}\n\nMy question is: {question}");
                    await SetAsync(key, response);
                }
            }
        }

        private string GetCacheKey(string question, string codeContext)
        {
            var input = $"{question}:{codeContext}";
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        }

        private async Task<string> CalculateCodeHashAsync()
        {
            using var sha256 = SHA256.Create();
            var hashBuilder = new StringBuilder();

            foreach (var file in Directory.GetFiles(_settings.CodebasePath, "*.*", SearchOption.AllDirectories))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                    hashBuilder.Append(Convert.ToBase64String(hash));
                }
                catch
                {
                    // Skip files that can't be read
                    continue;
                }
            }

            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(hashBuilder.ToString())));
        }

        public (long hits, long misses) GetCacheStats()
        {
            lock (_statsLock)
            {
                return (_cacheHits, _cacheMisses);
            }
        }

        private void IncrementHits()
        {
            lock (_statsLock)
            {
                _cacheHits++;
            }
        }

        private void IncrementMisses()
        {
            lock (_statsLock)
            {
                _cacheMisses++;
            }
        }

        private class CacheEntry<T>
        {
            public T Value { get; set; } = default!;
            public DateTime ExpirationTime { get; set; }
            public string CodeHash { get; set; } = string.Empty;
            public bool IsExpired => DateTime.UtcNow > ExpirationTime;
        }
    }
}