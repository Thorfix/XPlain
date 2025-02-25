using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using XPlain.Configuration;

namespace XPlain.Services
{
    public class EncryptionProvider : IEncryptionProvider
    {
        private readonly Dictionary<string, byte[]> _keys = new Dictionary<string, byte[]>();
        private readonly Dictionary<string, DateTime> _keyRotationSchedule = new Dictionary<string, DateTime>();
        private readonly List<string> _activeKeyIds = new List<string>();
        
        public bool IsEnabled { get; private set; }
        public string CurrentKeyId { get; private set; }
        public DateTime CurrentKeyCreatedAt { get; private set; }
        public DateTime NextScheduledRotation { get; private set; }
        public bool AutoRotationEnabled { get; set; }

        public EncryptionProvider(IOptions<CacheSettings> settings = null)
        {
            // Default initialization with no encryption
            IsEnabled = settings?.Value?.EncryptionEnabled ?? false;
            CurrentKeyId = "default-key-1";
            CurrentKeyCreatedAt = DateTime.UtcNow;
            NextScheduledRotation = DateTime.UtcNow.AddDays(30);
            AutoRotationEnabled = false;
            
            // Generate a default key
            using var rng = RandomNumberGenerator.Create();
            var key = new byte[32]; // 256-bit key
            rng.GetBytes(key);
            _keys[CurrentKeyId] = key;
            
            _activeKeyIds.Add(CurrentKeyId);
            _keyRotationSchedule[CurrentKeyId] = NextScheduledRotation;
        }

        public IEnumerable<string> GetActiveKeyIds()
        {
            return _activeKeyIds;
        }

        public Dictionary<string, DateTime> GetKeyRotationSchedule()
        {
            return new Dictionary<string, DateTime>(_keyRotationSchedule);
        }
        
        public bool RotateKey()
        {
            try
            {
                // Generate a new key
                var newKeyId = $"key-{Guid.NewGuid():N}";
                using var rng = RandomNumberGenerator.Create();
                var newKey = new byte[32]; // 256-bit key
                rng.GetBytes(newKey);
                
                // Store the new key
                _keys[newKeyId] = newKey;
                
                // Update key metadata
                var oldKeyId = CurrentKeyId;
                CurrentKeyId = newKeyId;
                CurrentKeyCreatedAt = DateTime.UtcNow;
                NextScheduledRotation = DateTime.UtcNow.AddDays(30);
                
                // Keep the old key active for decryption
                _activeKeyIds.Add(newKeyId);
                _keyRotationSchedule[newKeyId] = NextScheduledRotation;
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        public byte[] Encrypt(byte[] data)
        {
            if (!IsEnabled || data == null || data.Length == 0)
                return data;
                
            try
            {
                using var aes = Aes.Create();
                aes.Key = _keys[CurrentKeyId];
                aes.GenerateIV();
                
                using var encryptor = aes.CreateEncryptor();
                var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
                
                // Combine the IV and encrypted data
                var result = new byte[aes.IV.Length + encrypted.Length + CurrentKeyId.Length + 1];
                aes.IV.CopyTo(result, 0);
                encrypted.CopyTo(result, aes.IV.Length);
                
                // Store the key ID after the encrypted data
                var keyIdBytes = Encoding.UTF8.GetBytes(CurrentKeyId);
                result[aes.IV.Length + encrypted.Length] = (byte)keyIdBytes.Length;
                keyIdBytes.CopyTo(result, aes.IV.Length + encrypted.Length + 1);
                
                return result;
            }
            catch
            {
                // If encryption fails, return the original data
                return data;
            }
        }
        
        public byte[] Decrypt(byte[] data)
        {
            if (!IsEnabled || data == null || data.Length < 17) // 16 bytes IV + at least 1 byte data
                return data;
                
            try
            {
                // Extract the IV
                var iv = new byte[16];
                Array.Copy(data, iv, 16);
                
                // Extract the key ID
                var keyIdLength = data[data.Length - 1];
                var keyIdBytes = new byte[keyIdLength];
                Array.Copy(data, data.Length - keyIdLength - 1, keyIdBytes, 0, keyIdLength);
                var keyId = Encoding.UTF8.GetString(keyIdBytes);
                
                // Check if we have this key
                if (!_keys.ContainsKey(keyId))
                    return data; // Can't decrypt
                    
                // Extract the encrypted data
                var encryptedLength = data.Length - iv.Length - keyIdLength - 1;
                var encrypted = new byte[encryptedLength];
                Array.Copy(data, iv.Length, encrypted, 0, encryptedLength);
                
                // Decrypt the data
                using var aes = Aes.Create();
                aes.Key = _keys[keyId];
                aes.IV = iv;
                
                using var decryptor = aes.CreateDecryptor();
                return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            }
            catch
            {
                // If decryption fails, return the original data
                return data;
            }
        }
    }
}