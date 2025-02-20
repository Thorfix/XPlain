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

        [Fact]
        public async Task ConcurrentOperations_MaintainsCacheConsistency()
        {
            // Arrange 
            var iterations = 1000;
            var concurrentTasks = new List<Task>();
            var expectedFinalValue = "final_value";
            var key = "consistency_test_key";

            // Act - Multiple concurrent reads and writes to same key
            for (int i = 0; i < iterations; i++)
            {
                concurrentTasks.Add(_cacheProvider.SetAsync(key, $"value_{i}"));
                concurrentTasks.Add(_cacheProvider.GetAsync<string>(key));
                if (i == iterations - 1)
                {
                    // Ensure we know the final value that should be stored
                    await _cacheProvider.SetAsync(key, expectedFinalValue);
                }
            }

            await Task.WhenAll(concurrentTasks);

            // Assert
            var finalValue = await _cacheProvider.GetAsync<string>(key);
            Assert.Equal(expectedFinalValue, finalValue);

            // Verify no partial/corrupt files exist
            var tempFiles = Directory.GetFiles(_testDirectory, "*.tmp");
            Assert.Empty(tempFiles);

            // Verify WAL is clean
            var walFiles = Directory.GetFiles(Path.Combine(_testDirectory, "wal"));
            Assert.Empty(walFiles);
        }

        [Fact]
        public async Task EncryptionKeyRotation_PreservesData()
        {
            // Arrange
            var key = "rotation_test_key";
            var value = "sensitive_data";
            await _cacheProvider.SetAsync(key, value);

            // Get original encrypted file content
            var filePath = Path.Combine(_testDirectory,
                Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
            var originalEncrypted = await File.ReadAllBytesAsync(filePath);

            // Act - Create new provider with different encryption key
            var newSettings = _defaultSettings with
            {
                EncryptionKey = Convert.ToBase64String(Aes.Create().Key)
            };
            var newProvider = new FileBasedCacheProvider(
                Options.Create(newSettings),
                _llmProviderMock.Object);

            // Assert
            var retrievedValue = await newProvider.GetAsync<string>(key);
            Assert.Equal(value, retrievedValue);

            // Verify file was re-encrypted
            var newEncrypted = await File.ReadAllBytesAsync(filePath);
            Assert.NotEqual(originalEncrypted, newEncrypted);
        }

        [Fact]
        public async Task BackupRotation_ManagesStorageEfficiently()
        {
            // Arrange
            var maxBackups = 5;
            var backupDelay = 100; // ms between backups

            // Act - Create multiple backups
            for (int i = 0; i < maxBackups + 3; i++)
            {
                await _cacheProvider.SetAsync($"backup_key_{i}", $"backup_value_{i}");
                await Task.Delay(backupDelay); // Wait for backup
            }

            // Assert
            var backupFiles = Directory.GetFiles(Path.Combine(_testDirectory, "backups"))
                .OrderByDescending(f => f)
                .ToList();

            // Verify only maxBackups are kept
            Assert.Equal(maxBackups, backupFiles.Count);

            // Verify backups are ordered correctly (newest first)
            for (int i = 1; i < backupFiles.Count; i++)
            {
                var current = File.GetCreationTimeUtc(backupFiles[i - 1]);
                var previous = File.GetCreationTimeUtc(backupFiles[i]);
                Assert.True(current > previous);
            }
        }

        [Fact]
        public async Task AtomicOperations_HandlesMultiStepFailures()
        {
            // Arrange
            var key = "atomic_test_key";
            var value = "atomic_test_value";
            var failurePoints = new List<string> { "temp", "wal", "main" };

            foreach (var failPoint in failurePoints)
            {
                // Act - Simulate failure at different points
                var succeeded = false;
                try
                {
                    await _cacheProvider.SetAsync(key, value);

                    // Simulate failure
                    switch (failPoint)
                    {
                        case "temp":
                            // Corrupt temp files during write
                            foreach (var file in Directory.GetFiles(_testDirectory, "*.tmp"))
                            {
                                File.WriteAllText(file, "corrupted");
                            }
                            break;

                        case "wal":
                            // Corrupt WAL files
                            foreach (var file in Directory.GetFiles(Path.Combine(_testDirectory, "wal")))
                            {
                                File.WriteAllText(file, "corrupted");
                            }
                            break;

                        case "main":
                            // Corrupt main cache file
                            var cacheFile = Directory.GetFiles(_testDirectory, "*.json").First();
                            File.WriteAllText(cacheFile, "corrupted");
                            break;
                    }

                    // Try to read the value
                    var retrieved = await _cacheProvider.GetAsync<string>(key);
                    succeeded = retrieved == value;
                }
                catch
                {
                    // Expected for some failure points
                }

                // Assert - Data should either be fully written or not at all
                var finalValue = await _cacheProvider.GetAsync<string>(key);
                if (succeeded)
                {
                    Assert.Equal(value, finalValue);
                }
                else
                {
                    Assert.Null(finalValue);
                }

                // Cleanup for next iteration
                await _cacheProvider.RemoveAsync(key);
            }
        }

        [Fact]
        public async Task EnvironmentSpecificPaths_HandledCorrectly()
        {
            // Arrange
            var platformSpecificPaths = new[]
            {
                "folder\\subfolder\\file", // Windows style
                "folder/subfolder/file",   // Unix style
                "folder\\mixed/path/style\\test", // Mixed
                Path.Combine("folder", "subfolder", "file"), // Platform-native
                "folder name with spaces/file",
                "földer/文件/file", // Unicode paths
                @"C:\absolute\windows\path", // Windows absolute
                "/absolute/unix/path",       // Unix absolute
                "./relative/path",           // Relative paths
                "../parent/path"            // Parent directory
            };

            // Act & Assert
            foreach (var path in platformSpecificPaths)
            {
                var key = $"path_test_{path}";
                var value = "test_value";

                await _cacheProvider.SetAsync(key, value);
                var retrieved = await _cacheProvider.GetAsync<string>(key);
                Assert.Equal(value, retrieved);

                // Verify file exists with safe name
                var safePath = Convert.ToBase64String(
                    SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json";
                Assert.True(File.Exists(Path.Combine(_testDirectory, safePath)));
            }
        }

        [Fact]
        public async Task SensitiveData_StorageAndCleanup()
        {
            // Arrange
            var sensitiveKey = "sensitive_key";
            var sensitiveValue = "sensitive_data_" + Guid.NewGuid().ToString();
            var filePath = "";

            // Act
            await _cacheProvider.SetAsync(sensitiveKey, sensitiveValue);
            
            // Find the file where data is stored
            filePath = Directory.GetFiles(_testDirectory, "*.json")
                .First(f => File.ReadAllText(f).Contains(
                    Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(sensitiveKey)))));

            // Read raw file content
            var rawContent = await File.ReadAllTextAsync(filePath);

            // Assert
            // Verify sensitive data is not stored in plain text
            Assert.DoesNotContain(sensitiveValue, rawContent);

            // Cleanup
            await _cacheProvider.RemoveAsync(sensitiveKey);

            // Verify cleanup
            Assert.False(File.Exists(filePath));

            // Check WAL and backup files don't contain plaintext
            var allFiles = Directory.GetFiles(_testDirectory, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                var content = await File.ReadAllTextAsync(file);
                Assert.DoesNotContain(sensitiveValue, content);
            }
        }

        [Fact]
        public async Task CodeChange_CacheConsistency()
        {
            // Arrange
            var codeVersions = new[] 
            {
                "v1_hash",
                "v2_hash",
                "v3_hash"
            };

            var testData = new Dictionary<string, string>
            {
                {"key1", "value1"},
                {"key2", "value2"},
                {"key3", "value3"}
            };

            // Act & Assert
            foreach (var version in codeVersions)
            {
                // Add data for this version
                foreach (var (key, value) in testData)
                {
                    await _cacheProvider.SetAsync(key, value);
                }

                // Simulate code change
                await _cacheProvider.InvalidateOnCodeChangeAsync(version);

                // Verify all cache entries are invalidated
                foreach (var key in testData.Keys)
                {
                    var result = await _cacheProvider.GetAsync<string>(key);
                    Assert.Null(result);
                }

                // Verify cache files are cleaned up
                var jsonFiles = Directory.GetFiles(_testDirectory, "*.json");
                Assert.Empty(jsonFiles);
            }
        }

        [Fact]
        public async Task ConcurrentDictionary_Operations()
        {
            // Arrange
            var concurrentKeys = Enumerable.Range(0, 1000)
                .Select(i => $"concurrent_key_{i}")
                .ToArray();

            // Act
            // Test concurrent additions
            await Task.WhenAll(concurrentKeys.Select(async key =>
            {
                await _cacheProvider.SetAsync(key, "value");
                Assert.True(await _cacheProvider.ExistsAsync(key));
            }));

            // Test concurrent reads
            await Task.WhenAll(concurrentKeys.Select(async key =>
            {
                var value = await _cacheProvider.GetAsync<string>(key);
                Assert.Equal("value", value);
            }));

            // Test concurrent updates
            await Task.WhenAll(concurrentKeys.Select(async key =>
            {
                await _cacheProvider.SetAsync(key, "updated");
                var value = await _cacheProvider.GetAsync<string>(key);
                Assert.Equal("updated", value);
            }));

            // Test concurrent removals
            await Task.WhenAll(concurrentKeys.Select(async key =>
            {
                await _cacheProvider.RemoveAsync(key);
                Assert.False(await _cacheProvider.ExistsAsync(key));
            }));

            // Assert
            // Verify final state
            foreach (var key in concurrentKeys)
            {
                Assert.False(await _cacheProvider.ExistsAsync(key));
            }

            // Verify no orphaned files
            var remainingFiles = Directory.GetFiles(_testDirectory, "*.json");
            Assert.Empty(remainingFiles);
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