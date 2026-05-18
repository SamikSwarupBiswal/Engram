using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Security;

/// <summary>
/// Manages encryption keys and salts.
/// Stores key metadata in .engram/config/encryption.json.
/// NEVER stores the actual key — only salt and verification hash.
/// </summary>
public class KeyManager
{
    private readonly string _configDir;
    private readonly ILogger<KeyManager>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public KeyManager(string configDir, ILogger<KeyManager>? logger = null)
    {
        _configDir = configDir;
        _logger = logger;
    }

    /// <summary>Check if encryption is configured.</summary>
    public bool IsConfigured()
    {
        return File.Exists(GetConfigPath());
    }

    /// <summary>
    /// Set up encryption with a new password.
    /// Generates salt, derives key, stores verification hash.
    /// </summary>
    public EncryptionSetupResult Setup(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters", nameof(password));

        var salt = EncryptionService.GenerateSalt();
        var verificationHash = ComputeVerificationHash(password, salt);

        var config = new EncryptionConfig
        {
            Salt = salt,
            VerificationHash = verificationHash,
            CreatedAt = DateTimeOffset.UtcNow,
            Algorithm = "AES-256-GCM",
            KdfIterations = 100_000
        };

        SaveConfig(config);
        _logger?.LogInformation("Encryption configured");

        return new EncryptionSetupResult
        {
            Success = true,
            Salt = salt
        };
    }

    /// <summary>
    /// Unlock encryption with password. Returns initialized EncryptionService.
    /// </summary>
    public EncryptionService? Unlock(string password)
    {
        var config = LoadConfig();
        if (config == null) return null;

        var hash = ComputeVerificationHash(password, config.Salt);
        if (hash != config.VerificationHash)
        {
            _logger?.LogWarning("Wrong password");
            return null;
        }

        var service = new EncryptionService(_logger as ILogger<EncryptionService>);
        service.Initialize(password, config.Salt);
        _logger?.LogInformation("Encryption unlocked");
        return service;
    }

    /// <summary>Change encryption password.</summary>
    public bool ChangePassword(string oldPassword, string newPassword)
    {
        var config = LoadConfig();
        if (config == null) return false;

        var oldHash = ComputeVerificationHash(oldPassword, config.Salt);
        if (oldHash != config.VerificationHash) return false;

        var newSalt = EncryptionService.GenerateSalt();
        var newHash = ComputeVerificationHash(newPassword, newSalt);

        config.Salt = newSalt;
        config.VerificationHash = newHash;
        config.CreatedAt = DateTimeOffset.UtcNow;
        SaveConfig(config);

        _logger?.LogInformation("Password changed");
        return true;
    }

    /// <summary>Remove encryption (delete config).</summary>
    public void RemoveEncryption()
    {
        var path = GetConfigPath();
        if (File.Exists(path)) File.Delete(path);
        _logger?.LogInformation("Encryption removed");
    }

    private static string ComputeVerificationHash(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 100_000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        return Convert.ToBase64String(hash);
    }

    private string GetConfigPath() => Path.Combine(_configDir, "encryption.json");

    private EncryptionConfig? LoadConfig()
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<EncryptionConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch { return null; }
    }

    private void SaveConfig(EncryptionConfig config)
    {
        Directory.CreateDirectory(_configDir);
        var tmpPath = GetConfigPath() + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tmpPath, GetConfigPath(), overwrite: true);
    }
}

public class EncryptionConfig
{
    public string Salt { get; set; } = string.Empty;
    public string VerificationHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Algorithm { get; set; } = "AES-256-GCM";
    public int KdfIterations { get; set; } = 100_000;
}

public class EncryptionSetupResult
{
    public bool Success { get; init; }
    public string? Salt { get; init; }
}
