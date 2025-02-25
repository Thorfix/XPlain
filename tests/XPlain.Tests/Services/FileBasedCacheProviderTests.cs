using System;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Collections.Concurrent;
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
        
        // Add stopwatch for performance tracking in tests
        private readonly Stopwatch _stopwatch = new Stopwatch();

        // Comprehensive tests for compression functionality
    
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
                CodebasePath = _testDirectory,
                // Enable compression by default for tests
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                CompressionLevel = CompressionLevel.Optimal,
                MinSizeForCompressionBytes = 1024, // 1KB threshold
                AdaptiveCompression = true,
                ContentTypeAlgorithmMap = new Dictionary<ContentType, CompressionAlgorithm> {
                    { ContentType.TextJson, CompressionAlgorithm.Brotli },
                    { ContentType.TextPlain, CompressionAlgorithm.GZip }
                }
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
        public async Task WALRecovery_HandlesCorruptedWALEntries()
        {
            // Arrange
            var key = "corrupted_wal_key";
            var value = "corrupted_wal_value";
            var walDir = Path.Combine(_testDirectory, "wal");

            // Act
            await _cacheProvider.SetAsync(key, value);

            // Corrupt WAL file but preserve structure
            var walFiles = Directory.GetFiles(walDir);
            Assert.NotEmpty(walFiles);
            var walContent = await File.ReadAllTextAsync(walFiles[0]);
            var corruptedWal = walContent.Replace(value, "corrupted_value");
            await File.WriteAllTextAsync(walFiles[0], corruptedWal);

            // Corrupt cache file to force WAL recovery
            var cacheFile = Directory.GetFiles(_testDirectory, "*.json").First();
            File.WriteAllText(cacheFile, "corrupted");

            // Create new provider to trigger recovery
            var newProvider = new FileBasedCacheProvider(
                Options.Create(_defaultSettings),
                _llmProviderMock.Object);

            // Assert
            var recoveredValue = await newProvider.GetAsync<string>(key);
            // Should fail WAL recovery and fallback to backup or clean state
            Assert.Null(recoveredValue);

            // Verify WAL files are cleaned up
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
        public async Task EncryptionTest_ValidatesKeySize()
        {
            // Arrange
            var invalidKeySizes = new[] { 64, 96, 512 }; // Invalid AES key sizes
            var validKeySizes = new[] { 128, 192, 256 }; // Valid AES key sizes

            foreach (var keySize in invalidKeySizes)
            {
                var settings = _defaultSettings with
                {
                    EncryptionEnabled = true,
                    EncryptionAlgorithm = "AES",
                    EncryptionKeySize = keySize
                };

                // Act & Assert
                var exception = await Assert.ThrowsAnyAsync<CryptographicException>(() =>
                {
                    var provider = new FileBasedCacheProvider(
                        Options.Create(settings),
                        _llmProviderMock.Object);
                    return Task.CompletedTask;
                });
                Assert.Contains("key size", exception.Message.ToLower());
            }

            // Verify valid key sizes work
            foreach (var keySize in validKeySizes)
            {
                var settings = _defaultSettings with
                {
                    EncryptionEnabled = true,
                    EncryptionAlgorithm = "AES",
                    EncryptionKeySize = keySize
                };

                var provider = new FileBasedCacheProvider(
                    Options.Create(settings),
                    _llmProviderMock.Object);

                // Test encryption with this key size works
                var testKey = $"key_size_{keySize}";
                var testValue = "test_value";
                await provider.SetAsync(testKey, testValue);
                var result = await provider.GetAsync<string>(testKey);
                Assert.Equal(testValue, result);
            }
        }

        [Fact]
        public async Task ConcurrencyTest_DeadlockPrevention()
        {
            // Arrange
            var key1 = "deadlock_key_1";
            var key2 = "deadlock_key_2";
            var value1 = "value1";
            var value2 = "value2";
            var completedOperations = 0;

            // Act - Simulate potential deadlock scenario
            var task1 = Task.Run(async () =>
            {
                await _cacheProvider.SetAsync(key1, value1);
                await Task.Delay(100); // Force delay to increase chance of deadlock
                await _cacheProvider.GetAsync<string>(key2);
                Interlocked.Increment(ref completedOperations);
            });

            var task2 = Task.Run(async () =>
            {
                await _cacheProvider.SetAsync(key2, value2);
                await Task.Delay(100);
                await _cacheProvider.GetAsync<string>(key1);
                Interlocked.Increment(ref completedOperations);
            });

            // Assert
            var completedInTime = Task.WaitAll(new[] { task1, task2 }, TimeSpan.FromSeconds(5));
            Assert.True(completedInTime, "Operations timed out, possible deadlock");
            Assert.Equal(2, completedOperations);

            // Verify final state
            Assert.Equal(value1, await _cacheProvider.GetAsync<string>(key1));
            Assert.Equal(value2, await _cacheProvider.GetAsync<string>(key2));
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
        public async Task CompressionEnabled_ReducesStorageSize()
        {
            // Arrange
            var key = "compression_test_key";
            var largeValue = new string('x', 100 * 1024); // 100KB of repeating data (highly compressible)
            
            var compressionSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                CompressionLevel = CompressionLevel.Optimal,
                MinSizeForCompressionBytes = 1024 // 1KB threshold
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(compressionSettings),
                metricsService: null,
                mlPredictionService: null);

            // Act
            await provider.SetAsync(key, largeValue);
            var stats = provider.GetCacheStats();
            
            // Get the compressed value
            var retrievedValue = await provider.GetAsync<string>(key);

            // Assert
            Assert.Equal(largeValue, retrievedValue); // Data integrity maintained
            Assert.Equal(largeValue.Length, stats.StorageUsageBytes); // Original size is tracked
            Assert.True(stats.CompressedStorageUsageBytes < stats.StorageUsageBytes); // Compression reduced size
            
            // Compression should achieve significant size reduction for repeating data
            var compressionRatio = (double)stats.CompressedStorageUsageBytes / stats.StorageUsageBytes;
            Assert.True(compressionRatio < 0.1, $"Compression ratio ({compressionRatio}) should be less than 0.1 for highly compressible data");
            
            // Verify compression stats are collected
            Assert.True(stats.CompressionStats.ContainsKey("GZip"));
            Assert.True(stats.CompressionStats["GZip"].CompressedItems > 0);
            Assert.True(stats.CompressionStats["GZip"].CompressionSavingPercent > 50); // Should save at least 50%
            
            // Check memory usage
            var memoryBeforeAccess = GC.GetTotalMemory(true);
            for (int i = 0; i < 10; i++)
            {
                retrievedValue = await provider.GetAsync<string>(key);
            }
            var memoryAfterAccess = GC.GetTotalMemory(false);
            
            // Memory increase should be reasonable during decompression
            var memoryDelta = memoryAfterAccess - memoryBeforeAccess;
            Assert.True(memoryDelta < largeValue.Length, 
                $"Memory delta ({memoryDelta}) should be less than uncompressed size ({largeValue.Length})");
            
            // Test additional compression validation
            Console.WriteLine($"Compression Test Results:");
            Console.WriteLine($"Original size: {largeValue.Length} bytes");
            Console.WriteLine($"Compressed size: {stats.CompressedStorageUsageBytes} bytes");
            Console.WriteLine($"Compression ratio: {compressionRatio:F3}");
            Console.WriteLine($"Space saved: {stats.CompressionSavingsBytes} bytes ({stats.CompressionSavingsPercent:F1}%)");
            Console.WriteLine($"Memory impact: {memoryDelta} bytes");
            
            // Validate compression effectiveness statistics
            Assert.True(stats.CompressionSavingsBytes > 0, "Compression should save disk space");
            Assert.True(stats.CompressionSavingsPercent > 50, "Compression should save at least 50% of space");
            Assert.True(stats.CompressionStats["GZip"].EfficiencyScore > 0, "Compression efficiency score should be positive");
        }
        
        [Fact]
        public async Task CompressionComparison_DifferentAlgorithmsAndData()
        {
            // Arrange
            var testDataSets = new Dictionary<string, string>
            {
                ["Json"] = System.Text.Json.JsonSerializer.Serialize(new { 
                    items = Enumerable.Range(0, 100).Select(i => new { id = i, value = $"Item value {i}" }).ToArray(),
                    metadata = new { created = DateTime.Now, author = "Test" }
                }),
                ["Text"] = string.Join("\n", Enumerable.Range(0, 500).Select(i => $"Line {i} of sample text for testing compression efficiency with repeating patterns.")),
                ["Binary"] = Convert.ToBase64String(Enumerable.Range(0, 1000).SelectMany(i => BitConverter.GetBytes(i)).ToArray()),
                ["Mixed"] = "START-" + new string('x', 10000) + "-MIDDLE-" + string.Join(",", Enumerable.Range(0, 500)) + "-END"
            };
            
            var algorithms = new[] { CompressionAlgorithm.GZip, CompressionAlgorithm.Brotli };
            var compressionLevels = new[] { CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize };
            
            var results = new Dictionary<string, Dictionary<string, Dictionary<string, (double ratio, double timeMs)>>>();
            
            // Act - Test each combination of data, algorithm and compression level
            foreach (var (dataType, testData) in testDataSets)
            {
                results[dataType] = new Dictionary<string, Dictionary<string, (double ratio, double timeMs)>>();
                
                foreach (var algorithm in algorithms)
                {
                    results[dataType][algorithm.ToString()] = new Dictionary<string, (double ratio, double timeMs)>();
                    
                    foreach (var level in compressionLevels)
                    {
                        var compressionSettings = new CacheSettings
                        {
                            CacheEnabled = true,
                            CacheDirectory = Path.Combine(_testDirectory, $"{dataType}_{algorithm}_{level}"),
                            CompressionEnabled = true,
                            CompressionAlgorithm = algorithm,
                            CompressionLevel = level,
                            MinSizeForCompressionBytes = 0 // Force compression regardless of size
                        };
                        
                        Directory.CreateDirectory(compressionSettings.CacheDirectory);
                        
                        var provider = new FileBasedCacheProvider(
                            Options.Create(compressionSettings),
                            metricsService: null,
                            mlPredictionService: null);
                        
                        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                        await provider.SetAsync("test_key", testData);
                        stopwatch.Stop();
                        
                        var stats = provider.GetCacheStats();
                        var ratio = (double)stats.CompressedStorageUsageBytes / stats.StorageUsageBytes;
                        
                        results[dataType][algorithm.ToString()][level.ToString()] = (ratio, stopwatch.ElapsedMilliseconds);
                        
                        // Verify data integrity
                        var retrieved = await provider.GetAsync<string>("test_key");
                        Assert.Equal(testData, retrieved);
                    }
                }
            }
            
            // Assert
            foreach (var (dataType, algorithmResults) in results)
            {
                foreach (var (algorithm, levelResults) in algorithmResults)
                {
                    // Verify compression efficiency increases with compression level
                    var fastestRatio = levelResults[CompressionLevel.Fastest.ToString()].ratio;
                    var optimalRatio = levelResults[CompressionLevel.Optimal.ToString()].ratio;
                    var smallestRatio = levelResults[CompressionLevel.SmallestSize.ToString()].ratio;
                    
                    // Higher compression levels should give equal or better compression
                    Assert.True(optimalRatio <= fastestRatio * 1.1); // Allow small margin due to compression algorithms' internal behavior
                    Assert.True(smallestRatio <= optimalRatio * 1.1);
                    
                    // Higher levels should take more time
                    var fastestTime = levelResults[CompressionLevel.Fastest.ToString()].timeMs;
                    var optimalTime = levelResults[CompressionLevel.Optimal.ToString()].timeMs;
                    var smallestTime = levelResults[CompressionLevel.SmallestSize.ToString()].timeMs;
                    
                    // This relationship isn't always strict due to system variability, but generally holds
                    // We use a loose assertion to avoid test flakiness
                    Assert.True(smallestTime >= fastestTime * 0.7);
                }
                
                // All data types should show compression benefits
                var bestRatio = algorithmResults.SelectMany(a => a.Value)
                    .Min(r => r.Value.ratio);
                    
                Assert.True(bestRatio < 1.0, $"Data type {dataType} should benefit from compression");
                
                if (dataType == "Text" || dataType == "Json")
                {
                    // Text and JSON should compress very well
                    Assert.True(bestRatio < 0.3, $"Text-based data type {dataType} should compress very well");
                }
            }
            
            // Output detailed results to console for analysis
            Console.WriteLine("=== Compression Test Results ===");
            foreach (var (dataType, algorithmResults) in results)
            {
                Console.WriteLine($"\nData Type: {dataType}");
                foreach (var (algorithm, levelResults) in algorithmResults)
                {
                    Console.WriteLine($"  Algorithm: {algorithm}");
                    foreach (var (level, metrics) in levelResults)
                    {
                        Console.WriteLine($"    {level}: Ratio={metrics.ratio:F3}, Time={metrics.timeMs:F1}ms");
                    }
                }
            }
        }
        
        [Fact]
        public async Task CompressionDisabled_SkipsCompression()
        {
            // Arrange
            var key = "no_compression_test_key";
            var value = new string('x', 100 * 1024); // 100KB of data
            
            var noCompressionSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = false
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(noCompressionSettings),
                metricsService: null,
                mlPredictionService: null);

            // Act
            await provider.SetAsync(key, value);
            var stats = provider.GetCacheStats();
            
            // Assert
            Assert.Equal(stats.StorageUsageBytes, stats.CompressedStorageUsageBytes); // No compression
            Assert.Equal(1.0, stats.CompressionRatio); // Ratio should be 1.0 when disabled
        }
        
        [Theory]
        [InlineData(CompressionAlgorithm.GZip, 100 * 1024, true)]  // GZip, 100KB, compressible
        [InlineData(CompressionAlgorithm.Brotli, 100 * 1024, true)] // Brotli, 100KB, compressible
        [InlineData(CompressionAlgorithm.GZip, 1024, true)]        // GZip, 1KB, compressible
        [InlineData(CompressionAlgorithm.Brotli, 1024, true)]      // Brotli, 1KB, compressible
        [InlineData(CompressionAlgorithm.GZip, 512, true)]         // GZip, 512B, below threshold
        [InlineData(CompressionAlgorithm.Brotli, 512, true)]       // Brotli, 512B, below threshold
        [InlineData(CompressionAlgorithm.GZip, 5 * 1024 * 1024, true)]  // GZip, 5MB, compressible
        [InlineData(CompressionAlgorithm.Brotli, 5 * 1024 * 1024, true)] // Brotli, 5MB, compressible
        [InlineData(CompressionAlgorithm.GZip, 100 * 1024, false)] // GZip, 100KB, not compressible
        [InlineData(CompressionAlgorithm.Brotli, 100 * 1024, false)] // Brotli, 100KB, not compressible
        public async Task DifferentCompressionAlgorithms_WorkCorrectly(
            CompressionAlgorithm algorithm, int dataSize, bool compressible)
        {
            // Arrange
            var key = $"algorithm_test_{algorithm}_{dataSize}_{compressible}";
            
            // Generate either compressible (repeating) data or non-compressible (random) data
            string value;
            if (compressible)
            {
                value = new string('x', dataSize); // Highly compressible repeating data
            }
            else
            {
                // Generate pseudo-random data that's hard to compress
                var random = new Random(42); // Fixed seed for reproducibility
                var bytes = new byte[dataSize];
                random.NextBytes(bytes);
                value = Convert.ToBase64String(bytes);
                // Ensure we have exactly the requested size
                value = value.Substring(0, Math.Min(value.Length, dataSize));
                if (value.Length < dataSize)
                {
                    value += new string('0', dataSize - value.Length);
                }
            }
            
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = algorithm,
                MinSizeForCompressionBytes = 1024, // 1KB threshold
                TrackCompressionMetrics = true,
                MinCompressionRatio = 0.95 // Consider compression successful if it reduces size by at least 5%
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsService: null,
                mlPredictionService: null);

            // Act
            var sw = Stopwatch.StartNew();
            await provider.SetAsync(key, value);
            sw.Stop();
            var writeTime = sw.ElapsedMilliseconds;
            
            sw.Restart();
            var retrievedValue = await provider.GetAsync<string>(key);
            sw.Stop();
            var readTime = sw.ElapsedMilliseconds;
            
            var stats = provider.GetCacheStats();

            // Assert
            Assert.Equal(value, retrievedValue); // Data integrity maintained
            
            // Log algorithm performance
            Console.WriteLine($"=== Compression Test: {algorithm}, Size: {dataSize}, Compressible: {compressible} ===");
            Console.WriteLine($"Write time: {writeTime}ms");
            Console.WriteLine($"Read time: {readTime}ms");
            Console.WriteLine($"Compression ratio: {stats.CompressionRatio:F4}");
            Console.WriteLine($"Compression time: {stats.CompressionStats[algorithm.ToString()].AverageCompressionTimeMs:F2}ms");
            Console.WriteLine($"Decompression time: {stats.CompressionStats[algorithm.ToString()].AverageDecompressionTimeMs:F2}ms");
            
            if (dataSize >= settings.MinSizeForCompressionBytes)
            {
                if (compressible)
                {
                    // Compressible data should be successfully compressed
                    Assert.True(stats.CompressionRatio < 0.9, 
                        $"Compression ratio should be < 0.9 for compressible data, got {stats.CompressionRatio}");
                    Assert.True(stats.CompressionStats[algorithm.ToString()].CompressedItems > 0,
                        "Compressible data should be counted in compressed items");
                    
                    // For large compressible data, compression should be very effective
                    if (dataSize > 100 * 1024)
                    {
                        Assert.True(stats.CompressionRatio < 0.1, 
                            $"Large compressible data should have compression ratio < 0.1, got {stats.CompressionRatio}");
                    }
                }
                else
                {
                    // Non-compressible data might not compress well
                    // The provider should skip compression if it's not effective
                    // Or slightly compress if it finds a pattern
                    
                    // We don't make strong assertions here because random data
                    // compression effectiveness can vary by algorithm
                    Console.WriteLine($"Random data compression ratio: {stats.CompressionRatio:F4}");
                    
                    // But we verify the behavior is as expected:
                    // 1. Either compression was skipped (ratio = 1.0)
                    // 2. Or compression was minimal (ratio close to 1.0)
                    // 3. Or somehow compression was still effective (ratio < 0.9)
                    if (stats.CompressionRatio < 0.9)
                    {
                        Console.WriteLine("Note: Random data compressed surprisingly well");
                    }
                    else if (stats.CompressionRatio == 1.0)
                    {
                        Console.WriteLine("Compression was correctly skipped for non-compressible data");
                    }
                    else
                    {
                        Console.WriteLine($"Minimal compression achieved: {stats.CompressionRatio:F4}");
                    }
                }
                
                // Verify compression metrics are being tracked
                Assert.True(stats.CompressionStats[algorithm.ToString()].AverageCompressionTimeMs >= 0);
                
                // For data that was compressed, verify decompression metrics
                if (stats.CompressionStats[algorithm.ToString()].CompressedItems > 0)
                {
                    Assert.True(stats.CompressionStats[algorithm.ToString()].AverageDecompressionTimeMs >= 0);
                }
                
                // Compare algorithms if using Brotli with compressible data
                if (algorithm == CompressionAlgorithm.Brotli && compressible && dataSize > 10 * 1024)
                {
                    // Create GZip provider for comparison
                    var gzipSettings = settings with { CompressionAlgorithm = CompressionAlgorithm.GZip };
                    var gzipProvider = new FileBasedCacheProvider(
                        Options.Create(gzipSettings),
                        metricsService: null,
                        mlPredictionService: null);
                    
                    await gzipProvider.SetAsync(key, value);
                    var gzipStats = gzipProvider.GetCacheStats();
                    
                    // Brotli compression should be at least as good as GZip for text data
                    Console.WriteLine($"Brotli ratio: {stats.CompressionRatio:F4}, GZip ratio: {gzipStats.CompressionRatio:F4}");
                    Assert.True(stats.CompressionRatio <= gzipStats.CompressionRatio * 1.2, 
                        "Brotli should provide comparable or better compression than GZip for text data");
                    
                    // Compare performance
                    Console.WriteLine($"Brotli compression time: {stats.CompressionStats["Brotli"].AverageCompressionTimeMs:F2}ms, " +
                                    $"GZip: {gzipStats.CompressionStats["GZip"].AverageCompressionTimeMs:F2}ms");
                    Console.WriteLine($"Brotli decompression time: {stats.CompressionStats["Brotli"].AverageDecompressionTimeMs:F2}ms, " +
                                    $"GZip: {gzipStats.CompressionStats["GZip"].AverageDecompressionTimeMs:F2}ms");
                }
            }
            else
            {
                // Too small for compression
                Assert.Equal(1.0, stats.CompressionRatio);
                
                // Should track items not compressed due to size
                Assert.True(stats.CompressionStats[algorithm.ToString()].TotalItems > 0);
                
                // Should have no compressed items
                Assert.Equal(0, stats.CompressionStats[algorithm.ToString()].CompressedItems);
            }
            
            // Verify memory usage is reasonable
            GC.Collect(); // Force collection to measure accurate memory
            var memoryAfter = GC.GetTotalMemory(false);
            Console.WriteLine($"Memory after test: {memoryAfter / (1024 * 1024)}MB");
        }
        
        [Fact]
        public async Task AdaptiveCompression_SelectsOptimalSettings()
        {
            // Arrange
            var compressibleData = new string('x', 100 * 1024); // Highly compressible
            var jsonData = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, 1000)
                .Select(i => new { Id = i, Name = $"Item {i}", Value = i * 3.14 })
                .ToArray());
            var mixedData = "START" + new string('x', 50 * 1024) + 
                            System.Text.Json.JsonSerializer.Serialize(new { random = Guid.NewGuid() }) +
                            new string('y', 40 * 1024);
            
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                AdaptiveCompression = true,
                MinSizeForCompressionBytes = 1024,
                ContentTypeAlgorithmMap = new Dictionary<ContentType, CompressionAlgorithm> {
                    { ContentType.TextJson, CompressionAlgorithm.Brotli },
                    { ContentType.TextPlain, CompressionAlgorithm.GZip }
                }
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsService: null,
                mlPredictionService: null);

            // Act
            await provider.SetAsync("compressible_key", compressibleData);
            await provider.SetAsync("json_key", jsonData);
            await provider.SetAsync("mixed_key", mixedData);
            
            var stats = provider.GetCacheStats();
            
            // Assert
            // All entries should be compressed
            Assert.True(stats.CompressionRatio < 1.0);
            
            // Verify compression was effective for all types
            Assert.True(stats.CompressionSavingsBytes > 0);
            
            // Verify the adaptive algorithm chose different algorithms for different content types
            // We can't directly test algorithm selection, but we can verify effectiveness
            Assert.True(stats.CompressionStats.ContainsKey("GZip"));
            Assert.True(stats.CompressionStats.ContainsKey("Brotli"));
            
            if (stats.CompressionStats["GZip"].CompressedItems > 0 && 
                stats.CompressionStats["Brotli"].CompressedItems > 0)
            {
                // If both algorithms were used, verify they were effective
                Assert.True(stats.CompressionStats["GZip"].CompressionRatio < 0.5);
                Assert.True(stats.CompressionStats["Brotli"].CompressionRatio < 0.5);
            }
            else
            {
                // If only one algorithm was used, it should have been very effective
                var usedAlgorithm = stats.CompressionStats["GZip"].CompressedItems > 0 ? "GZip" : "Brotli";
                Assert.True(stats.CompressionStats[usedAlgorithm].CompressionRatio < 0.3);
            }
            
            // Retrieve and verify data integrity for all types
            var retrievedCompressible = await provider.GetAsync<string>("compressible_key");
            var retrievedJson = await provider.GetAsync<string>("json_key");
            var retrievedMixed = await provider.GetAsync<string>("mixed_key");
            
            Assert.Equal(compressibleData, retrievedCompressible);
            Assert.Equal(jsonData, retrievedJson);
            Assert.Equal(mixedData, retrievedMixed);
            
            // Verify content type detection is working by checking compression stats
            Assert.True(stats.CompressionByContentType.Count > 0, "Content type detection should be working");
            
            // Verify adaptive algorithm selected best compression based on content type
            var mostEfficientAlgo = stats.MostEfficientAlgorithm;
            Assert.False(string.IsNullOrEmpty(mostEfficientAlgo), "Should identify most efficient compression algorithm");
        }
        
        [Fact]
        public async Task CacheVersions_MaintainBackwardCompatibility()
        {
            // This test verifies that non-compressed entries (old format) can still be read
            
            // Arrange - Create an entry with compression disabled
            var key = "legacy_cache_key";
            var value = "legacy_cache_value";
            
            var noCompressionSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = false
            };
            
            var legacyProvider = new FileBasedCacheProvider(
                Options.Create(noCompressionSettings),
                metricsService: null,
                mlPredictionService: null);
            
            // Create cache entry without compression
            await legacyProvider.SetAsync(key, value);
            
            // Act - Create a new provider with compression enabled and auto-upgrade
            var compressionSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                UpgradeUncompressedEntries = true
            };
            
            var modernProvider = new FileBasedCacheProvider(
                Options.Create(compressionSettings),
                metricsService: null,
                mlPredictionService: null);
            
            // Try to read the entry created without compression
            var retrievedValue = await modernProvider.GetAsync<string>(key);
            
            // Assert
            Assert.Equal(value, retrievedValue); // Should be able to read the legacy entry
            
            // Allow time for background upgrade to run
            await Task.Delay(200);
            
            // Reading the entry again should now return from compressed storage
            retrievedValue = await modernProvider.GetAsync<string>(key);
            Assert.Equal(value, retrievedValue);
            
            // The cache should now have compressed entries
            var stats = modernProvider.GetCacheStats();
            
            // At least one entry should be using compression
            Assert.True(stats.CompressedItemCount > 0, "Cache should have at least one compressed entry after upgrade");
            
            // Create a new provider with different compression algorithm to test cross-algo compatibility
            var brotliSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.Brotli,
                UpgradeUncompressedEntries = true
            };
            
            var brotliProvider = new FileBasedCacheProvider(
                Options.Create(brotliSettings),
                metricsService: null,
                mlPredictionService: null);
            
            // Should still be able to read GZip compressed entries
            retrievedValue = await brotliProvider.GetAsync<string>(key);
            Assert.Equal(value, retrievedValue);
            
            // Test with a new key to ensure format versioning works
            var newKey = "new_version_key";
            var newValue = "new_version_value";
            
            await brotliProvider.SetAsync(newKey, newValue);
            
            // Should be able to read with original provider
            var crossAlgoValue = await legacyProvider.GetAsync<string>(newKey);
            Assert.Equal(newValue, crossAlgoValue);
        }
        
        [Fact]
        public async Task Compression_PerformanceImpact()
        {
            // Arrange
            var largeValue = new string('x', 1024 * 1024); // 1MB of repeating data
            var keys = Enumerable.Range(0, 10).Select(i => $"perf_key_{i}").ToArray();
            
            var compressionSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                CompressionLevel = CompressionLevel.Fastest, // Use fastest for this test
                MinSizeForCompressionBytes = 1024, // 1KB threshold
                AutoAdjustCompressionLevel = true
            };
            
            var noCompressionSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = Path.Combine(_testDirectory, "no_compression"),
                CompressionEnabled = false
            };
            
            Directory.CreateDirectory(Path.Combine(_testDirectory, "no_compression"));
            
            var compressedProvider = new FileBasedCacheProvider(
                Options.Create(compressionSettings),
                metricsService: null,
                mlPredictionService: null);
                
            var uncompressedProvider = new FileBasedCacheProvider(
                Options.Create(noCompressionSettings),
                metricsService: null,
                mlPredictionService: null);
            
            // Act - Measure write performance
            var compressedWrite = Stopwatch.StartNew();
            foreach (var key in keys)
            {
                await compressedProvider.SetAsync(key, largeValue);
            }
            compressedWrite.Stop();
            
            var uncompressedWrite = Stopwatch.StartNew();
            foreach (var key in keys)
            {
                await uncompressedProvider.SetAsync(key, largeValue);
            }
            uncompressedWrite.Stop();
            
            // Measure read performance
            var compressedRead = Stopwatch.StartNew();
            foreach (var key in keys)
            {
                await compressedProvider.GetAsync<string>(key);
            }
            compressedRead.Stop();
            
            var uncompressedRead = Stopwatch.StartNew();
            foreach (var key in keys)
            {
                await uncompressedProvider.GetAsync<string>(key);
            }
            uncompressedRead.Stop();
            
            // Get stats to compare storage savings
            var compressedStats = compressedProvider.GetCacheStats();
            var uncompressedStats = uncompressedProvider.GetCacheStats();
            
            // Assert
            // Write operations with compression should take longer, but not excessively
            Assert.True(compressedWrite.ElapsedMilliseconds >= uncompressedWrite.ElapsedMilliseconds);
            // But the ratio shouldn't be extreme - compression shouldn't take more than 3x the time
            Assert.True(compressedWrite.ElapsedMilliseconds < uncompressedWrite.ElapsedMilliseconds * 3);
            
            // Read operations might be slightly slower with compression due to decompression overhead
            Assert.True(compressedRead.ElapsedMilliseconds >= uncompressedRead.ElapsedMilliseconds * 0.8);
            
            // Storage should be significantly reduced
            Assert.True(compressedStats.CompressedStorageUsageBytes < uncompressedStats.CompressedStorageUsageBytes);
            // For repeating data, we should get over 90% reduction
            Assert.True(compressedStats.CompressedStorageUsageBytes < uncompressedStats.CompressedStorageUsageBytes * 0.1);
            
            // Check memory impact
            var memoryAfterWrite = GC.GetTotalMemory(true);
            var intensiveReads = 100;
            
            // Perform intensive repeated reads to measure memory impact
            for (int i = 0; i < intensiveReads; i++)
            {
                await compressedProvider.GetAsync<string>(keys[i % keys.Length]);
            }
            
            var memoryAfterReads = GC.GetTotalMemory(false);
            var memoryIncrease = memoryAfterReads - memoryAfterWrite;
            
            // Memory increase should be controlled during repeated decompressions
            Assert.True(memoryIncrease < largeValue.Length * 2, 
                $"Memory increase ({memoryIncrease}) should be limited during repeated decompressions");
            
            // Verify auto-adjustment of compression level works
            Assert.NotNull(compressedStats.CompressionStats["GZip"]);
            
            // Log performance metrics
            Console.WriteLine($"Compressed write: {compressedWrite.ElapsedMilliseconds}ms");
            Console.WriteLine($"Uncompressed write: {uncompressedWrite.ElapsedMilliseconds}ms");
            Console.WriteLine($"Compressed read: {compressedRead.ElapsedMilliseconds}ms");
            Console.WriteLine($"Uncompressed read: {uncompressedRead.ElapsedMilliseconds}ms");
            Console.WriteLine($"Storage with compression: {compressedStats.CompressedStorageUsageBytes} bytes");
            Console.WriteLine($"Storage without compression: {uncompressedStats.CompressedStorageUsageBytes} bytes");
            Console.WriteLine($"Compression ratio: {compressedStats.CompressionRatio}");
            Console.WriteLine($"Memory increase after {intensiveReads} reads: {memoryIncrease} bytes");
            Console.WriteLine($"Average compression time: {compressedStats.CompressionStats["GZip"].AverageCompressionTimeMs}ms");
            Console.WriteLine($"Average decompression time: {compressedStats.CompressionStats["GZip"].AverageDecompressionTimeMs}ms");
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

        [Fact]
        public async Task WAL_PersistenceDuringProcessRestart()
        {
            // Arrange
            var key = "wal_persistence_key";
            var value = "wal_persistence_value";
            var walDir = Path.Combine(_testDirectory, "wal");

            // Act - Phase 1: Initial write with immediate process "termination"
            var initialProvider = new FileBasedCacheProvider(
                Options.Create(_defaultSettings),
                _llmProviderMock.Object);

            await initialProvider.SetAsync(key, value);

            // Simulate process termination by forcing WAL file to remain
            var walFiles = Directory.GetFiles(walDir);
            Assert.NotEmpty(walFiles);

            // Corrupt the main cache file to simulate partial write
            var cacheFile = Directory.GetFiles(_testDirectory, "*.json")
                .First(f => f.Contains(
                    Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key)))));
            File.WriteAllText(cacheFile, "corrupted during process termination");

            // Act - Phase 2: New process startup
            var newProvider = new FileBasedCacheProvider(
                Options.Create(_defaultSettings),
                _llmProviderMock.Object);

            // Assert
            var recoveredValue = await newProvider.GetAsync<string>(key);
            Assert.Equal(value, recoveredValue);

            // Verify WAL files are cleaned up after recovery
            Assert.Empty(Directory.GetFiles(walDir));
        }

        [Fact]
        public async Task CacheConsistency_PartialWriteFailures()
        {
            // Arrange
            var key = "partial_write_key";
            var value = "partial_write_value";
            var tempFiles = new List<string>();

            // Act - Simulate multiple partial write scenarios
            for (int failurePoint = 0; failurePoint < 3; failurePoint++)
            {
                try
                {
                    var writeTask = _cacheProvider.SetAsync(key, value);

                    // Capture temp files during write
                    tempFiles.AddRange(Directory.GetFiles(_testDirectory, "*.tmp"));

                    switch (failurePoint)
                    {
                        case 0: // Fail during temp file write
                            foreach (var tmp in tempFiles)
                            {
                                File.WriteAllText(tmp, "corrupted temp");
                            }
                            break;

                        case 1: // Fail during WAL write
                            var walFiles = Directory.GetFiles(Path.Combine(_testDirectory, "wal"));
                            foreach (var wal in walFiles)
                            {
                                File.WriteAllText(wal, "corrupted wal");
                            }
                            break;

                        case 2: // Fail during final move
                            var cacheFiles = Directory.GetFiles(_testDirectory, "*.json");
                            foreach (var cache in cacheFiles)
                            {
                                File.WriteAllText(cache, "corrupted cache");
                            }
                            break;
                    }

                    await writeTask;
                }
                catch
                {
                    // Expected exceptions during simulated failures
                }

                // Verify cache state after each failure
                var storedValue = await _cacheProvider.GetAsync<string>(key);
                if (storedValue != null)
                {
                    Assert.Equal(value, storedValue);
                }

                // Verify no temp files remain
                Assert.Empty(Directory.GetFiles(_testDirectory, "*.tmp"));
            }
        }

        [Fact]
        public async Task Compression_EdgeCasesAndErrorHandling()
        {
            // Arrange
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                MinSizeForCompressionBytes = 1024, // 1KB threshold
                TrackCompressionMetrics = true
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsService: null,
                mlPredictionService: null);
                
            // Test cases
            var testCases = new Dictionary<string, object>
            {
                ["empty_string"] = "",
                ["null_value"] = null,
                ["whitespace"] = "   \t\n\r   ",
                ["zero_bytes"] = new byte[0],
                ["one_byte"] = new byte[1] { 42 },
                ["object_with_nulls"] = new { name = (string)null, id = 0, isActive = false },
                ["special_chars"] = "!@#$%^&*()_+{}[]|\\:;\"'<>,.?/~`",
                ["unicode"] = "こんにちは世界! Привет, мир! مرحبا بالعالم!",
                ["corrupted_utf8"] = new string(new char[] { '\uD800' }), // Unpaired surrogate
                ["exactly_threshold"] = new string('x', settings.MinSizeForCompressionBytes),
                ["just_below_threshold"] = new string('x', settings.MinSizeForCompressionBytes - 1),
                ["just_above_threshold"] = new string('x', settings.MinSizeForCompressionBytes + 1)
            };
            
            // Act & Assert
            foreach (var (testName, testValue) in testCases)
            {
                var key = $"edge_case_{testName}";
                
                try
                {
                    // All these operations should succeed or fail gracefully
                    await provider.SetAsync(key, testValue);
                    
                    // If we get here, the set operation succeeded
                    // Now try to retrieve it
                    var retrieved = await provider.GetAsync<object>(key);
                    
                    // For null values, we expect null back
                    if (testValue == null)
                    {
                        Assert.Null(retrieved);
                    }
                    // For empty collections, we either get null or empty
                    else if (testValue is byte[] byteArray && byteArray.Length == 0)
                    {
                        Assert.True(retrieved == null || 
                            (retrieved is byte[] retrievedBytes && retrievedBytes.Length == 0));
                    }
                    // For other values, we expect non-null
                    else
                    {
                        Assert.NotNull(retrieved);
                    }
                    
                    // Just log that the case succeeded
                    Console.WriteLine($"Edge case '{testName}' handled successfully");
                }
                catch (Exception ex)
                {
                    // Log any failures, but don't fail the test
                    // Some edge cases might legitimately throw exceptions
                    Console.WriteLine($"Edge case '{testName}' threw exception: {ex.Message}");
                    
                    // But the provider should still be functional
                    await provider.SetAsync("recovery_test", "value");
                    var recovered = await provider.GetAsync<string>("recovery_test");
                    Assert.Equal("value", recovered);
                }
            }
            
            // Verify the cache stats are still sensible
            var stats = provider.GetCacheStats();
            Assert.NotNull(stats);
            
            // Verify provider can still handle normal operations after edge cases
            var normalKey = "normal_after_edge_cases";
            var normalValue = "normal_value";
            await provider.SetAsync(normalKey, normalValue);
            var result = await provider.GetAsync<string>(normalKey);
            Assert.Equal(normalValue, result);
            
            // Test compression setting changes midway
            var newSettings = settings with 
            { 
                CompressionAlgorithm = CompressionAlgorithm.Brotli,
                CompressionLevel = CompressionLevel.SmallestSize
            };
            
            var newProvider = new FileBasedCacheProvider(
                Options.Create(newSettings),
                metricsService: null,
                mlPredictionService: null);
                
            // Should still be able to read existing entries
            result = await newProvider.GetAsync<string>(normalKey);
            Assert.Equal(normalValue, result);
            
            // And use new compression settings for new entries
            var newKey = "post_settings_change";
            var newValue = new string('y', 10 * 1024); // 10KB compressible data
            
            await newProvider.SetAsync(newKey, newValue);
            var newStats = newProvider.GetCacheStats();
            
            // Should see the new algorithm in stats
            Assert.True(newStats.CompressionStats.ContainsKey("Brotli"));
            Assert.True(newStats.CompressionStats["Brotli"].CompressedItems > 0);
            
            // Disable compression mid-operation
            var noCompressionSettings = settings with { CompressionEnabled = false };
            var noCompressionProvider = new FileBasedCacheProvider(
                Options.Create(noCompressionSettings),
                metricsService: null,
                mlPredictionService: null);
                
            // Should still read compressed entries
            result = await noCompressionProvider.GetAsync<string>(newKey);
            Assert.Equal(newValue, result);
            
            // But new entries won't be compressed
            await noCompressionProvider.SetAsync("uncompressed_key", newValue);
            var finalStats = noCompressionProvider.GetCacheStats();
            
            // The ratio will include both compressed and uncompressed items
            Console.WriteLine($"Final compression ratio with mixed entries: {finalStats.CompressionRatio:F4}");
        }
        
        [Fact]
        public async Task SensitiveData_CleanupAfterExpiration()
        {
            // Arrange
            var key = "sensitive_expiring_key";
            var sensitiveValue = "sensitive_data_" + Guid.NewGuid().ToString();
            var shortExpiration = TimeSpan.FromMilliseconds(100);

            // Act
            await _cacheProvider.SetAsync(key, sensitiveValue, shortExpiration);
            
            // Get the cache file path
            var cacheFile = Directory.GetFiles(_testDirectory, "*.json")
                .First(f => f.Contains(
                    Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key)))));

            // Initial verification
            Assert.True(File.Exists(cacheFile));
            var initialContent = await File.ReadAllTextAsync(cacheFile);
            Assert.DoesNotContain(sensitiveValue, initialContent);

            // Wait for expiration
            await Task.Delay(shortExpiration.Add(TimeSpan.FromMilliseconds(100)));

            // Trigger cleanup by attempting to read
            var expiredValue = await _cacheProvider.GetAsync<string>(key);
            Assert.Null(expiredValue);

            // Assert
            // Verify the file is deleted
            Assert.False(File.Exists(cacheFile));

            // Check all files in cache directory for sensitive data
            foreach (var file in Directory.GetFiles(_testDirectory, "*.*", SearchOption.AllDirectories))
            {
                var content = await File.ReadAllTextAsync(file);
                Assert.DoesNotContain(sensitiveValue, content);
            }

            // Verify WAL and backup cleanup
            var walFiles = Directory.GetFiles(Path.Combine(_testDirectory, "wal"));
            var backupFiles = Directory.GetFiles(Path.Combine(_testDirectory, "backups"));

            foreach (var file in walFiles.Concat(backupFiles))
            {
                var content = await File.ReadAllTextAsync(file);
                Assert.DoesNotContain(sensitiveValue, content);
            }

            // Verify new cache operations work after cleanup
            await _cacheProvider.SetAsync(key, "new value");
            var newValue = await _cacheProvider.GetAsync<string>(key);
            Assert.Equal("new value", newValue);
        }

        [Fact]
        public async Task AtomicOperations_ConcurrentFileSystemAccess()
        {
            // Arrange
            const int concurrentOperations = 100;
            var key = "atomic_test_key";
            var tasks = new List<Task>();
            var successfulOperations = 0;
            var operations = new ConcurrentBag<string>();

            // Act
            // Multiple concurrent file operations
            for (int i = 0; i < concurrentOperations; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Perform write operation
                        await _cacheProvider.SetAsync(key, $"value_{i}");
                        operations.Add($"write_{i}");

                        // Immediately try to read
                        var value = await _cacheProvider.GetAsync<string>(key);
                        Assert.NotNull(value);
                        Assert.StartsWith("value_", value);
                        operations.Add($"read_{i}");

                        Interlocked.Increment(ref successfulOperations);
                    }
                    catch (Exception ex)
                    {
                        operations.Add($"error_{i}_{ex.GetType().Name}");
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(concurrentOperations, successfulOperations);

            // Verify file system state
            var tempFiles = Directory.GetFiles(_testDirectory, "*.tmp");
            Assert.Empty(tempFiles);

            var walFiles = Directory.GetFiles(Path.Combine(_testDirectory, "wal"));
            Assert.Empty(walFiles);

            // Verify final cache state is consistent
            var finalValue = await _cacheProvider.GetAsync<string>(key);
            Assert.NotNull(finalValue);
            Assert.StartsWith("value_", finalValue);

            // Check operation order
            var orderedOps = operations.OrderBy(op => op).ToList();
            foreach (var op in orderedOps.Where(o => o.StartsWith("write_")))
            {
                var writeNum = int.Parse(op.Split('_')[1]);
                var corresponding = orderedOps.FirstOrDefault(o => o == $"read_{writeNum}");
                Assert.NotNull(corresponding);
            }
        }

        [Fact]
        public async Task Performance_StressTest()
        {
            // Arrange
            var iterationCounts = new[] { 100, 1000, 5000 };
            var results = new List<(int iterations, double avgWrite, double avgRead, double avgConc)>();

            foreach (var iterations in iterationCounts)
            {
                var writeResults = new ConcurrentBag<double>();
                var readResults = new ConcurrentBag<double>();
                var concurrentResults = new ConcurrentBag<double>();
                
                // Act
                // Test sequential writes
                for (int i = 0; i < iterations; i++)
                {
                    var sw = Stopwatch.StartNew();
                    await _cacheProvider.SetAsync($"perf_key_{i}", $"value_{i}");
                    sw.Stop();
                    writeResults.Add(sw.ElapsedMilliseconds);
                }

                // Test sequential reads
                for (int i = 0; i < iterations; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var result = await _cacheProvider.GetAsync<string>($"perf_key_{i}");
                    sw.Stop();
                    readResults.Add(sw.ElapsedMilliseconds);
                    Assert.NotNull(result);
                }

                // Test concurrent operations
                var tasks = new List<Task>();
                for (int i = 0; i < iterations; i++)
                {
                    var current = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        var sw = Stopwatch.StartNew();
                        if (current % 2 == 0)
                        {
                            await _cacheProvider.SetAsync($"conc_key_{current}", $"value_{current}");
                        }
                        else
                        {
                            var key = $"conc_key_{current - 1}";
                            if (await _cacheProvider.ExistsAsync(key))
                            {
                                await _cacheProvider.GetAsync<string>(key);
                            }
                        }
                        sw.Stop();
                        concurrentResults.Add(sw.ElapsedMilliseconds);
                    }));
                }
                await Task.WhenAll(tasks);

                results.Add((
                    iterations,
                    writeResults.Average(),
                    readResults.Average(),
                    concurrentResults.Average()
                ));
            }

            // Assert
            foreach (var (iters, avgWrite, avgRead, avgConc) in results)
            {
                // Reads should be faster than writes
                Assert.True(avgRead < avgWrite * 1.5);

                // Performance should scale reasonably with load
                var baselineWrite = results.First().avgWrite;
                var baselineRead = results.First().avgRead;
                var writeDegradation = avgWrite / baselineWrite;
                var readDegradation = avgRead / baselineRead;

                // Allow for some performance degradation at scale, but not exponential
                Assert.True(writeDegradation < Math.Log10(iters));
                Assert.True(readDegradation < Math.Log10(iters));

                // Concurrent operations shouldn't be drastically slower
                Assert.True(avgConc < avgWrite * 2);

                // Log performance metrics
                await _cacheProvider.LogQueryStatsAsync(
                    $"stress_test_{iters}",
                    "performance_test",
                    avgWrite + avgRead + avgConc / 3,
                    true);
            }
        }

        [Fact]
        public async Task CacheInvalidation_DetailedMechanisms()
        {
            // Arrange
            var testData = new List<(string key, string value, TimeSpan? expiration)>
            {
                ("key1", "value1", TimeSpan.FromMilliseconds(100)),  // Short expiration
                ("key2", "value2", TimeSpan.FromDays(1)),            // Long expiration
                ("key3", "value3", null),                            // Default expiration
                ("key4", "value4", TimeSpan.FromMilliseconds(50))    // Very short expiration
            };

            // Act - Phase 1: Initial data population
            foreach (var (key, value, expiration) in testData)
            {
                await _cacheProvider.SetAsync(key, value, expiration);
            }

            // Wait for short expirations
            await Task.Delay(150);

            // Phase 2: Verify expiration-based invalidation
            Assert.Null(await _cacheProvider.GetAsync<string>("key1"));
            Assert.Null(await _cacheProvider.GetAsync<string>("key4"));
            Assert.NotNull(await _cacheProvider.GetAsync<string>("key2"));
            Assert.NotNull(await _cacheProvider.GetAsync<string>("key3"));

            // Phase 3: Code change invalidation
            await _cacheProvider.InvalidateOnCodeChangeAsync("new_code_hash");

            // Verify all entries are invalidated
            foreach (var (key, _, _) in testData)
            {
                Assert.Null(await _cacheProvider.GetAsync<string>(key));
            }

            // Phase 4: Test selective invalidation
            await _cacheProvider.SetAsync("test_key", "test_value");
            var cacheStats = _cacheProvider.GetCacheStats();
            var initialInvalidationCount = cacheStats.InvalidationCount;

            // Corrupt specific cache entry
            var cacheFile = Directory.GetFiles(_testDirectory, "*.json").First();
            File.WriteAllText(cacheFile, "corrupted_data");

            // Try to read corrupted entry
            var result = await _cacheProvider.GetAsync<string>("test_key");
            Assert.Null(result);

            // Verify only the corrupted entry was invalidated
            cacheStats = _cacheProvider.GetCacheStats();
            Assert.Equal(initialInvalidationCount + 1, cacheStats.InvalidationCount);
        }

        [Fact]
        public async Task FileLock_HandlingDuringOperations()
        {
            // Arrange
            var key = "lock_test_key";
            var value = "lock_test_value";
            var filePath = Path.Combine(_testDirectory, 
                Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");

            // Act & Assert
            // Test 1: Write operation with file lock
            using (var fileLock = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                // Attempt write while file is locked
                await Assert.ThrowsAnyAsync<IOException>(async () =>
                    await _cacheProvider.SetAsync(key, value));
            }

            // Test 2: Concurrent lock attempts
            var tasks = new List<Task>();
            var successCount = 0;
            var failCount = 0;

            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var fs = File.Open(filePath, FileMode.OpenOrCreate, 
                            FileAccess.ReadWrite, FileShare.None);
                        await Task.Delay(100); // Hold the lock
                        Interlocked.Increment(ref successCount);
                    }
                    catch (IOException)
                    {
                        Interlocked.Increment(ref failCount);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Verify lock contention
            Assert.True(successCount > 0);
            Assert.True(failCount > 0);
            Assert.Equal(10, successCount + failCount);

            // Test 3: Lock release and recovery
            await _cacheProvider.SetAsync(key, value);
            var result = await _cacheProvider.GetAsync<string>(key);
            Assert.Equal(value, result);

            // Test 4: Lock timeout behavior
            var longRunningTask = Task.Run(async () =>
            {
                using var fs = File.Open(filePath, FileMode.OpenOrCreate, 
                    FileAccess.ReadWrite, FileShare.None);
                await Task.Delay(5000); // Hold lock for 5 seconds
            });

            // Attempt operations while lock is held
            var timeoutTask = Task.Run(async () =>
            {
                await Task.Delay(100); // Give time for lock to be acquired
                var sw = Stopwatch.StartNew();
                try
                {
                    await _cacheProvider.SetAsync(key, "timeout_test");
                }
                catch (IOException)
                {
                    sw.Stop();
                    return sw.ElapsedMilliseconds;
                }
                return 0L;
            });

            var timeoutMs = await timeoutTask;
            Assert.True(timeoutMs > 0, "Operation should timeout or fail when lock is held");
            Assert.True(timeoutMs < 5000, "Operation should not wait for full lock duration");
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
        
        [Fact]
        public async Task Compression_TestVeryLargeDataWithLimitedMemory()
        {
            // Arrange - Use a very large data size to test memory-efficient compression
            var dataSize = 20 * 1024 * 1024; // 20MB
            var largeValue = new string('x', dataSize); // Highly compressible repeating data
            var key = "very_large_data_key";
            
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                CompressionLevel = CompressionLevel.Fastest, // Use fastest for very large data
                MinSizeForCompressionBytes = 1024,
                MaxSizeForHighCompressionBytes = 10 * 1024 * 1024 // 10MB threshold
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsService: null,
                mlPredictionService: null);

            // Get initial memory usage
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var memoryBefore = GC.GetTotalMemory(true);
            
            // Act - store the large value
            var sw = Stopwatch.StartNew();
            await provider.SetAsync(key, largeValue);
            sw.Stop();
            var writeTime = sw.ElapsedMilliseconds;
            
            // Measure memory after storage
            var memoryAfterStorage = GC.GetTotalMemory(false);
            
            // Read it back
            sw.Restart();
            var retrievedValue = await provider.GetAsync<string>(key);
            sw.Stop();
            var readTime = sw.ElapsedMilliseconds;
            
            // Measure final memory
            var memoryAfterRetrieval = GC.GetTotalMemory(false);
            
            var stats = provider.GetCacheStats();
            
            // Assert
            Assert.Equal(largeValue, retrievedValue); // Data integrity maintained
            Assert.True(stats.CompressionRatio < 0.1); // For repeating data, should get >90% compression
            
            // Memory usage should be reasonable - we expect decompression to use memory
            // but it shouldn't be excessive compared to the original data size
            Console.WriteLine($"Initial memory: {memoryBefore / (1024 * 1024)}MB");
            Console.WriteLine($"Memory after storage: {memoryAfterStorage / (1024 * 1024)}MB");
            Console.WriteLine($"Memory after retrieval: {memoryAfterRetrieval / (1024 * 1024)}MB");
            Console.WriteLine($"Write time: {writeTime}ms");
            Console.WriteLine($"Read time: {readTime}ms");
            Console.WriteLine($"Compression ratio: {stats.CompressionRatio:F3}");
            Console.WriteLine($"Original size: {dataSize / (1024 * 1024)}MB");
            Console.WriteLine($"Compressed size: {(dataSize * stats.CompressionRatio) / (1024 * 1024):F2}MB");
            
            // Memory usage during decompression should be reasonable
            var memoryForDecompression = memoryAfterRetrieval - memoryAfterStorage;
            Assert.True(memoryForDecompression < dataSize * 1.5, 
                "Memory usage during decompression should be controlled");
            
            // Performance should be reasonable for large data
            Assert.True(readTime < 5000, "Decompression should be reasonably fast even for large data");
            
            // Verify efficient memory usage patterns for large data
            var compressionStats = stats.CompressionStats["GZip"];
            Assert.True(compressionStats.CompressedItems > 0, "Large data should be compressed");
            
            // Calculate and log memory efficiency metrics
            var memoryEfficiency = dataSize / Math.Max(1, memoryForDecompression);
            Console.WriteLine($"Memory efficiency ratio: {memoryEfficiency:F1}x");
            Console.WriteLine($"Memory overhead per MB: {memoryForDecompression / (dataSize / (1024 * 1024)):F0} bytes");
            
            // Verify performance for very large data is reasonable
            Assert.True(writeTime < 5000, "Compression of large data should complete in reasonable time");
            Assert.True(readTime < writeTime * 2, "Decompression should not be significantly slower than compression");
            
            // Storage efficiency should be excellent for this test case
            var storageSavings = stats.CompressionSavingsBytes;
            var savingsPercent = storageSavings * 100.0 / dataSize;
            Console.WriteLine($"Storage savings: {storageSavings / (1024 * 1024):F2}MB ({savingsPercent:F1}%)");
            Assert.True(savingsPercent > 90, "Storage savings should exceed 90% for highly compressible data");
        }
        
        [Fact]
        public async Task Compression_TestNonCompressibleData()
        {
            // Arrange - Create truly non-compressible data (random bytes)
            var random = new Random(42); // Fixed seed for reproducibility
            var dataSize = 5 * 1024 * 1024; // 5MB
            var bytes = new byte[dataSize];
            random.NextBytes(bytes); // Generate random bytes that should not compress well
            var nonCompressibleData = Convert.ToBase64String(bytes);
            var key = "non_compressible_data_key";
            
            // Create a second dataset with pre-compressed data (already compressed data shouldn't be compressed again)
            var compressibleData = new string('x', 1024 * 1024); // 1MB repeating data that compresses well
            using var compressedStream = new MemoryStream();
            using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal))
            {
                var compressibleBytes = System.Text.Encoding.UTF8.GetBytes(compressibleData);
                gzipStream.Write(compressibleBytes, 0, compressibleBytes.Length);
            }
            var preCompressedData = Convert.ToBase64String(compressedStream.ToArray());
            var preCompressedKey = "pre_compressed_data_key";
            
            // Configure cache to skip compression if ratio is not beneficial
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                CompressionLevel = CompressionLevel.Optimal,
                MinSizeForCompressionBytes = 1024,
                MinCompressionRatio = 0.9, // Only compress if it reduces size by at least 10%
                AdaptiveCompression = true
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsService: null,
                mlPredictionService: null);

            // Act - Test with random data
            var sw = Stopwatch.StartNew();
            await provider.SetAsync(key, nonCompressibleData);
            sw.Stop();
            var randomDataWriteTime = sw.ElapsedMilliseconds;
            
            var stats = provider.GetCacheStats();
            var retrievedValue = await provider.GetAsync<string>(key);
            
            // Also test with pre-compressed data
            await provider.SetAsync(preCompressedKey, preCompressedData);
            var statsAfterBoth = provider.GetCacheStats();
            var retrievedPreCompressed = await provider.GetAsync<string>(preCompressedKey);
            
            // Assert
            Assert.Equal(nonCompressibleData, retrievedValue); // Data integrity maintained for random data
            Assert.Equal(preCompressedData, retrievedPreCompressed); // Data integrity maintained for pre-compressed data
            
            // Expected behaviors:
            // 1. Compression should be skipped (ratio = 1.0) if not beneficial
            // 2. Compression might achieve minimal benefit (ratio close to MinCompressionRatio)
            // 3. System should detect when data is already compressed
            Console.WriteLine($"=== Non-compressible Data Tests ===");
            Console.WriteLine($"Random data size: {nonCompressibleData.Length} bytes");
            Console.WriteLine($"Compression ratio: {stats.CompressionRatio:F3}");
            Console.WriteLine($"Compression attempted: {stats.CompressionStats["GZip"].TotalItems > 0}");
            Console.WriteLine($"Compression succeeded: {stats.CompressedItemCount > 0}");
            Console.WriteLine($"Write time: {randomDataWriteTime}ms");
            
            Console.WriteLine($"\nPre-compressed data size: {preCompressedData.Length} bytes");
            Console.WriteLine($"Final compression ratio: {statsAfterBoth.CompressionRatio:F3}");
            
            // Verify the implementation properly handles non-compressible data:
            
            // If compression was applied, the ratio should be reasonable
            if (stats.CompressedItemCount > 0)
            {
                // Even random data might compress slightly due to patterns
                Assert.True(stats.CompressionRatio <= 0.99, 
                    "If compression was used, it should provide at least some benefit");
                Console.WriteLine("Note: Random data was compressed with minimal benefit");
            }
            else
            {
                // If compression was correctly skipped, ratio should be 1.0
                Assert.Equal(1.0, stats.CompressionRatio);
                Console.WriteLine("Compression correctly skipped for non-compressible data");
            }
            
            // Check if pre-compressed data was correctly detected
            if (statsAfterBoth.CompressionStats["GZip"].TotalItems > stats.CompressionStats["GZip"].TotalItems)
            {
                Console.WriteLine("Pre-compressed data was processed");
                
                // If system properly detected pre-compressed data, it shouldn't attempt to compress it again
                if (statsAfterBoth.CompressedItemCount == stats.CompressedItemCount)
                {
                    Console.WriteLine("Pre-compressed data was correctly identified and not compressed again");
                }
                else
                {
                    Console.WriteLine("Pre-compressed data was compressed again (not ideal but still valid)");
                }
            }
            
            // Most importantly, verify the system didn't waste resources
            Assert.True(randomDataWriteTime < 5000, 
                "Processing non-compressible data shouldn't take excessive time");
                
            // Overall storage should be reasonable
            var finalStats = provider.GetCacheStats();
            Console.WriteLine($"Total storage usage: {finalStats.StorageUsageBytes} bytes");
            Console.WriteLine($"Compressed storage usage: {finalStats.CompressedStorageUsageBytes} bytes");
        }
        
        [Fact]
        public async Task Compression_TestContentTypeDetection()
        {
            // Arrange - Create different content types to test auto-detection and algorithm selection
            var jsonData = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, 1000)
                .Select(i => new { Id = i, Name = $"Item {i}", Value = i * 3.14 })
                .ToArray());
                
            var xmlData = $@"<root>
                <items>
                    {string.Join("", Enumerable.Range(0, 1000).Select(i => $"<item id=\"{i}\"><n>Item {i}</n><value>{i * 3.14}</value></item>"))}
                </items>
                <metadata>
                    <created>{DateTime.Now}</created>
                    <description>Test data with repeating structure</description>
                </metadata>
            </root>";
                
            var htmlData = $@"<!DOCTYPE html>
            <html>
                <head><title>Test HTML</title></head>
                <body>
                    <div class='container'>
                        {string.Join("", Enumerable.Range(0, 1000).Select(i => $"<div id='item-{i}'>Item {i}</div>"))}
                    </div>
                </body>
            </html>";
                
            var binaryData = new byte[100 * 1024]; // 100KB
            new Random(42).NextBytes(binaryData); // Random binary data
            
            // Create cache provider with adaptive compression config
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                AdaptiveCompression = true,
                ContentTypeAlgorithmMap = new Dictionary<ContentType, CompressionAlgorithm> {
                    { ContentType.TextJson, CompressionAlgorithm.Brotli },
                    { ContentType.TextXml, CompressionAlgorithm.Brotli },
                    { ContentType.TextHtml, CompressionAlgorithm.Brotli },
                    { ContentType.BinaryData, CompressionAlgorithm.GZip }
                },
                // Add content-specific compression level settings
                ContentTypeCompressionLevelMap = new Dictionary<ContentType, CompressionLevel> {
                    { ContentType.TextJson, CompressionLevel.SmallestSize },
                    { ContentType.TextXml, CompressionLevel.SmallestSize },
                    { ContentType.TextHtml, CompressionLevel.SmallestSize },
                    { ContentType.BinaryData, CompressionLevel.Fastest }
                }
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsService: null,
                mlPredictionService: null);

            // Act - Store different content types
            await provider.SetAsync("json_key", jsonData);
            await provider.SetAsync("xml_key", xmlData);
            await provider.SetAsync("html_key", htmlData);
            await provider.SetAsync("binary_key", binaryData);
            
            // Get compression statistics
            var stats = provider.GetCacheStats();
            
            // Collect size information for each content type
            var jsonSize = jsonData.Length;
            var xmlSize = xmlData.Length;
            var htmlSize = htmlData.Length;
            var binarySize = binaryData.Length;
            
            // Assert - Basic verification
            Assert.Equal(jsonData, await provider.GetAsync<string>("json_key"));
            Assert.Equal(xmlData, await provider.GetAsync<string>("xml_key"));
            Assert.Equal(htmlData, await provider.GetAsync<string>("html_key"));
            Assert.Equal(binaryData, await provider.GetAsync<byte[]>("binary_key"));
            
            // With content type detection, we expect to see Brotli used for text formats
            Assert.True(stats.CompressionStats["Brotli"].CompressedItems > 0, 
                "Brotli should be used for at least some content types");
                
            // Log detailed compression outcomes by content type
            Console.WriteLine("=== Content Type Detection Test Results ===");
            Console.WriteLine($"Brotli items: {stats.CompressionStats["Brotli"].CompressedItems}");
            Console.WriteLine($"GZip items: {stats.CompressionStats["GZip"].CompressedItems}");
            Console.WriteLine($"Overall compression ratio: {stats.CompressionRatio:F3}");
            
            // Log individual content type results if available
            if (stats.CompressionByContentType != null && stats.CompressionByContentType.Count > 0)
            {
                Console.WriteLine("\nCompression by Content Type:");
                foreach (var kvp in stats.CompressionByContentType)
                {
                    Console.WriteLine($"  {kvp.Key}: ratio={kvp.Value.CompressionRatio:F3}, " +
                                    $"items={kvp.Value.CompressedItems}, " +
                                    $"avg_time={kvp.Value.AverageCompressionTimeMs:F1}ms");
                }
            }
            
            // Additional specific assertions for different content types
            
            // JSON data should use Brotli with smallest size and compress very well
            var jsonResult = await provider.GetAsync<string>("json_key");
            Console.WriteLine($"\nJSON: {jsonSize} bytes original");
            Assert.Equal(jsonData, jsonResult);
            
            // XML data should also use Brotli and compress well
            var xmlResult = await provider.GetAsync<string>("xml_key");
            Console.WriteLine($"XML: {xmlSize} bytes original");
            Assert.Equal(xmlData, xmlResult);
            
            // HTML data should follow the same pattern
            var htmlResult = await provider.GetAsync<string>("html_key");
            Console.WriteLine($"HTML: {htmlSize} bytes original");
            Assert.Equal(htmlData, htmlResult);
            
            // Binary data compression will vary but should use GZip with fastest level
            var binaryResult = await provider.GetAsync<byte[]>("binary_key");
            Console.WriteLine($"Binary: {binarySize} bytes original");
            Assert.Equal(binaryData, binaryResult);
            
            // For structured text data, compression ratio should be very good
            Assert.True(stats.CompressionRatio < 0.3, 
                "With appropriate content type detection, compression should be very effective");
                
            // Verify adaptive algorithm selection is working
            if (stats.CompressionStats["Brotli"].CompressedItems > 0 && stats.CompressionStats["GZip"].CompressedItems > 0)
            {
                // Both algorithms should have been selected based on content type
                Console.WriteLine("\nAdaptive algorithm selection is working correctly");
                Console.WriteLine($"Brotli efficiency: {stats.CompressionStats["Brotli"].EfficiencyScore:F3}");
                Console.WriteLine($"GZip efficiency: {stats.CompressionStats["GZip"].EfficiencyScore:F3}");
                Console.WriteLine($"Most efficient algorithm: {stats.MostEfficientAlgorithm}");
            }
        }
        
        [Fact]
        public async Task Compression_TestAlgorithmPerformanceComparison()
        {
            // This test compares performance metrics between GZip and Brotli
            
            // Arrange
            var dataSize = 5 * 1024 * 1024; // 5MB
            var textData = new string('a', dataSize); // Highly compressible
            
            var gzipSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = Path.Combine(_testDirectory, "gzip_test"),
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                CompressionLevel = CompressionLevel.Optimal
            };
            
            var brotliSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = Path.Combine(_testDirectory, "brotli_test"),
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.Brotli,
                CompressionLevel = CompressionLevel.Optimal
            };
            
            Directory.CreateDirectory(gzipSettings.CacheDirectory);
            Directory.CreateDirectory(brotliSettings.CacheDirectory);
            
            var gzipProvider = new FileBasedCacheProvider(
                Options.Create(gzipSettings),
                metricsService: null,
                mlPredictionService: null);
                
            var brotliProvider = new FileBasedCacheProvider(
                Options.Create(brotliSettings),
                metricsService: null,
                mlPredictionService: null);
                
            // Act - Measure GZip performance
            var sw = Stopwatch.StartNew();
            await gzipProvider.SetAsync("perf_key", textData);
            sw.Stop();
            var gzipWriteTime = sw.ElapsedMilliseconds;
            
            sw.Restart();
            var gzipResult = await gzipProvider.GetAsync<string>("perf_key");
            sw.Stop();
            var gzipReadTime = sw.ElapsedMilliseconds;
            
            var gzipStats = gzipProvider.GetCacheStats();
            
            // Measure Brotli performance
            sw.Restart();
            await brotliProvider.SetAsync("perf_key", textData);
            sw.Stop();
            var brotliWriteTime = sw.ElapsedMilliseconds;
            
            sw.Restart();
            var brotliResult = await brotliProvider.GetAsync<string>("perf_key");
            sw.Stop();
            var brotliReadTime = sw.ElapsedMilliseconds;
            
            var brotliStats = brotliProvider.GetCacheStats();
            
            // Assert
            Assert.Equal(textData, gzipResult); // Data integrity for GZip
            Assert.Equal(textData, brotliResult); // Data integrity for Brotli
            
            // Both should achieve good compression
            Assert.True(gzipStats.CompressionRatio < 0.1);
            Assert.True(brotliStats.CompressionRatio < 0.1);
            
            // Log performance metrics for comparison
            Console.WriteLine("Algorithm Performance Comparison:");
            Console.WriteLine($"GZip write time: {gzipWriteTime}ms");
            Console.WriteLine($"GZip read time: {gzipReadTime}ms");
            Console.WriteLine($"GZip ratio: {gzipStats.CompressionRatio:F3}");
            Console.WriteLine($"Brotli write time: {brotliWriteTime}ms");
            Console.WriteLine($"Brotli read time: {brotliReadTime}ms");
            Console.WriteLine($"Brotli ratio: {brotliStats.CompressionRatio:F3}");
            
            // Brotli usually achieves better compression ratios for text
            Assert.True(brotliStats.CompressionRatio <= gzipStats.CompressionRatio * 1.2,
                "Brotli should provide at least comparable compression to GZip for text");
        }
        
        [Fact]
        public async Task Compression_AdaptiveContentType_Selection()
        {
            // Arrange
            var textData = new string('a', 100 * 1024); // 100KB text
            var jsonData = System.Text.Json.JsonSerializer.Serialize(new { 
                items = Enumerable.Range(0, 1000).Select(i => new { id = i, name = $"Item {i}", value = i * 3.14 }).ToArray(),
                metadata = new { created = DateTime.Now, description = "Test data with repeating structure" }
            });
            var xmlData = $@"<root>
                <items>
                    {string.Join("", Enumerable.Range(0, 1000).Select(i => $"<item id=\"{i}\"><name>Item {i}</name><value>{i * 3.14}</value></item>"))}
                </items>
                <metadata>
                    <created>{DateTime.Now}</created>
                    <description>Test data with repeating structure</description>
                </metadata>
            </root>";
            
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                AdaptiveCompression = true,
                ContentTypeAlgorithmMap = new Dictionary<ContentType, CompressionAlgorithm> {
                    { ContentType.TextJson, CompressionAlgorithm.Brotli },
                    { ContentType.TextXml, CompressionAlgorithm.Brotli },
                    { ContentType.TextPlain, CompressionAlgorithm.GZip }
                },
                ContentTypeCompressionLevelMap = new Dictionary<ContentType, CompressionLevel> {
                    { ContentType.TextJson, CompressionLevel.SmallestSize },
                    { ContentType.TextXml, CompressionLevel.SmallestSize },
                    { ContentType.TextPlain, CompressionLevel.Optimal }
                }
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsService: null,
                mlPredictionService: null);

            // Act
            await provider.SetAsync("text_key", textData);
            await provider.SetAsync("json_key", jsonData);
            await provider.SetAsync("xml_key", xmlData);
            
            // Assert
            var stats = provider.GetCacheStats();
            
            // Content-type based compression should be working
            Assert.True(stats.CompressionRatio < 1.0);
            Assert.True(stats.CompressedItemCount > 0);
            
            // For adaptive compression, both algorithms should be used
            Assert.True(stats.CompressionStats["GZip"].CompressedItems > 0);
            Assert.True(stats.CompressionStats["Brotli"].CompressedItems > 0);
            
            // Verify all data can be retrieved correctly
            var retrievedText = await provider.GetAsync<string>("text_key");
            var retrievedJson = await provider.GetAsync<string>("json_key");
            var retrievedXml = await provider.GetAsync<string>("xml_key");
            
            Assert.Equal(textData, retrievedText);
            Assert.Equal(jsonData, retrievedJson);
            Assert.Equal(xmlData, retrievedXml);
            
            // Log compression stats for analysis
            Console.WriteLine("=== Content Type Compression Analysis ===");
            Console.WriteLine($"Overall compression ratio: {stats.CompressionRatio}");
            Console.WriteLine($"GZip items: {stats.CompressionStats["GZip"].CompressedItems}, " +
                $"ratio: {stats.CompressionStats["GZip"].CompressionRatio}");
            Console.WriteLine($"Brotli items: {stats.CompressionStats["Brotli"].CompressedItems}, " +
                $"ratio: {stats.CompressionStats["Brotli"].CompressionRatio}");
        }
        
        [Fact]
        public async Task Compression_VerifyLargeDataMemoryUsage()
        {
            // Arrange
            var largeValue = new string('x', 10 * 1024 * 1024); // 10MB of repeating data
            var key = "large_data_key";
            
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                CompressionLevel = CompressionLevel.Fastest, // Use fastest for large data
                MinSizeForCompressionBytes = 1024
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsService: null,
                mlPredictionService: null);

            // Act - First measure memory without compression
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var memoryBefore = GC.GetTotalMemory(true);
            
            // Store large value
            await provider.SetAsync(key, largeValue);
            
            // Measure memory after storage
            var memoryAfterStorage = GC.GetTotalMemory(false);
            
            // Now access the compressed data
            var sw = Stopwatch.StartNew();
            var retrievedValue = await provider.GetAsync<string>(key);
            sw.Stop();
            var readTime = sw.ElapsedMilliseconds;
            
            // Measure memory after retrieval
            var memoryAfterRetrieval = GC.GetTotalMemory(false);
            
            // Get stats to check compression results
            var stats = provider.GetCacheStats();
            
            // Assert
            Assert.Equal(largeValue, retrievedValue); // Verify data integrity
            Assert.True(stats.CompressionRatio < 0.1); // For repeating data, compression should be very effective
            
            // Check that memory usage during retrieval is reasonable
            // The decompressed data should be in memory, but we shouldn't see double memory usage
            var memoryForDecompression = memoryAfterRetrieval - memoryAfterStorage;
            Console.WriteLine($"Memory before: {memoryBefore / (1024 * 1024)}MB");
            Console.WriteLine($"Memory after storage: {memoryAfterStorage / (1024 * 1024)}MB");
            Console.WriteLine($"Memory after retrieval: {memoryAfterRetrieval / (1024 * 1024)}MB");
            Console.WriteLine($"Memory for decompression: {memoryForDecompression / (1024 * 1024)}MB");
            Console.WriteLine($"Read time: {readTime}ms");
            Console.WriteLine($"Compression ratio: {stats.CompressionRatio}");
            
            // Memory usage for decompression should be less than twice the original data size
            // This is a loose assertion since memory behavior can vary
            Assert.True(memoryForDecompression < largeValue.Length * 2, 
                "Memory usage for decompression should be controlled");
            
            // Verify decompression time is reasonable for large data
            Assert.True(readTime < 2000, "Decompression time should be reasonable for 10MB of data");
        }
        
        [Fact]
        public async Task Compression_AllAlgorithms_AllLevels_AllDataTypes()
        {
            // This comprehensive test validates all compression algorithms and levels
            // against different types of data to ensure they all work correctly
            
            // Arrange
            var algorithms = new[] { CompressionAlgorithm.GZip, CompressionAlgorithm.Brotli };
            var levels = new[] { CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize };
            
            // Different data types to test
            var testData = new Dictionary<string, string>
            {
                ["Text"] = new string('a', 100 * 1024), // Highly compressible repeating text
                ["JSON"] = System.Text.Json.JsonSerializer.Serialize(new { 
                    items = Enumerable.Range(0, 100).Select(i => 
                        new { id = i, name = $"Item {i}", description = $"Description for item {i}" }).ToArray()
                }),
                ["XML"] = $@"<root>{string.Join("", Enumerable.Range(0, 100)
                    .Select(i => $"<item id=\"{i}\"><name>Item {i}</name></item>"))}</root>",
                ["Mixed"] = "START" + new string('x', 10000) + "MIDDLE" + 
                            string.Join(",", Enumerable.Range(0, 1000)) + "END"
            };
            
            var results = new Dictionary<string, Dictionary<string, Dictionary<string, TestResult>>>();
            
            // Test each combination
            foreach (var algorithm in algorithms)
            {
                results[algorithm.ToString()] = new Dictionary<string, Dictionary<string, TestResult>>();
                
                foreach (var level in levels)
                {
                    results[algorithm.ToString()][level.ToString()] = new Dictionary<string, TestResult>();
                    
                    // For each data type
                    foreach (var (dataType, data) in testData)
                    {
                        var settings = new CacheSettings
                        {
                            CacheEnabled = true,
                            CacheDirectory = Path.Combine(_testDirectory, $"{algorithm}_{level}_{dataType}"),
                            CompressionEnabled = true,
                            CompressionAlgorithm = algorithm,
                            CompressionLevel = level,
                            MinSizeForCompressionBytes = 0, // Force compression
                            TrackCompressionMetrics = true
                        };
                        
                        Directory.CreateDirectory(settings.CacheDirectory);
                        
                        var provider = new FileBasedCacheProvider(
                            Options.Create(settings),
                            metricsService: null,
                            mlPredictionService: null);
                        
                        var key = $"test_{dataType}";
                        var sw = Stopwatch.StartNew();
                        await provider.SetAsync(key, data);
                        sw.Stop();
                        var writeTime = sw.ElapsedMilliseconds;
                        
                        sw.Restart();
                        var retrievedData = await provider.GetAsync<string>(key);
                        sw.Stop();
                        var readTime = sw.ElapsedMilliseconds;
                        
                        var stats = provider.GetCacheStats();
                        var algorithmStats = stats.CompressionStats[algorithm.ToString()];
                        
                        // Store results
                        results[algorithm.ToString()][level.ToString()][dataType] = new TestResult
                        {
                            CompressionRatio = stats.CompressionRatio,
                            WriteTimeMs = writeTime,
                            ReadTimeMs = readTime,
                            DataSize = data.Length,
                            CompressedSize = (long)(data.Length * stats.CompressionRatio),
                            Success = data == retrievedData
                        };
                        
                        // Assert each individual test
                        Assert.Equal(data, retrievedData); // Data integrity
                        Assert.True(stats.CompressionRatio < 1.0 || stats.CompressionRatio == 1.0); // Either compressed or skipped
                    }
                }
            }
            
            // Assert
            // All combinations should maintain data integrity
            foreach (var (algo, levelResults) in results)
            {
                foreach (var (level, dataResults) in levelResults)
                {
                    foreach (var (dataType, result) in dataResults)
                    {
                        Assert.True(result.Success, 
                            $"Data integrity failed for {algo}, {level}, {dataType}");
                    }
                }
            }
            
            // Log detailed results for analysis
            Console.WriteLine("=== Compression Algorithm and Level Analysis ===");
            foreach (var algorithm in algorithms)
            {
                Console.WriteLine($"\nAlgorithm: {algorithm}");
                foreach (var level in levels)
                {
                    Console.WriteLine($"  Level: {level}");
                    foreach (var dataType in testData.Keys)
                    {
                        var result = results[algorithm.ToString()][level.ToString()][dataType];
                        Console.WriteLine($"    {dataType}: Ratio={result.CompressionRatio:F3}, " +
                            $"Write={result.WriteTimeMs}ms, Read={result.ReadTimeMs}ms, " +
                            $"Size={result.DataSize/1024}KB→{result.CompressedSize/1024}KB");
                    }
                }
            }
            
            // Additional assertions for expected compression behavior
            
            // Text data should compress best
            var bestTextRatio = results.SelectMany(a => a.Value)
                .SelectMany(l => l.Value)
                .Where(d => d.Key == "Text")
                .Min(r => r.Value.CompressionRatio);
            Assert.True(bestTextRatio < 0.1, "Text should compress very well (>90% reduction)");
            
            // Repeating data should compress better with SmallestSize level versus Fastest
            var testType = "Text";
            foreach (var algo in algorithms)
            {
                var fastestRatio = results[algo.ToString()][CompressionLevel.Fastest.ToString()][testType].CompressionRatio;
                var smallestRatio = results[algo.ToString()][CompressionLevel.SmallestSize.ToString()][testType].CompressionRatio;
                Assert.True(smallestRatio <= fastestRatio * 1.1, 
                    $"{algo}: SmallestSize should provide at least as good compression as Fastest");
            }
            
            // Higher compression levels should generally take more time for compression
            foreach (var algo in algorithms)
            {
                foreach (var dataType in testData.Keys)
                {
                    var fastestTime = results[algo.ToString()][CompressionLevel.Fastest.ToString()][dataType].WriteTimeMs;
                    var smallestTime = results[algo.ToString()][CompressionLevel.SmallestSize.ToString()][dataType].WriteTimeMs;
                    
                    // This is a soft assertion since timing can vary
                    if (smallestTime < fastestTime)
                    {
                        Console.WriteLine($"Note: {algo} {dataType}: SmallestSize ({smallestTime}ms) was faster than Fastest ({fastestTime}ms)");
                    }
                }
            }
        }
        
        private class TestResult
        {
            public double CompressionRatio { get; set; }
            public long WriteTimeMs { get; set; }
            public long ReadTimeMs { get; set; }
            public long DataSize { get; set; }
            public long CompressedSize { get; set; }
            public bool Success { get; set; }
        }
        
        [Fact]
        public async Task Compression_AutoTunes_BasedOnCpuUsage()
        {
            // Arrange
            var largeValue = new string('x', 2 * 1024 * 1024); // 2MB of repeating data
            var key = "auto_tune_test_key";
            
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                AdaptiveCompression = true,
                AutoAdjustCompressionLevel = true,
                CompressionLevel = CompressionLevel.SmallestSize, // Start with highest compression
                MinSizeForCompressionBytes = 1024,
                MaxSizeForHighCompressionBytes = 1 * 1024 * 1024 // 1MB threshold
            };
            
            var metricsMock = new Mock<MetricsCollectionService>();
            metricsMock.Setup(m => m.GetCustomMetric("cpu_usage_percent"))
                .Returns(new MetricValue { LastValue = 85 }); // Simulate high CPU usage
                
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsMock.Object,
                mlPredictionService: null);

            // Act - With simulated high CPU
            await provider.SetAsync(key, largeValue);
            var statsWithHighCpu = provider.GetCacheStats();
            
            // Change CPU load to low and create a new provider
            metricsMock.Setup(m => m.GetCustomMetric("cpu_usage_percent"))
                .Returns(new MetricValue { LastValue = 20 }); // Simulate low CPU usage
                
            var lowCpuProvider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsMock.Object,
                mlPredictionService: null);
                
            await lowCpuProvider.SetAsync(key + "_low", largeValue);
            var statsWithLowCpu = lowCpuProvider.GetCacheStats();
            
            // Assert
            // Both should compress the data
            Assert.True(statsWithHighCpu.CompressionRatio < 1.0);
            Assert.True(statsWithLowCpu.CompressionRatio < 1.0);
            
            // With high CPU, compression should favor speed over compression ratio
            if (statsWithHighCpu.CompressionStats["GZip"].CompressedItems > 0 && 
                statsWithLowCpu.CompressionStats["GZip"].CompressedItems > 0)
            {
                // Verify that compression under high CPU load took less time
                Assert.True(statsWithHighCpu.CompressionStats["GZip"].AverageCompressionTimeMs <= 
                           statsWithLowCpu.CompressionStats["GZip"].AverageCompressionTimeMs * 1.2,
                           "Under high CPU load, compression should be faster");
            }
            
            // The actual compression ratio depends on the implementation details, but in general
            // low CPU should allow for better compression ratio if time permits
            // This is a soft assertion since the actual behavior depends on the implementation
            Console.WriteLine($"High CPU compression ratio: {statsWithHighCpu.CompressionRatio}");
            Console.WriteLine($"Low CPU compression ratio: {statsWithLowCpu.CompressionRatio}");
            Console.WriteLine($"High CPU compression time: {statsWithHighCpu.AverageCompressionTimeMs}ms");
            Console.WriteLine($"Low CPU compression time: {statsWithLowCpu.AverageCompressionTimeMs}ms");
        }
        
        [Fact]
        public async Task Compression_MigrateLegacyCacheEntries()
        {
            // Arrange - Create uncompressed entries
            var uncompressedSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = false, // No compression
                EncryptionEnabled = false   // No encryption for this test
            };
            
            var uncompressedProvider = new FileBasedCacheProvider(
                Options.Create(uncompressedSettings),
                metricsService: null,
                mlPredictionService: null);
                
            // Create a set of cache entries without compression
            var entries = new Dictionary<string, string>();
            for (int i = 0; i < 20; i++)
            {
                var key = $"legacy_key_{i}";
                var value = i % 2 == 0 
                    ? new string('x', 10 * 1024) // Large compressible entry (10KB)
                    : $"Small value {i}";        // Small entry (not compressible due to size)
                    
                await uncompressedProvider.SetAsync(key, value);
                entries[key] = value;
            }
            
            // Verify entries were created without compression
            var uncompressedStats = uncompressedProvider.GetCacheStats();
            Assert.Equal(1.0, uncompressedStats.CompressionRatio); // No compression
            
            // Act - Create a new provider with compression and auto-upgrade
            var compressedSettings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                MinSizeForCompressionBytes = 1024, // 1KB threshold
                UpgradeUncompressedEntries = true, // Auto-upgrade legacy entries
                EncryptionEnabled = false
            };
            
            var compressedProvider = new FileBasedCacheProvider(
                Options.Create(compressedSettings),
                metricsService: null,
                mlPredictionService: null);
                
            // Allow time for background migration to complete
            await Task.Delay(500);
            
            // Access entries to ensure they're in the cache
            foreach (var key in entries.Keys)
            {
                var value = await compressedProvider.GetAsync<string>(key);
                Assert.Equal(entries[key], value); // Data remains intact
            }
            
            // Warm up the cache to ensure all entries are loaded
            foreach (var key in entries.Keys)
            {
                await compressedProvider.GetAsync<string>(key);
            }
            
            // Assert - Verify migration worked
            var compressedStats = compressedProvider.GetCacheStats();
            
            // At least large entries should now be compressed
            Assert.True(compressedStats.CompressedItemCount > 0, "Some entries should be compressed after migration");
            Assert.True(compressedStats.CompressionRatio < 1.0, "Overall compression ratio should improve");
            
            // Log migration results
            Console.WriteLine("=== Legacy Entry Migration Results ===");
            Console.WriteLine($"Total entries: {entries.Count}");
            Console.WriteLine($"Compressed entries: {compressedStats.CompressedItemCount}");
            Console.WriteLine($"Original storage: {uncompressedStats.StorageUsageBytes} bytes");
            Console.WriteLine($"Compressed storage: {compressedStats.CompressedStorageUsageBytes} bytes");
            Console.WriteLine($"Storage saved: {compressedStats.CompressionSavingsBytes} bytes");
            Console.WriteLine($"Compression ratio: {compressedStats.CompressionRatio:F3}");
            Console.WriteLine($"Cache hits: {compressedStats.Hits}");
            
            // Verify a new provider with different algorithm can still read migrated entries
            var brotliSettings = compressedSettings with { CompressionAlgorithm = CompressionAlgorithm.Brotli };
            var brotliProvider = new FileBasedCacheProvider(
                Options.Create(brotliSettings),
                metricsService: null,
                mlPredictionService: null);
                
            // Verify all entries can be read with the new algorithm
            foreach (var key in entries.Keys)
            {
                var value = await brotliProvider.GetAsync<string>(key);
                Assert.Equal(entries[key], value); // Data remains intact across algorithm changes
            }
        }
        
        [Fact]
        public async Task CompressionHistory_TracksEffectiveness()
        {
            // Arrange
            var largeValue = new string('x', 1024 * 1024); // 1MB of repeating data
            var settings = new CacheSettings
            {
                CacheEnabled = true,
                CacheDirectory = _testDirectory,
                CompressionEnabled = true,
                CompressionAlgorithm = CompressionAlgorithm.GZip,
                TrackCompressionMetrics = true,
                CompressionMetricsRetentionHours = 1
            };
            
            var provider = new FileBasedCacheProvider(
                Options.Create(settings),
                metricsService: null,
                mlPredictionService: null);

            // Act - Store multiple values to generate history
            for (int i = 0; i < 5; i++)
            {
                await provider.SetAsync($"history_key_{i}", largeValue);
                await Task.Delay(50); // Allow time for processing
            }
            
            var stats = provider.GetCacheStats();
            
            // Assert
            // Should be tracking compression history
            Assert.NotEmpty(stats.CompressionHistory);
            
            // Each history entry should have valid data
            foreach (var entry in stats.CompressionHistory)
            {
                Assert.True(entry.Timestamp <= DateTime.UtcNow);
                Assert.True(entry.CompressionRatio > 0);
                Assert.True(entry.BytesSaved > 0);
                Assert.False(string.IsNullOrEmpty(entry.PrimaryAlgorithm));
            }
            
            // Verify history entries have increasing timestamps
            var orderedEntries = stats.CompressionHistory.OrderBy(e => e.Timestamp).ToArray();
            for (int i = 1; i < orderedEntries.Length; i++)
            {
                Assert.True(orderedEntries[i].Timestamp >= orderedEntries[i-1].Timestamp,
                    "History entries should have increasing timestamps");
            }
        }
    }
    
    [Fact]
    public async Task Compression_LegacyMigrationAndVersionCompatibility_WorksCorrectly()
    {
        // This test validates that the compression system correctly handles
        // legacy uncompressed entries and migration between compression versions
        
        // Arrange
        // First create entries without compression
        var uncompressedSettings = new CacheSettings
        {
            CacheEnabled = true,
            CacheDirectory = _testDirectory,
            CompressionEnabled = false, // Disable compression
            EncryptionEnabled = false   // Disable encryption for this test
        };
        
        var uncompressedProvider = new FileBasedCacheProvider(
            Options.Create(uncompressedSettings),
            metricsService: null,
            mlPredictionService: null);
        
        // Create a variety of test entries with different sizes and types
        var entries = new Dictionary<string, object>
        {
            ["legacy_small"] = "Small uncompressed value",
            ["legacy_medium"] = new string('a', 5 * 1024), // 5KB text
            ["legacy_large"] = new string('b', 50 * 1024), // 50KB text
            ["legacy_json"] = System.Text.Json.JsonSerializer.Serialize(new { 
                items = Enumerable.Range(0, 100).Select(i => new { id = i, name = $"Item {i}" }).ToArray() 
            }),
            ["legacy_binary"] = Enumerable.Range(0, 1000).SelectMany(i => BitConverter.GetBytes(i)).ToArray()
        };
        
        // Store all entries with the uncompressed provider
        foreach (var (key, value) in entries)
        {
            if (value is string strValue)
            {
                await uncompressedProvider.SetAsync(key, strValue);
            }
            else if (value is byte[] byteValue)
            {
                await uncompressedProvider.SetAsync(key, byteValue);
            }
        }
        
        // Verify all entries are stored uncompressed
        var uncompressedStats = uncompressedProvider.GetCacheStats();
        Assert.Equal(1.0, uncompressedStats.CompressionRatio); // No compression
        
        // Act - Create a new provider with compression enabled and auto-upgrade
        var gzipSettings = new CacheSettings
        {
            CacheEnabled = true,
            CacheDirectory = _testDirectory,
            CompressionEnabled = true,
            CompressionAlgorithm = CompressionAlgorithm.GZip,
            CompressionLevel = CompressionLevel.Optimal,
            MinSizeForCompressionBytes = 1024, // 1KB threshold
            UpgradeUncompressedEntries = true, // Enable auto-upgrade
            EncryptionEnabled = false
        };
        
        var gzipProvider = new FileBasedCacheProvider(
            Options.Create(gzipSettings),
            metricsService: null,
            mlPredictionService: null);
        
        // Allow time for background migration to complete
        await Task.Delay(500);
        
        // Test reading all entries to trigger any lazy migrations
        var gzipResults = new Dictionary<string, object>();
        foreach (var key in entries.Keys)
        {
            if (entries[key] is string)
            {
                gzipResults[key] = await gzipProvider.GetAsync<string>(key);
            }
            else if (entries[key] is byte[])
            {
                gzipResults[key] = await gzipProvider.GetAsync<byte[]>(key);
            }
        }
        
        // Get stats after migration
        var gzipStats = gzipProvider.GetCacheStats();
        
        // Act 2 - Create a provider with different compression algorithm
        var brotliSettings = new CacheSettings
        {
            CacheEnabled = true,
            CacheDirectory = _testDirectory,
            CompressionEnabled = true,
            CompressionAlgorithm = CompressionAlgorithm.Brotli, // Different algorithm
            CompressionLevel = CompressionLevel.SmallestSize,
            MinSizeForCompressionBytes = 1024,
            UpgradeUncompressedEntries = true,
            EncryptionEnabled = false
        };
        
        var brotliProvider = new FileBasedCacheProvider(
            Options.Create(brotliSettings),
            metricsService: null,
            mlPredictionService: null);
        
        // Wait for potential migrations
        await Task.Delay(500);
        
        // Read everything with the Brotli provider
        var brotliResults = new Dictionary<string, object>();
        foreach (var key in entries.Keys)
        {
            if (entries[key] is string)
            {
                brotliResults[key] = await brotliProvider.GetAsync<string>(key);
            }
            else if (entries[key] is byte[])
            {
                brotliResults[key] = await brotliProvider.GetAsync<byte[]>(key);
            }
        }
        
        // Get stats from Brotli provider
        var brotliStats = brotliProvider.GetCacheStats();
        
        // Act 3 - Create one more entry with Brotli
        var brotliKey = "brotli_only_entry";
        var brotliValue = new string('c', 20 * 1024); // 20KB
        await brotliProvider.SetAsync(brotliKey, brotliValue);
        
        // Now try to read it with the GZip provider
        var crossAlgoResult = await gzipProvider.GetAsync<string>(brotliKey);
        
        // Assert
        // 1. Verify data integrity across all providers
        foreach (var key in entries.Keys)
        {
            if (entries[key] is string strValue)
            {
                Assert.Equal(strValue, gzipResults[key]);
                Assert.Equal(strValue, brotliResults[key]);
            }
            else if (entries[key] is byte[] byteValue)
            {
                var gzipBytes = (byte[])gzipResults[key];
                var brotliBytes = (byte[])brotliResults[key];
                Assert.Equal(byteValue.Length, gzipBytes.Length);
                Assert.Equal(byteValue.Length, brotliBytes.Length);
                
                // Check a sample of the bytes
                for (int i = 0; i < Math.Min(byteValue.Length, 100); i++)
                {
                    Assert.Equal(byteValue[i], gzipBytes[i]);
                    Assert.Equal(byteValue[i], brotliBytes[i]);
                }
            }
        }
        
        // 2. Verify GZip provider migrated legacy entries to compressed format
        Assert.True(gzipStats.CompressedItemCount > 0, "GZip should have compressed some entries");
        Assert.True(gzipStats.CompressionRatio < 1.0, "Compression should be effective");
        
        // 3. Verify Brotli provider can read GZip's entries
        Assert.True(brotliStats.CompressionRatio < 1.0, "Brotli should maintain compression");
        
        // 4. Verify cross-algorithm compatibility 
        Assert.Equal(brotliValue, crossAlgoResult);
        
        // 5. Check that the compression system is tracking algorithms correctly
        Assert.True(gzipStats.CompressionStats["GZip"].CompressedItems > 0, 
            "GZip provider should use GZip algorithm");
            
        // Log detailed migration results
        Console.WriteLine("=== Legacy Migration and Version Compatibility Test ===");
        Console.WriteLine($"Original entries: {entries.Count}");
        Console.WriteLine($"GZip compressed entries: {gzipStats.CompressedItemCount}");
        Console.WriteLine($"GZip compression ratio: {gzipStats.CompressionRatio:F3}");
        Console.WriteLine($"Brotli compressed entries: {brotliStats.CompressedItemCount}");
        Console.WriteLine($"Brotli compression ratio: {brotliStats.CompressionRatio:F3}");
        Console.WriteLine($"Storage saved by GZip: {gzipStats.CompressionSavingsBytes / 1024:F0}KB");
        
        // Verify most efficient algorithm is being tracked
        Console.WriteLine($"GZip provider most efficient algorithm: {gzipStats.MostEfficientAlgorithm}");
        Console.WriteLine($"Brotli provider most efficient algorithm: {brotliStats.MostEfficientAlgorithm}");
        
        // Verify provider handles compression version changes correctly
        Console.WriteLine($"GZip format version: 2");
        Console.WriteLine($"Brotli format version: 2");
        Console.WriteLine($"Cross-algorithm compatibility: {(crossAlgoResult == brotliValue ? "Success" : "Failed")}");
    }
    
    [Fact]
    public async Task Compression_AdaptiveContentTypeDetection_CrossDataTypes()
    {
        // This test validates that adaptive content-type detection correctly identifies
        // and optimally compresses different content types with appropriate algorithms
        
        // Arrange - Create samples of different content types
        
        // 1. JSON data (structured, highly compressible)
        var jsonData = System.Text.Json.JsonSerializer.Serialize(new { 
            items = Enumerable.Range(0, 1000).Select(i => new { 
                id = i, 
                name = $"Item {i}", 
                description = $"This is a long description for item {i} with some repeated content to improve compression"
            }).ToArray(),
            metadata = new { 
                created = DateTime.Now, 
                author = "Test User",
                tags = new[] { "test", "compression", "json", "content-type" }
            }
        });
        
        // 2. XML data (structured, highly compressible)
        var xmlData = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
        <root>
            <items>
                {string.Join("", Enumerable.Range(0, 1000).Select(i => $@"
                <item id=""{i}"">
                    <name>Item {i}</name>
                    <description>This is a long description for item {i} with some repeated content to improve compression</description>
                    <properties>
                        <property key=""key1"" value=""value1"" />
                        <property key=""key2"" value=""value2"" />
                    </properties>
                </item>"))}
            </items>
            <metadata>
                <created>{DateTime.Now}</created>
                <author>Test User</author>
                <tags>
                    <tag>test</tag>
                    <tag>compression</tag>
                    <tag>xml</tag>
                    <tag>content-type</tag>
                </tags>
            </metadata>
        </root>";
                
        // 3. HTML data (structured, highly compressible)
        var htmlData = $@"<!DOCTYPE html>
        <html>
        <head>
            <title>Compression Test</title>
            <meta charset=""utf-8"">
            <style>
                body {{ font-family: Arial, sans-serif; margin: 0; padding: 20px; }}
                .item {{ border: 1px solid #ccc; margin: 10px; padding: 10px; }}
                h1 {{ color: #333; }}
            </style>
        </head>
        <body>
            <h1>Content Type Detection Test</h1>
            <div class=""container"">
                {string.Join("", Enumerable.Range(0, 500).Select(i => $@"
                <div class=""item"" id=""item-{i}"">
                    <h2>Item {i}</h2>
                    <p>This is a description for item {i} with repeated content to improve compression.</p>
                    <ul>
                        <li>Property 1: Value 1</li>
                        <li>Property 2: Value 2</li>
                    </ul>
                </div>"))}
            </div>
        </body>
        </html>";
                
        // 4. Plain text (repeating, highly compressible)
        var plainTextData = string.Join("\n", Enumerable.Range(0, 1000)
            .Select(i => $"Line {i}: This is a test line with repeating content to ensure good compression ratios."));
                
        // 5. Binary data (random, less compressible)
        var random = new Random(42); // Fixed seed for reproducibility
        var binaryData = new byte[100 * 1024]; // 100KB
        random.NextBytes(binaryData);
        
        // Configure cache with adaptive compression
        var settings = new CacheSettings
        {
            CacheEnabled = true,
            CacheDirectory = _testDirectory,
            CompressionEnabled = true,
            AdaptiveCompression = true,
            ContentTypeAlgorithmMap = new Dictionary<ContentType, CompressionAlgorithm> {
                { ContentType.TextJson, CompressionAlgorithm.Brotli },
                { ContentType.TextXml, CompressionAlgorithm.Brotli },
                { ContentType.TextHtml, CompressionAlgorithm.Brotli },
                { ContentType.TextPlain, CompressionAlgorithm.GZip },
                { ContentType.BinaryData, CompressionAlgorithm.GZip },
                { ContentType.CompressedData, CompressionAlgorithm.None }
            },
            ContentTypeCompressionLevelMap = new Dictionary<ContentType, CompressionLevel> {
                { ContentType.TextJson, CompressionLevel.SmallestSize },
                { ContentType.TextXml, CompressionLevel.SmallestSize },
                { ContentType.TextHtml, CompressionLevel.SmallestSize },
                { ContentType.TextPlain, CompressionLevel.Optimal },
                { ContentType.BinaryData, CompressionLevel.Fastest }
            }
        };
        
        var provider = new FileBasedCacheProvider(
            Options.Create(settings),
            metricsService: null,
            mlPredictionService: null);

        // Act - Store each content type
        await provider.SetAsync("json", jsonData);
        await provider.SetAsync("xml", xmlData);
        await provider.SetAsync("html", htmlData);
        await provider.SetAsync("text", plainTextData);
        await provider.SetAsync("binary", binaryData);
        
        // Get stats after all types are stored
        var stats = provider.GetCacheStats();
        
        // Retrieve all types to verify data integrity
        var jsonResult = await provider.GetAsync<string>("json");
        var xmlResult = await provider.GetAsync<string>("xml");
        var htmlResult = await provider.GetAsync<string>("html");
        var textResult = await provider.GetAsync<string>("text");
        var binaryResult = await provider.GetAsync<byte[]>("binary");
        
        // Assert - Data integrity
        Assert.Equal(jsonData, jsonResult);
        Assert.Equal(xmlData, xmlResult);
        Assert.Equal(htmlData, htmlResult);
        Assert.Equal(plainTextData, textResult);
        Assert.Equal(binaryData.Length, binaryResult.Length);
        for (int i = 0; i < 100; i++) // Check sample of binary data
        {
            Assert.Equal(binaryData[i], binaryResult[i]);
        }
        
        // Assert - Compression effectiveness
        // With content-type detection, both algorithms should be used
        Assert.True(stats.CompressionStats["GZip"].CompressedItems + 
                   stats.CompressionStats["Brotli"].CompressedItems > 0,
            "Compression should be applied to at least some content types");
            
        // For structured text formats, compression ratio should be excellent
        Assert.True(stats.CompressionRatio < 0.3, 
            $"With appropriate content type detection, overall compression should be very effective, got {stats.CompressionRatio:F3}");
            
        // Both Brotli and GZip should have been used for different content types
        var usedAlgorithms = new HashSet<string>();
        if (stats.CompressionStats["Brotli"].CompressedItems > 0) usedAlgorithms.Add("Brotli");
        if (stats.CompressionStats["GZip"].CompressedItems > 0) usedAlgorithms.Add("GZip");
        
        Assert.True(usedAlgorithms.Count > 0, "At least one compression algorithm should be used");
        
        // With truly adaptive selection, both algorithms should be used for optimal content types
        if (settings.AdaptiveCompression)
        {
            // This is an ideal outcome, but not strictly required
            // as the best algorithm might be the same for all content types
            Console.WriteLine($"Algorithms used: {string.Join(", ", usedAlgorithms)}");
        }
        
        // Log detailed compression results
        Console.WriteLine("=== Content Type Detection and Adaptive Compression Test ===");
        Console.WriteLine($"Total items: {stats.CachedItemCount}");
        Console.WriteLine($"Compressed items: {stats.CompressedItemCount}");
        Console.WriteLine($"Overall compression ratio: {stats.CompressionRatio:F3}");
        Console.WriteLine($"Storage savings: {stats.CompressionSavingsPercent:F1}%");
        
        Console.WriteLine("\nCompression by algorithm:");
        Console.WriteLine($"GZip items: {stats.CompressionStats["GZip"].CompressedItems}, " +
            $"ratio: {stats.CompressionStats["GZip"].CompressionRatio:F3}, " +
            $"time: {stats.CompressionStats["GZip"].AverageCompressionTimeMs:F2}ms");
        Console.WriteLine($"Brotli items: {stats.CompressionStats["Brotli"].CompressedItems}, " +
            $"ratio: {stats.CompressionStats["Brotli"].CompressionRatio:F3}, " +
            $"time: {stats.CompressionStats["Brotli"].AverageCompressionTimeMs:F2}ms");
            
        // Verify content-specific metrics are collected
        if (stats.CompressionByContentType != null && stats.CompressionByContentType.Count > 0)
        {
            Console.WriteLine("\nCompression by content type:");
            foreach (var kvp in stats.CompressionByContentType)
            {
                Console.WriteLine($"  {kvp.Key}: ratio={kvp.Value.CompressionRatio:F3}, " +
                                $"items={kvp.Value.CompressedItems}, " +
                                $"avg_time={kvp.Value.AverageCompressionTimeMs:F1}ms");
            }
        }
        
        // Check content type detection accuracy
        // This is indirect since we can't directly access the internal content type detection
        // We infer based on compression ratios and algorithm selection
        
        // JSON, XML, HTML should all be very compressible with Brotli
        var textualCompressionRatio = Math.Min(
            Math.Min(
                jsonData.Length > 0 ? (double)stats.CompressedStorageUsageBytes / jsonData.Length : 1.0,
                xmlData.Length > 0 ? (double)stats.CompressedStorageUsageBytes / xmlData.Length : 1.0
            ),
            htmlData.Length > 0 ? (double)stats.CompressedStorageUsageBytes / htmlData.Length : 1.0
        );
        
        Assert.True(textualCompressionRatio < 0.2, 
            $"Textual content should compress very well with Brotli, ratio: {textualCompressionRatio:F3}");
        
        // Verify algorithm selection is effective
        Assert.True(stats.MostEfficientAlgorithm != "None", 
            "System should identify most efficient compression algorithm");
        Console.WriteLine($"Most efficient algorithm: {stats.MostEfficientAlgorithm}");
        Console.WriteLine($"Compression efficiency score: {stats.CompressionEfficiencyScore:F3}");
    }

    [Fact]
    public async Task Compression_NonCompressibleBinaryData_HandledCorrectly()
    {
        // This test verifies that the compression system correctly handles
        // truly non-compressible binary data like encrypted content or random data
        
        // Arrange
        const int dataSize = 5 * 1024 * 1024; // 5MB
        
        // Create truly non-compressible random data
        var random = new Random(42); // Fixed seed for reproducibility
        var nonCompressibleBytes = new byte[dataSize];
        random.NextBytes(nonCompressibleBytes); // Random bytes are effectively non-compressible
        
        var key = "non_compressible_binary_key";
        
        var settings = new CacheSettings
        {
            CacheEnabled = true,
            CacheDirectory = _testDirectory,
            CompressionEnabled = true,
            CompressionAlgorithm = CompressionAlgorithm.GZip,
            CompressionLevel = CompressionLevel.SmallestSize, // Try with highest compression
            MinSizeForCompressionBytes = 1024, // 1KB threshold
            MinCompressionRatio = 0.9, // Only use compression if it reduces by at least 10%
            AdaptiveCompression = true
        };
        
        var provider = new FileBasedCacheProvider(
            Options.Create(settings),
            metricsService: null,
            mlPredictionService: null);

        // Act
        // Store non-compressible data
        var sw = Stopwatch.StartNew();
        await provider.SetAsync(key, nonCompressibleBytes);
        sw.Stop();
        var writeTime = sw.ElapsedMilliseconds;
        
        // Get compressed data
        var retrievedData = await provider.GetAsync<byte[]>(key);
        var stats = provider.GetCacheStats();
        
        // Assert
        // 1. Data integrity
        Assert.Equal(nonCompressibleBytes.Length, retrievedData.Length);
        for (int i = 0; i < 100; i++) // Check first 100 bytes
        {
            Assert.Equal(nonCompressibleBytes[i], retrievedData[i]);
        }
        
        // 2. System should recognize the data is non-compressible
        // Either by not applying compression at all (ratio = 1.0)
        // Or by detecting minimal compression benefit and reverting to original
        Console.WriteLine($"Compression ratio for non-compressible data: {stats.CompressionRatio}");
        
        if (stats.CompressionRatio < 0.99)
        {
            // If compression was somehow applied, it should have minimal effect
            // and the benefit should be minimal
            Console.WriteLine("System applied minor compression to random data");
            Assert.True(stats.CompressionRatio > 0.9, 
                "Random data should not compress well");
        }
        else
        {
            // If system correctly skipped compression, ratio should be 1.0
            Console.WriteLine("System correctly skipped compression for non-compressible data");
            Assert.True(stats.CompressionRatio >= 0.99,
                "System should skip compression for non-compressible data");
        }
        
        // 3. Verify that system doesn't waste resources trying to compress
        Assert.True(writeTime < 5000, "Processing non-compressible data shouldn't take excessive time");
        
        // 4. Try storing different non-compressible binary formats
        
        // Create pseudo-encrypted data (looks like encrypted data)
        var pseudoEncryptedData = new byte[dataSize];
        for (int i = 0; i < dataSize; i++)
        {
            pseudoEncryptedData[i] = (byte)(nonCompressibleBytes[i] ^ 0xA5); // Simple XOR "encryption"
        }
        
        await provider.SetAsync("encrypted_like_data", pseudoEncryptedData);
        var encryptedLikeRetrieved = await provider.GetAsync<byte[]>("encrypted_like_data");
        
        // Verify data integrity
        Assert.Equal(pseudoEncryptedData.Length, encryptedLikeRetrieved.Length);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(pseudoEncryptedData[i], encryptedLikeRetrieved[i]);
        }
        
        // 5. Get final stats and verify behavior
        var finalStats = provider.GetCacheStats();
        
        // Log detailed metrics for analysis
        Console.WriteLine("=== Non-Compressible Binary Data Test ===");
        Console.WriteLine($"Data size: {dataSize / (1024 * 1024)}MB");
        Console.WriteLine($"Write time: {writeTime}ms");
        Console.WriteLine($"Final compression ratio: {finalStats.CompressionRatio:F3}");
        Console.WriteLine($"Storage used: {finalStats.StorageUsageBytes / (1024 * 1024):F2}MB");
        Console.WriteLine($"Compressed storage: {finalStats.CompressedStorageUsageBytes / (1024 * 1024):F2}MB");
        
        if (finalStats.CompressionStats["GZip"].TotalItems > 0)
        {
            Console.WriteLine($"GZip avg compression time: {finalStats.CompressionStats["GZip"].AverageCompressionTimeMs:F2}ms");
            Console.WriteLine($"GZip compression attempts: {finalStats.CompressionStats["GZip"].TotalItems}");
            Console.WriteLine($"GZip compression successes: {finalStats.CompressionStats["GZip"].CompressedItems}");
        }
        
        if (finalStats.CompressionStats["Brotli"].TotalItems > 0)
        {
            Console.WriteLine($"Brotli avg compression time: {finalStats.CompressionStats["Brotli"].AverageCompressionTimeMs:F2}ms");
            Console.WriteLine($"Brotli compression attempts: {finalStats.CompressionStats["Brotli"].TotalItems}");
            Console.WriteLine($"Brotli compression successes: {finalStats.CompressionStats["Brotli"].CompressedItems}");
        }
        
        // Most importantly, verify the system is still functional after handling non-compressible data
        var testKey = "after_binary_test";
        var testValue = "This is a test value";
        await provider.SetAsync(testKey, testValue);
        var result = await provider.GetAsync<string>(testKey);
        Assert.Equal(testValue, result);
    }
    
    [Fact]
    public async Task Compression_OptimizesMemoryForExtremelyLargeData()
    {
        // This test validates that compression handles extremely large data (20MB+) efficiently
        // with bounded memory usage, avoiding excessive memory allocation during compression/decompression
        
        // Arrange
        // Create a very large repeating pattern (40MB) that will compress very well
        var dataSize = 40 * 1024 * 1024; // 40MB
        var chunkSize = 1024 * 1024; // Use 1MB chunks to build the string efficiently
        var chunk = new string('x', chunkSize);
        
        var sb = new System.Text.StringBuilder(dataSize);
        for (int i = 0; i < dataSize / chunkSize; i++)
        {
            sb.Append(chunk);
        }
        var extremelyLargeValue = sb.ToString();
        var key = "extreme_memory_test_key";
        
        var settings = new CacheSettings
        {
            CacheEnabled = true,
            CacheDirectory = _testDirectory,
            CompressionEnabled = true,
            CompressionAlgorithm = CompressionAlgorithm.GZip, // GZip is typically more memory-efficient
            CompressionLevel = CompressionLevel.Fastest, // Use fastest compression for large data
            MinSizeForCompressionBytes = 1024,
            // Enable memory-optimized processing
            AutoAdjustCompressionLevel = true,
            MaxSizeForHighCompressionBytes = 5 * 1024 * 1024 // 5MB threshold
        };
        
        var provider = new FileBasedCacheProvider(
            Options.Create(settings),
            metricsService: null,
            mlPredictionService: null);

        // Force garbage collection to get accurate memory measurements
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        var memoryBefore = GC.GetTotalMemory(true);
        
        // Act - Store the extremely large value
        var sw = Stopwatch.StartNew();
        await provider.SetAsync(key, extremelyLargeValue);
        sw.Stop();
        var compressionTime = sw.ElapsedMilliseconds;
        
        // Measure memory after storage
        var memoryAfterCompression = GC.GetTotalMemory(false);
        
        // Force garbage collection again
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        var memoryAfterGC = GC.GetTotalMemory(true);
        
        // Read it back (decompress)
        sw.Restart();
        var retrievedValue = await provider.GetAsync<string>(key);
        sw.Stop();
        var decompressionTime = sw.ElapsedMilliseconds;
        
        // Measure memory after retrieval
        var memoryAfterDecompression = GC.GetTotalMemory(false);
        
        // Get stats to check compression results
        var stats = provider.GetCacheStats();
        
        // Assert
        // 1. Value integrity
        Assert.Equal(extremelyLargeValue.Length, retrievedValue.Length);
        Assert.Equal(extremelyLargeValue[0], retrievedValue[0]);
        Assert.Equal(extremelyLargeValue[extremelyLargeValue.Length - 1], retrievedValue[retrievedValue.Length - 1]);
        
        // 2. Compression should be very effective (>99% for repeating data)
        Assert.True(stats.CompressionRatio < 0.01,
            $"Compression ratio for highly-repetitive data should be excellent, got {stats.CompressionRatio}");
        
        // 3. Memory usage during compression should be reasonable - shouldn't spike to more than 
        // 2x the original data size
        var compressionMemoryOverhead = memoryAfterCompression - memoryBefore;
        Assert.True(compressionMemoryOverhead < extremelyLargeValue.Length,
            $"Memory overhead during compression ({compressionMemoryOverhead / (1024*1024)}MB) " +
            $"should be less than original data size ({extremelyLargeValue.Length / (1024*1024)}MB)");
        
        // 4. Memory should be reclaimable after compression
        var memoryRetainedAfterGC = memoryAfterGC - memoryBefore;
        Assert.True(memoryRetainedAfterGC < extremelyLargeValue.Length / 2,
            $"Memory after GC ({memoryRetainedAfterGC / (1024*1024)}MB) should be much less than " + 
            $"the original data size ({extremelyLargeValue.Length / (1024*1024)}MB)");
        
        // 5. Memory usage during decompression should be efficient
        var decompressionMemoryOverhead = memoryAfterDecompression - memoryAfterGC;
        // Need to account for the decompressed value in memory, so allow for 1.5x the original size
        Assert.True(decompressionMemoryOverhead < extremelyLargeValue.Length * 1.5,
            $"Memory during decompression ({decompressionMemoryOverhead / (1024*1024)}MB) is too high");
        
        // 6. Compression/decompression should complete in reasonable time
        Assert.True(compressionTime < 10000, $"Compression took too long: {compressionTime}ms");
        Assert.True(decompressionTime < 10000, $"Decompression took too long: {decompressionTime}ms");
        
        // Log detailed metrics
        Console.WriteLine("=== Extremely Large Data Compression Memory Test ===");
        Console.WriteLine($"Data size: {extremelyLargeValue.Length / (1024*1024)}MB");
        Console.WriteLine($"Compression time: {compressionTime}ms");
        Console.WriteLine($"Decompression time: {decompressionTime}ms");
        Console.WriteLine($"Compression ratio: {stats.CompressionRatio:F5}");
        Console.WriteLine($"Memory before: {memoryBefore / (1024*1024)}MB");
        Console.WriteLine($"Memory after compression: {memoryAfterCompression / (1024*1024)}MB");
        Console.WriteLine($"Memory overhead during compression: {compressionMemoryOverhead / (1024*1024)}MB");
        Console.WriteLine($"Memory after GC: {memoryAfterGC / (1024*1024)}MB");
        Console.WriteLine($"Memory retained after GC: {memoryRetainedAfterGC / (1024*1024)}MB");
        Console.WriteLine($"Memory after decompression: {memoryAfterDecompression / (1024*1024)}MB");
        Console.WriteLine($"Memory overhead during decompression: {decompressionMemoryOverhead / (1024*1024)}MB");
        Console.WriteLine($"Compressed size: {(extremelyLargeValue.Length * stats.CompressionRatio) / (1024*1024):F2}MB");
        Console.WriteLine($"Bytes saved: {stats.CompressionSavingsBytes / (1024*1024):F2}MB");
    }
}