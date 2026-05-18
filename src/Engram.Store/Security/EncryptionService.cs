using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Security;

/// <summary>
/// AES-256-GCM encryption for data at rest.
/// Each file encrypted with a unique nonce. Key derived from master password.
/// </summary>
public class EncryptionService : IDisposable
{
    private readonly ILogger<EncryptionService>? _logger;
    private byte[]? _key;
    private bool _disposed;

    private const int KeySize = 32; // 256 bits
    private const int NonceSize = 12; // 96 bits for GCM
    private const int TagSize = 16; // 128-bit auth tag

    public bool IsInitialized => _key != null;

    public EncryptionService(ILogger<EncryptionService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initialize with a password. Derives a 256-bit key using PBKDF2.
    /// </summary>
    public void Initialize(string password, string salt)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));
        if (string.IsNullOrEmpty(salt)) throw new ArgumentNullException(nameof(salt));

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 100_000, HashAlgorithmName.SHA256);
        _key = pbkdf2.GetBytes(KeySize);

        _logger?.LogInformation("Encryption initialized (PBKDF2, 100k iterations)");
    }

    /// <summary>
    /// Initialize with a raw key (for sync scenarios).
    /// </summary>
    public void InitializeWithKey(byte[] key)
    {
        if (key == null || key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes", nameof(key));
        _key = (byte[])key.Clone();
    }

    /// <summary>
    /// Encrypt plaintext bytes. Returns nonce + tag + ciphertext.
    /// </summary>
    public byte[] Encrypt(byte[] plaintext)
    {
        EnsureInitialized();

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: [nonce (12)] [tag (16)] [ciphertext]
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Decrypt ciphertext (nonce + tag + ciphertext format).
    /// </summary>
    public byte[] Decrypt(byte[] encrypted)
    {
        EnsureInitialized();

        if (encrypted.Length < NonceSize + TagSize)
            throw new ArgumentException("Encrypted data too short");

        var nonce = encrypted[..NonceSize];
        var tag = encrypted[NonceSize..(NonceSize + TagSize)];
        var ciphertext = encrypted[(NonceSize + TagSize)..];

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>Encrypt a string.</summary>
    public string EncryptString(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = Encrypt(bytes);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>Decrypt a string.</summary>
    public string DecryptString(string encryptedBase64)
    {
        var encrypted = Convert.FromBase64String(encryptedBase64);
        var bytes = Decrypt(encrypted);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Encrypt a file in place.</summary>
    public void EncryptFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);
        var plaintext = File.ReadAllBytes(path);
        var encrypted = Encrypt(plaintext);
        File.WriteAllBytes(path + ".enc", encrypted);
        File.Delete(path);
        _logger?.LogDebug("Encrypted: {Path}", path);
    }

    /// <summary>Decrypt a .enc file in place.</summary>
    public void DecryptFile(string encPath)
    {
        if (!File.Exists(encPath)) throw new FileNotFoundException("File not found", encPath);
        var encrypted = File.ReadAllBytes(encPath);
        var plaintext = Decrypt(encrypted);
        var originalPath = encPath.EndsWith(".enc") ? encPath[..^4] : encPath;
        File.WriteAllBytes(originalPath, plaintext);
        File.Delete(encPath);
        _logger?.LogDebug("Decrypted: {Path}", encPath);
    }

    /// <summary>Generate a random salt for key derivation.</summary>
    public static string GenerateSalt()
    {
        var salt = new byte[32];
        RandomNumberGenerator.Fill(salt);
        return Convert.ToBase64String(salt);
    }

    /// <summary>Generate a random encryption key.</summary>
    public static byte[] GenerateKey()
    {
        var key = new byte[KeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    private void EnsureInitialized()
    {
        if (_key == null)
            throw new InvalidOperationException("Encryption not initialized. Call Initialize() first.");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_key != null)
            {
                Array.Clear(_key, 0, _key.Length);
                _key = null;
            }
            _disposed = true;
        }
    }
}
