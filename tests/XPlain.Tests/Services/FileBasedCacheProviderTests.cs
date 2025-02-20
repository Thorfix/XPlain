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

        [Fact]
        public async Task WAL_FileIntegrity_DuringConcurrentOperations()
        {
            // Arrange
            var key = "wal_test_key";
            var value = "wal_test_value";
            var walDir = Path.Combine(_testDirectory, "wal");
            var operations = 100;

            // Act - Generate concurrent operations that will create WAL entries
            var tasks = new List<Task>();
            for (int i = 0; i < operations; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await _cacheProvider.SetAsync(key, $"{value}_{i}");
                    // Immediately try to read to force potential race conditions
                    await _cacheProvider.GetAsync<string>(key);
                }));
            }

            // Monitor WAL directory during operations
            var walFiles = new List<string>();
            tasks.Add(Task.Run(async () =>
            {
                while (tasks.Any(t => !t.IsCompleted))
                {
                    var currentWalFiles = Directory.GetFiles(walDir);
                    foreach (var file in currentWalFiles)
                    {
                        if (!walFiles.Contains(file))
                        {
                            // Verify WAL file integrity
                            var content = await File.ReadAllTextAsync(file);
                            Assert.Contains("Operation", content); // WAL entries should contain operation type
                            Assert.Contains("Timestamp", content); // WAL entries should contain timestamp
                            walFiles.Add(file);
                        }
                    }
                    await Task.Delay(10);
                }
            }));

            await Task.WhenAll(tasks);

            // Assert
            // Verify final state is consistent
            var finalValue = await _cacheProvider.GetAsync<string>(key);
            Assert.NotNull(finalValue);
            Assert.StartsWith(value, finalValue);

            // Verify WAL cleanup
            Assert.Empty(Directory.GetFiles(walDir));
        }

        [Fact]
        public async Task ErrorRecoveryChain_WALToBackupToClean()
        {
            // Arrange
            var key = "recovery_chain_key";
            var value = "recovery_chain_value";
            var walDir = Path.Combine(_testDirectory, "wal");
            var backupDir = Path.Combine(_testDirectory, "backups");

            // Step 1: Initial state with data
            await _cacheProvider.SetAsync(key, value);
            Assert.Equal(value, await _cacheProvider.GetAsync<string>(key));

            // Step 2: Corrupt main cache file but leave WAL intact
            var cacheFile = Directory.GetFiles(_testDirectory, "*.json").First();
            File.WriteAllText(cacheFile, "corrupted");

            // Step 3: Attempt recovery from WAL
            var newProvider1 = new FileBasedCacheProvider(
                Options.Create(_defaultSettings),
                _llmProviderMock.Object);
            var walRecoveryValue = await newProvider1.GetAsync<string>(key);
            Assert.Equal(value, walRecoveryValue);

            // Step 4: Corrupt both cache and WAL, leaving backup
            File.WriteAllText(cacheFile, "corrupted");
            foreach (var walFile in Directory.GetFiles(walDir))
            {
                File.WriteAllText(walFile, "corrupted");
            }

            // Step 5: Attempt recovery from backup
            var newProvider2 = new FileBasedCacheProvider(
                Options.Create(_defaultSettings),
                _llmProviderMock.Object);
            var backupRecoveryValue = await newProvider2.GetAsync<string>(key);
            Assert.Equal(value, backupRecoveryValue);

            // Step 6: Corrupt everything
            File.WriteAllText(cacheFile, "corrupted");
            foreach (var walFile in Directory.GetFiles(walDir))
            {
                File.WriteAllText(walFile, "corrupted");
            }
            foreach (var backupFile in Directory.GetFiles(backupDir))
            {
                File.WriteAllText(backupFile, "corrupted");
            }

            // Step 7: Verify clean state initialization
            var newProvider3 = new FileBasedCacheProvider(
                Options.Create(_defaultSettings),
                _llmProviderMock.Object);
            var cleanStateValue = await newProvider3.GetAsync<string>(key);
            Assert.Null(cleanStateValue);

            // Verify directory structure is clean
            Assert.Empty(Directory.GetFiles(_testDirectory, "*.json"));
            Assert.Empty(Directory.GetFiles(walDir));
            Assert.Empty(Directory.GetFiles(backupDir));
        }

        [Fact]
        public async Task FileSystem_RaceConditions_DuringBackupRestore()
        {
            // Arrange
            var key = "race_condition_key";
            var value = "race_condition_value";
            var operations = 100;
            var backupDir = Path.Combine(_testDirectory, "backups");

            // Act - Concurrent backup/restore operations
            var tasks = new List<Task>();
            
            // Task 1: Continuously write and read data
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < operations; i++)
                {
                    await _cacheProvider.SetAsync(key, $"{value}_{i}");
                    await _cacheProvider.GetAsync<string>(key);
                    await Task.Delay(10);
                }
            }));

            // Task 2: Trigger backup operations
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < operations/10; i++)
                {
                    await _cacheProvider.SetAsync($"backup_trigger_{i}", "trigger");
                    await Task.Delay(50);
                }
            }));

            // Task 3: Simulate backup restorations
            tasks.Add(Task.Run(async () =>
            {
                while (!tasks[0].IsCompleted)
                {
                    var backups = Directory.GetFiles(backupDir);
                    if (backups.Any())
                    {
                        var newProvider = new FileBasedCacheProvider(
                            Options.Create(_defaultSettings),
                            _llmProviderMock.Object);
                        var restoredValue = await newProvider.GetAsync<string>(key);
                        Assert.NotNull(restoredValue);
                        Assert.StartsWith(value, restoredValue);
                    }
                    await Task.Delay(100);
                }
            }));

            await Task.WhenAll(tasks);

            // Assert
            // Verify final state is consistent
            var finalValue = await _cacheProvider.GetAsync<string>(key);
            Assert.NotNull(finalValue);
            Assert.StartsWith(value, finalValue);

            // Verify no partial/corrupt files exist
            Assert.Empty(Directory.GetFiles(_testDirectory, "*.tmp"));
            Assert.Empty(Directory.GetFiles(_testDirectory, "*.corrupt"));
        }

        [Theory]
        [InlineData("AES", 256)]
        [InlineData("AES", 192)]
        [InlineData("AES", 128)]
        public async Task EncryptionAlgorithms_WorkWithDifferentKeyConfigrations(string algorithm, int keySize)
        {
            // Arrange
            var testSettings = _defaultSettings with
            {
                EncryptionAlgorithm = algorithm,
                EncryptionKeySize = keySize
            };
            
            var testProvider = new FileBasedCacheProvider(
                Options.Create(testSettings),
                _llmProviderMock.Object);

            var sensitiveData = "sensitive_information_" + Guid.NewGuid().ToString();
            var key = "encryption_test_key";

            // Act
            await testProvider.SetAsync(key, sensitiveData);
            
            // Get raw file content
            var filePath = Directory.GetFiles(_testDirectory, "*.json")
                .First(f => f.Contains(
                    Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key)))));
            var rawContent = await File.ReadAllTextAsync(filePath);

            // Assert
            Assert.DoesNotContain(sensitiveData, rawContent); // Data is encrypted
            var retrievedData = await testProvider.GetAsync<string>(key);
            Assert.Equal(sensitiveData, retrievedData); // Can be decrypted
        }

        [Theory]
        [InlineData(100, 10)]    // Light load
        [InlineData(1000, 100)]  // Medium load
        [InlineData(5000, 1000)] // Heavy load
        public async Task CachePerformance_UnderDifferentLoadPatterns(int totalOperations, int concurrentOperations)
        {
            // Arrange
            var stopwatch = new Stopwatch();
            var operationsPerBatch = totalOperations / concurrentOperations;
            var results = new ConcurrentDictionary<string, double>();

            // Act
            stopwatch.Start();

            // Run batches of concurrent operations
            for (int batch = 0; batch < concurrentOperations; batch++)
            {
                var tasks = new List<Task>();
                for (int i = 0; i < operationsPerBatch; i++)
                {
                    var key = $"perf_key_{batch}_{i}";
                    var value = $"perf_value_{batch}_{i}";
                    
                    tasks.Add(Task.Run(async () =>
                    {
                        var sw = Stopwatch.StartNew();
                        await _cacheProvider.SetAsync(key, value);
                        await _cacheProvider.GetAsync<string>(key);
                        sw.Stop();
                        results.TryAdd(key, sw.ElapsedMilliseconds);
                    }));
                }
                await Task.WhenAll(tasks);
            }

            stopwatch.Stop();

            // Assert
            var totalTime = stopwatch.ElapsedMilliseconds;
            var avgOperationTime = results.Values.Average();
            var maxOperationTime = results.Values.Max();

            // Performance thresholds (adjust based on environment)
            Assert.True(avgOperationTime < 100, $"Average operation time ({avgOperationTime}ms) exceeded threshold");
            Assert.True(maxOperationTime < 500, $"Maximum operation time ({maxOperationTime}ms) exceeded threshold");
            Assert.True(totalTime < totalOperations * 50, $"Total execution time ({totalTime}ms) exceeded threshold");

            // Verify cache state
            var cacheStats = _cacheProvider.GetCacheStats();
            Assert.Equal(totalOperations, cacheStats.Hits + cacheStats.Misses);
        }

        [Fact]
        public async Task RecoveryMechanism_VerifiesDataConsistency()
        {
            // Arrange
            var testData = new Dictionary<string, string>();
            for (int i = 0; i < 100; i++)
            {
                testData.Add($"key_{i}", $"value_{i}");
            }

            // Act - Phase 1: Initial data population
            foreach (var (key, value) in testData)
            {
                await _cacheProvider.SetAsync(key, value);
            }

            // Verify initial state
            foreach (var (key, expectedValue) in testData)
            {
                var value = await _cacheProvider.GetAsync<string>(key);
                Assert.Equal(expectedValue, value);
            }

            // Phase 2: Simulate partial corruption
            var cacheFiles = Directory.GetFiles(_testDirectory, "*.json");
            for (int i = 0; i < cacheFiles.Length; i += 2) // Corrupt every other file
            {
                File.WriteAllText(cacheFiles[i], "corrupted");
            }

            // Phase 3: Recovery attempt through WAL
            var provider1 = new FileBasedCacheProvider(
                Options.Create(_defaultSettings),
                _llmProviderMock.Object);

            // Verify WAL recovery
            foreach (var (key, expectedValue) in testData)
            {
                var value = await provider1.GetAsync<string>(key);
                if (value != null) // Some values should be recovered
                {
                    Assert.Equal(expectedValue, value);
                }
            }

            // Phase 4: Simulate WAL corruption, forcing backup recovery
            foreach (var file in Directory.GetFiles(Path.Combine(_testDirectory, "wal")))
            {
                File.WriteAllText(file, "corrupted");
            }

            // Phase 5: Recovery attempt through backup
            var provider2 = new FileBasedCacheProvider(
                Options.Create(_defaultSettings),
                _llmProviderMock.Object);

            // Verify backup recovery
            var recoveredCount = 0;
            foreach (var (key, expectedValue) in testData)
            {
                var value = await provider2.GetAsync<string>(key);
                if (value != null)
                {
                    Assert.Equal(expectedValue, value);
                    recoveredCount++;
                }
            }

            // Assert some data was recovered
            Assert.True(recoveredCount > 0, "No data was recovered from backup");

            // Phase 6: Complete corruption, verify clean state
            foreach (var file in Directory.GetFiles(_testDirectory, "*.*", SearchOption.AllDirectories))
            {
                try { File.WriteAllText(file, "corrupted"); } catch { }
            }

            var provider3 = new FileBasedCacheProvider(
                Options.Create(_defaultSettings),
                _llmProviderMock.Object);

            // Verify clean state
            foreach (var key in testData.Keys)
            {
                var value = await provider3.GetAsync<string>(key);
                Assert.Null(value);
            }

            // Verify cache is in clean, working state
            var testKey = "new_test_key";
            var testValue = "new_test_value";
            await provider3.SetAsync(testKey, testValue);
            Assert.Equal(testValue, await provider3.GetAsync<string>(testKey));
        }

        [Fact]
        public async Task FileSystem_ErrorScenarios()
        {
            // Arrange
            var key = "error_test_key";
            var value = "error_test_value";
            var readOnlyDir = Path.Combine(_testDirectory, "readonly");
            Directory.CreateDirectory(readOnlyDir);
            
            var readOnlySettings = _defaultSettings with { CacheDirectory = readOnlyDir };
            File.SetAttributes(readOnlyDir, FileAttributes.ReadOnly);

            // Act & Assert - Permission errors
            var readOnlyProvider = new FileBasedCacheProvider(
                Options.Create(readOnlySettings),
                _llmProviderMock.Object);
            
            await Assert.ThrowsAnyAsync<IOException>(async () =>
                await readOnlyProvider.SetAsync(key, value));

            // Reset directory attributes
            File.SetAttributes(readOnlyDir, FileAttributes.Normal);

            // Simulate disk full scenario
            var originalPath = Path.Combine(_testDirectory, "huge_file.tmp");
            try
            {
                // Create a file that's too large to handle
                using (var fs = new FileStream(originalPath, FileMode.Create, FileAccess.Write))
                {
                    fs.SetLength(long.MaxValue - 1); // Simulate disk full
                }

                await Assert.ThrowsAnyAsync<IOException>(async () =>
                    await _cacheProvider.SetAsync("large_key", "large_value"));
            }
            catch (IOException)
            {
                // Expected when system actually can't allocate the space
            }
            finally
            {
                if (File.Exists(originalPath))
                    File.Delete(originalPath);
            }
        }

        [Fact]
        public async Task LockTimeout_DeadlockPrevention()
        {
            // Arrange
            var key = "lock_test_key";
            var value = "lock_test_value";
            var lockFile = Path.Combine(_testDirectory, "lock_file");
            var maxWaitTime = TimeSpan.FromSeconds(2);
            var operationTimeout = TimeSpan.FromSeconds(5);

            // Create artificial lock
            using (var fileLock = File.Open(lockFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                // Act - Try operations while file is locked
                var task = Task.Run(async () =>
                {
                    await _cacheProvider.SetAsync(key, value);
                    return await _cacheProvider.GetAsync<string>(key);
                });

                // Assert - Operation should timeout or fail gracefully
                var completedInTime = task.Wait(operationTimeout);
                Assert.True(completedInTime, "Operation did not timeout as expected");
            }

            // Verify cache is still operational
            await _cacheProvider.SetAsync(key, value);
            var result = await _cacheProvider.GetAsync<string>(key);
            Assert.Equal(value, result);
        }

        [Fact]
        public async Task CacheOperations_PerformanceMetrics()
        {
            // Arrange
            var iterations = 1000;
            var results = new Dictionary<string, List<double>>
            {
                { "Read", new List<double>() },
                { "Write", new List<double>() },
                { "Update", new List<double>() }
            };

            var key = "perf_metric_key";
            var value = "perf_metric_value";

            // Act
            // Measure write performance
            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                await _cacheProvider.SetAsync($"{key}_{i}", value);
                sw.Stop();
                results["Write"].Add(sw.ElapsedMilliseconds);
            }

            // Measure read performance
            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                await _cacheProvider.GetAsync<string>($"{key}_{i}");
                sw.Stop();
                results["Read"].Add(sw.ElapsedMilliseconds);
            }

            // Measure update performance
            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                await _cacheProvider.SetAsync($"{key}_{i}", $"{value}_updated");
                sw.Stop();
                results["Update"].Add(sw.ElapsedMilliseconds);
            }

            // Assert
            foreach (var (operation, measurements) in results)
            {
                var avg = measurements.Average();
                var max = measurements.Max();
                var min = measurements.Min();
                var p95 = measurements.OrderBy(m => m).ElementAt((int)(iterations * 0.95));

                // Performance thresholds
                Assert.True(avg < 50, $"{operation} average time ({avg}ms) exceeded threshold");
                Assert.True(p95 < 100, $"{operation} P95 time ({p95}ms) exceeded threshold");
                Assert.True(max < 200, $"{operation} max time ({max}ms) exceeded threshold");

                // Operation specific assertions
                switch (operation)
                {
                    case "Read":
                        Assert.True(avg < results["Write"].Average(), 
                            "Read operations should be faster than writes");
                        break;
                    case "Update":
                        Assert.True(avg <= results["Write"].Average() * 1.2, 
                            "Update should not be significantly slower than write");
                        break;
                }
            }

            // Verify cache statistics
            var stats = _cacheProvider.GetCacheStats();
            Assert.Equal(iterations, stats.Hits); // All reads should be hits
        }

        [Theory]
        [InlineData("/tmp/cache")]      // Unix-style path
        [InlineData("C:\\temp\\cache")] // Windows-style path
        [InlineData("./relative/path")]  // Relative path
        [InlineData("../parent/path")]   // Parent directory path
        [InlineData("path with spaces")] // Path with spaces
        public async Task DifferentEnvironments_PathHandling(string basePath)
        {
            // Arrange
            var testPath = Path.Combine(_testDirectory, "env_test");
            Directory.CreateDirectory(testPath);
            
            var envSettings = _defaultSettings with 
            { 
                CacheDirectory = Path.Combine(testPath, basePath.Replace('\\', Path.DirectorySeparatorChar))
            };

            var provider = new FileBasedCacheProvider(
                Options.Create(envSettings),
                _llmProviderMock.Object);

            // Act
            await provider.SetAsync("env_key", "env_value");
            var result = await provider.GetAsync<string>("env_key");

            // Assert
            Assert.Equal("env_value", result);
        }

        [Fact]
        public async Task ConcurrentDictionary_SpecificOperations()
        {
            // Arrange
            const int concurrentOperations = 1000;
            var tasks = new List<Task>();
            var exceptions = new ConcurrentBag<Exception>();
            var successCount = 0;

            // Act
            // Test 1: Concurrent additions
            for (int i = 0; i < concurrentOperations; i++)
            {
                var current = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await _cacheProvider.SetAsync($"concurrent_key_{current}", $"value_{current}");
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }));
            }
            await Task.WhenAll(tasks);
            
            // Assert first test
            Assert.Empty(exceptions);
            Assert.Equal(concurrentOperations, successCount);

            // Reset for next test
            tasks.Clear();
            exceptions.Clear();
            successCount = 0;

            // Test 2: Concurrent reads and writes to same keys
            var keys = Enumerable.Range(0, 10).Select(i => $"shared_key_{i}").ToArray();
            foreach (var key in keys)
            {
                await _cacheProvider.SetAsync(key, "initial");
            }

            for (int i = 0; i < concurrentOperations; i++)
            {
                var current = i;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var key = keys[current % keys.Length];
                        if (current % 2 == 0)
                        {
                            await _cacheProvider.SetAsync(key, $"value_{current}");
                        }
                        else
                        {
                            var value = await _cacheProvider.GetAsync<string>(key);
                            Assert.NotNull(value);
                        }
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }));
            }
            await Task.WhenAll(tasks);

            // Assert second test
            Assert.Empty(exceptions);
            Assert.Equal(concurrentOperations, successCount);

            // Verify final state
            foreach (var key in keys)
            {
                Assert.True(await _cacheProvider.ExistsAsync(key));
            }
        }

        [Fact]
        public async Task EncryptionDecryption_ComprehensiveVerification()
        {
            // Arrange
            var testCases = new[]
            {
                // Regular string
                new { Key = "regular_key", Value = "regular_value" },
                // Large data
                new { Key = "large_key", Value = new string('x', 1024 * 1024) },
                // Special characters
                new { Key = "special_key", Value = "!@#$%^&*()_+{}[]|\\:;\"'<>,.?/~`" },
                // Unicode
                new { Key = "unicode_key", Value = "Hello, 世界! Привет, мир! שָׁלוֹם, עוֹלָם!" },
                // JSON structure
                new { Key = "json_key", Value = "{\"nested\":{\"array\":[1,2,3],\"value\":\"test\"}}" },
                // Empty string
                new { Key = "empty_key", Value = "" },
                // Whitespace
                new { Key = "whitespace_key", Value = "   \t\n\r   " }
            };

            foreach (var testCase in testCases)
            {
                // Act
                await _cacheProvider.SetAsync(testCase.Key, testCase.Value);

                // Get encrypted file content
                var filePath = Directory.GetFiles(_testDirectory, "*.json")
                    .First(f => f.Contains(
                        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(testCase.Key)))));
                
                var encryptedContent = await File.ReadAllTextAsync(filePath);

                // Assert
                // 1. Encrypted content should not contain original value
                Assert.DoesNotContain(testCase.Value, encryptedContent);

                // 2. Decrypt and verify
                var decrypted = await _cacheProvider.GetAsync<string>(testCase.Key);
                Assert.Equal(testCase.Value, decrypted);

                // 3. Check encryption markers
                Assert.Contains("\"IV\":", encryptedContent); // Should contain IV
                Assert.DoesNotContain("\"Value\":", encryptedContent); // Raw value should not be visible
            }

            // Additional encryption verification
            var stats = _cacheProvider.GetCacheStats();
            var encryptionStatus = stats.EncryptionStatus;
            Assert.True(encryptionStatus.Enabled);
            Assert.Equal(_defaultSettings.EncryptionAlgorithm, encryptionStatus.Algorithm);
            Assert.Equal(testCases.Length, encryptionStatus.EncryptedFileCount);
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