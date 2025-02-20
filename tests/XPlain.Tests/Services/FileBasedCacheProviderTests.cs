using System;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using XPlain.Configuration;
using XPlain.Services;

namespace XPlain.Tests.Services
{
    public class FileBasedCacheProviderTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly Mock<ILLMProvider> _llmProviderMock;
        private readonly CacheSettings _defaultSettings;
        private readonly FileBasedCacheProvider _cacheProvider;

        public FileBasedCacheProviderTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"XPlainTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);

            _defaultSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                EncryptionEnabled = true,
                EncryptionAlgorithm = "AES",
                CacheExpirationHours = 24,
                AllowUnencryptedLegacyFiles = false,
                CodebasePath = _testDirectory
            };

            _llmProviderMock = new Mock<ILLMProvider>();
            var settingsOptions = Options.Create(_defaultSettings);
            _cacheProvider = new FileBasedCacheProvider(settingsOptions, _llmProviderMock.Object);
        }

        [Fact]
        public async Task GetAsync_NonExistentKey_ReturnsNull()
        {
            // Act
            var result = await _cacheProvider.GetAsync<string>("nonexistentkey");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SetAndGetAsync_EncryptedData_WorksCorrectly()
        {
            // Arrange
            var key = "testKey";
            var value = "testValue";

            // Act
            await _cacheProvider.SetAsync(key, value);
            var result = await _cacheProvider.GetAsync<string>(key);

            // Assert
            Assert.Equal(value, result);
        }

        [Fact]
        public async Task RemoveAsync_ExistingKey_RemovesEntry()
        {
            // Arrange
            var key = "testKey";
            var value = "testValue";
            await _cacheProvider.SetAsync(key, value);

            // Act
            await _cacheProvider.RemoveAsync(key);
            var result = await _cacheProvider.GetAsync<string>(key);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_ExpiredEntry_ReturnsNull()
        {
            // Arrange
            var key = "testKey";
            var value = "testValue";
            await _cacheProvider.SetAsync(key, value, TimeSpan.FromMilliseconds(1));
            
            // Wait for expiration
            await Task.Delay(50);

            // Act
            var result = await _cacheProvider.GetAsync<string>(key);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task CacheStats_TracksHitsAndMisses()
        {
            // Arrange
            var key = "testKey";
            var value = "testValue";

            // Act - Generate a miss
            var miss = await _cacheProvider.GetAsync<string>(key);
            
            // Generate a hit
            await _cacheProvider.SetAsync(key, value);
            var hit = await _cacheProvider.GetAsync<string>(key);

            // Get stats
            var stats = _cacheProvider.GetCacheStats();

            // Assert
            Assert.Equal(1, stats.Hits);
            Assert.Equal(1, stats.Misses);
        }

        [Fact]
        public async Task InvalidateOnCodeChangeAsync_InvalidatesEntries()
        {
            // Arrange
            var key = "testKey";
            var value = "testValue";
            await _cacheProvider.SetAsync(key, value);

            // Act
            await _cacheProvider.InvalidateOnCodeChangeAsync("newCodeHash");
            var result = await _cacheProvider.GetAsync<string>(key);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ExistsAsync_ExistingKey_ReturnsTrue()
        {
            // Arrange
            var key = "testKey";
            var value = "testValue";
            await _cacheProvider.SetAsync(key, value);

            // Act
            var exists = await _cacheProvider.ExistsAsync(key);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public async Task WarmupCacheAsync_PopulatesCache()
        {
            // Arrange
            var questions = new[] { "test question" };
            var codeContext = "test context";
            _llmProviderMock.Setup(x => x.GetCompletionAsync(It.IsAny<string>()))
                .ReturnsAsync("test response");

            // Act
            await _cacheProvider.WarmupCacheAsync(questions, codeContext);

            // Assert
            var key = Convert.ToBase64String(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{questions[0]}:{codeContext}")));
            var exists = await _cacheProvider.ExistsAsync(key);
            Assert.True(exists);
        }

        [Fact]
        public async Task LogQueryStatsAsync_UpdatesStats()
        {
            // Arrange
            var queryType = "test";
            var query = "test query";
            var responseTime = 100.0;

            // Act
            await _cacheProvider.LogQueryStatsAsync(queryType, query, responseTime, true);
            var stats = _cacheProvider.GetCacheStats();

            // Assert
            Assert.True(stats.QueryTypeStats.ContainsKey(queryType));
            Assert.Equal(1, stats.QueryTypeStats[queryType]);
        }

        [Fact]
        public async Task EncryptionAndDecryption_WorksCorrectly()
        {
            // Arrange
            var key = "encryptedKey";
            var sensitiveData = "sensitive information";

            // Act
            await _cacheProvider.SetAsync(key, sensitiveData);
            
            // Verify file content is encrypted
            var filePath = Path.Combine(_testDirectory, 
                Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
            var fileContent = await File.ReadAllTextAsync(filePath);
            
            // Assert
            Assert.DoesNotContain(sensitiveData, fileContent); // Data should be encrypted
            var retrievedData = await _cacheProvider.GetAsync<string>(key);
            Assert.Equal(sensitiveData, retrievedData); // But should decrypt correctly
        }

        [Fact]
        public async Task ConcurrentOperations_HandleCorrectly()
        {
            // Arrange
            var key = "concurrentKey";
            var tasks = new List<Task>();
            var iterations = 100;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                tasks.Add(_cacheProvider.SetAsync(key, $"value{i}"));
                tasks.Add(_cacheProvider.GetAsync<string>(key));
            }

            // Assert
            await Task.WhenAll(tasks);
            Assert.True(await _cacheProvider.ExistsAsync(key));
        }

        [Fact]
        public async Task WriteAheadLogging_PreservesDataOnFailure()
        {
            // Arrange
            var key = "walKey";
            var value = "walValue";

            // Act
            await _cacheProvider.SetAsync(key, value);
            
            // Verify WAL file was created and contains the operation
            var walFiles = Directory.GetFiles(Path.Combine(_testDirectory, "wal"));
            Assert.NotEmpty(walFiles);

            // Corrupt the main cache file to simulate failure
            var cacheFile = Path.Combine(_testDirectory, 
                Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
            File.WriteAllText(cacheFile, "corrupted data");

            // Create new cache provider to trigger recovery
            var newProvider = new FileBasedCacheProvider(
                Options.Create(_defaultSettings), 
                _llmProviderMock.Object);

            // Assert
            var recoveredValue = await newProvider.GetAsync<string>(key);
            Assert.Equal(value, recoveredValue);
        }

        [Fact]
        public async Task BackupAndRestore_WorksCorrectly()
        {
            // Arrange
            var key1 = "backupKey1";
            var value1 = "backupValue1";
            var key2 = "backupKey2";
            var value2 = "backupValue2";

            // Add some data and trigger backup
            await _cacheProvider.SetAsync(key1, value1);
            await _cacheProvider.SetAsync(key2, value2);
            
            // Wait for backup to complete
            await Task.Delay(100);

            // Verify backup was created
            var backupFiles = Directory.GetFiles(Path.Combine(_testDirectory, "backups"));
            Assert.NotEmpty(backupFiles);

            // Corrupt all cache files
            foreach (var file in Directory.GetFiles(_testDirectory, "*.json"))
            {
                File.WriteAllText(file, "corrupted");
            }

            // Create new provider to trigger restore
            var newProvider = new FileBasedCacheProvider(
                Options.Create(_defaultSettings), 
                _llmProviderMock.Object);

            // Assert
            var restoredValue1 = await newProvider.GetAsync<string>(key1);
            var restoredValue2 = await newProvider.GetAsync<string>(key2);
            Assert.Equal(value1, restoredValue1);
            Assert.Equal(value2, restoredValue2);
        }

        [Fact]
        public async Task AtomicOperations_HandlesFailureCorrectly()
        {
            // Arrange
            var key = "atomicKey";
            var value = "atomicValue";
            var tempFiles = Directory.GetFiles(_testDirectory, "*.tmp");
            Assert.Empty(tempFiles); // No temp files initially

            // Act
            await _cacheProvider.SetAsync(key, value);
            
            // Assert
            tempFiles = Directory.GetFiles(_testDirectory, "*.tmp");
            Assert.Empty(tempFiles); // No temp files after operation
            var result = await _cacheProvider.GetAsync<string>(key);
            Assert.Equal(value, result);
        }

        [Fact]
        public async Task ErrorRecovery_HandlesMultipleFailureMechanisms()
        {
            // Arrange
            var key = "recoveryKey";
            var value = "recoveryValue";
            await _cacheProvider.SetAsync(key, value);

            // Corrupt everything to test full recovery chain
            // 1. Corrupt cache files
            foreach (var file in Directory.GetFiles(_testDirectory, "*.json"))
            {
                File.WriteAllText(file, "corrupted");
            }

            // 2. Corrupt WAL files
            foreach (var file in Directory.GetFiles(Path.Combine(_testDirectory, "wal")))
            {
                File.WriteAllText(file, "corrupted");
            }

            // Create new provider to trigger recovery chain
            var newProvider = new FileBasedCacheProvider(
                Options.Create(_defaultSettings), 
                _llmProviderMock.Object);

            // Assert
            // Provider should have recovered from backup
            var recoveredValue = await newProvider.GetAsync<string>(key);
            Assert.Equal(value, recoveredValue);
        }

        [Fact]
        public async Task CrossPlatformPaths_HandleCorrectly()
        {
            // Arrange
            var key = "path/with/separators\\and\\mixed/slashes";
            var value = "pathValue";

            // Act
            await _cacheProvider.SetAsync(key, value);
            var result = await _cacheProvider.GetAsync<string>(key);

            // Assert
            Assert.Equal(value, result);
        }

        [Fact]
        public async Task PerformanceTest_CacheOperations()
        {
            // Arrange
            var iterations = 1000;
            var maxAcceptableMs = 100; // 100ms per operation max
            var results = new List<double>();

            // Act - Measure write performance
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            
            for (int i = 0; i < iterations; i++)
            {
                var key = $"perf_key_{i}";
                var value = $"perf_value_{i}";
                await _cacheProvider.SetAsync(key, value);
            }
            
            var writeTime = stopwatch.ElapsedMilliseconds / (double)iterations;
            results.Add(writeTime);
            
            // Measure read performance
            stopwatch.Restart();
            
            for (int i = 0; i < iterations; i++)
            {
                var key = $"perf_key_{i}";
                await _cacheProvider.GetAsync<string>(key);
            }
            
            var readTime = stopwatch.ElapsedMilliseconds / (double)iterations;
            results.Add(readTime);
            
            // Measure concurrent performance
            stopwatch.Restart();
            var tasks = new List<Task>();
            
            for (int i = 0; i < iterations; i++)
            {
                var key = $"perf_key_{i}";
                tasks.Add(_cacheProvider.GetAsync<string>(key));
                tasks.Add(_cacheProvider.SetAsync(key, $"new_value_{i}"));
            }
            
            await Task.WhenAll(tasks);
            var concurrentTime = stopwatch.ElapsedMilliseconds / (double)(iterations * 2);
            results.Add(concurrentTime);

            // Assert
            foreach (var time in results)
            {
                Assert.True(time < maxAcceptableMs, $"Operation took {time}ms > {maxAcceptableMs}ms threshold");
            }
        }

        [Fact]
        public async Task IntegrationTest_FileSystemOperations()
        {
            // Test file creation
            var key1 = "integration_key_1";
            var value1 = "integration_value_1";
            await _cacheProvider.SetAsync(key1, value1);

            var cacheFile = Directory.GetFiles(_testDirectory, "*.json").FirstOrDefault();
            Assert.NotNull(cacheFile);

            // Test file locking
            var tasks = new List<Task>();
            var exceptions = 0;
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var fs = File.Open(cacheFile!, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        await Task.Delay(100); // Hold the lock
                    }
                    catch (IOException)
                    {
                        Interlocked.Increment(ref exceptions);
                    }
                }));
            }
            await Task.WhenAll(tasks);
            Assert.True(exceptions > 0, "File locking not working as expected");

            // Test directory structure
            Assert.True(Directory.Exists(Path.Combine(_testDirectory, "wal")));
            Assert.True(Directory.Exists(Path.Combine(_testDirectory, "backups")));
            Assert.True(Directory.Exists(Path.Combine(_testDirectory, "analytics")));

            // Test backup creation
            await _cacheProvider.SetAsync("backup_trigger_key", "backup_trigger_value");
            await Task.Delay(200); // Wait for backup
            Assert.True(Directory.GetFiles(Path.Combine(_testDirectory, "backups")).Length > 0);

            // Test WAL cleanup
            var walFiles = Directory.GetFiles(Path.Combine(_testDirectory, "wal"));
            Assert.Empty(walFiles); // WAL should be cleaned up after successful operation

            // Test file permissions
            var fileInfo = new FileInfo(cacheFile!);
            Assert.True((fileInfo.Attributes & FileAttributes.Hidden) == 0);
            Assert.True((fileInfo.Attributes & FileAttributes.ReadOnly) == 0);

            // Test large file handling
            var largeValue = new string('x', 1024 * 1024); // 1MB string
            await _cacheProvider.SetAsync("large_key", largeValue);
            var retrievedValue = await _cacheProvider.GetAsync<string>("large_key");
            Assert.Equal(largeValue.Length, retrievedValue?.Length);

            // Test file deletion
            await _cacheProvider.RemoveAsync(key1);
            Assert.False(File.Exists(cacheFile));

            // Test automatic cleanup
            var expiredKey = "expired_key";
            await _cacheProvider.SetAsync(expiredKey, "expired_value", TimeSpan.FromMilliseconds(1));
            await Task.Delay(50);
            await _cacheProvider.GetAsync<string>(expiredKey); // This should trigger cleanup
            Assert.False(File.Exists(Path.Combine(_testDirectory, 
                Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(expiredKey))) + ".json")));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}