using System.Security.Cryptography;
using Engram.Store.Security;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Industrial-level tests for the Security layer.
/// Tests encryption roundtrips, key management, export/import, deletion.
/// </summary>
public class SecurityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _workspaceDir;

    public SecurityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-sec-" + Guid.NewGuid().ToString("N")[..8]);
        _workspaceDir = Path.Combine(_tempDir, "workspace");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_workspaceDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── Encryption Service ───

    [Fact]
    public void Encryption_Initialize_DerivesKey()
    {
        using var enc = new EncryptionService();
        enc.Initialize("password123", EncryptionService.GenerateSalt());
        Assert.True(enc.IsInitialized);
    }

    [Fact]
    public void Encryption_Initialize_EmptyPassword_Throws()
    {
        using var enc = new EncryptionService();
        Assert.Throws<ArgumentNullException>(() => enc.Initialize("", "salt"));
    }

    [Fact]
    public void Encryption_EncryptDecrypt_Roundtrip()
    {
        using var enc = new EncryptionService();
        enc.Initialize("test-password", "test-salt-12345");

        var plaintext = "Hello, Engram! This is secret data.";
        var encrypted = enc.EncryptString(plaintext);
        var decrypted = enc.DecryptString(encrypted);

        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(plaintext, encrypted);
    }

    [Fact]
    public void Encryption_EncryptBytes_Roundtrip()
    {
        using var enc = new EncryptionService();
        enc.Initialize("test-password", "test-salt-12345");

        var plaintext = new byte[] { 1, 2, 3, 4, 5, 255, 0, 128 };
        var encrypted = enc.Encrypt(plaintext);
        var decrypted = enc.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encryption_DifferentPasswords_DifferentCiphertext()
    {
        var salt = EncryptionService.GenerateSalt();
        using var enc1 = new EncryptionService();
        using var enc2 = new EncryptionService();
        enc1.Initialize("password1", salt);
        enc2.Initialize("password2", salt);

        var text = "same plaintext";
        var enc1Result = enc1.EncryptString(text);
        var enc2Result = enc2.EncryptString(text);

        Assert.NotEqual(enc1Result, enc2Result);
    }

    [Fact]
    public void Encryption_SamePassword_SamePlaintext_DifferentCiphertext()
    {
        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var text = "same text";
        var enc1 = enc.EncryptString(text);
        var enc2 = enc.EncryptString(text);

        // Different nonces → different ciphertext
        Assert.NotEqual(enc1, enc2);
    }

    [Fact]
    public void Encryption_WrongPassword_DecryptFails()
    {
        var salt = EncryptionService.GenerateSalt();
        using var enc1 = new EncryptionService();
        enc1.Initialize("correct-password", salt);
        var encrypted = enc1.EncryptString("secret");

        using var enc2 = new EncryptionService();
        enc2.Initialize("wrong-password", salt);

        Assert.ThrowsAny<Exception>(() => enc2.DecryptString(encrypted));
    }

    [Fact]
    public void Encryption_NotInitialized_EncryptThrows()
    {
        using var enc = new EncryptionService();
        Assert.Throws<InvalidOperationException>(() => enc.Encrypt(new byte[] { 1 }));
    }

    [Fact]
    public void Encryption_NotInitialized_DecryptThrows()
    {
        using var enc = new EncryptionService();
        Assert.Throws<InvalidOperationException>(() => enc.Decrypt(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Encryption_EmptyPlaintext_Works()
    {
        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var encrypted = enc.Encrypt(Array.Empty<byte>());
        var decrypted = enc.Decrypt(encrypted);
        Assert.Empty(decrypted);
    }

    [Fact]
    public void Encryption_LargeData_Works()
    {
        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var large = new byte[1024 * 1024]; // 1MB
        RandomNumberGenerator.Fill(large);

        var encrypted = enc.Encrypt(large);
        var decrypted = enc.Decrypt(encrypted);
        Assert.Equal(large, decrypted);
    }

    [Fact]
    public void Encryption_EncryptFile_DecryptFile_Roundtrip()
    {
        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var path = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(path, "secret content");

        enc.EncryptFile(path);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".enc"));

        enc.DecryptFile(path + ".enc");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".enc"));
        Assert.Equal("secret content", File.ReadAllText(path));
    }

    [Fact]
    public void Encryption_GenerateSalt_ReturnsUnique()
    {
        var salt1 = EncryptionService.GenerateSalt();
        var salt2 = EncryptionService.GenerateSalt();
        Assert.NotEqual(salt1, salt2);
    }

    [Fact]
    public void Encryption_GenerateKey_Returns32Bytes()
    {
        var key = EncryptionService.GenerateKey();
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void Encryption_Dispose_ClearsKey()
    {
        var enc = new EncryptionService();
        enc.Initialize("password", "salt");
        Assert.True(enc.IsInitialized);
        enc.Dispose();
        Assert.False(enc.IsInitialized);
    }

    [Fact]
    public void Encryption_DoubleDispose_DoesNotThrow()
    {
        var enc = new EncryptionService();
        enc.Initialize("password", "salt");
        enc.Dispose();
        var ex = Record.Exception(() => enc.Dispose());
        Assert.Null(ex);
    }

    // ─── Key Manager ───

    [Fact]
    public void KeyManager_NotConfigured_Initially()
    {
        var km = new KeyManager(_tempDir);
        Assert.False(km.IsConfigured());
    }

    [Fact]
    public void KeyManager_Setup_CreatesConfig()
    {
        var km = new KeyManager(_tempDir);
        var result = km.Setup("password123");

        Assert.True(result.Success);
        Assert.NotNull(result.Salt);
        Assert.True(km.IsConfigured());
    }

    [Fact]
    public void KeyManager_Setup_ShortPassword_Throws()
    {
        var km = new KeyManager(_tempDir);
        Assert.Throws<ArgumentException>(() => km.Setup("short"));
    }

    [Fact]
    public void KeyManager_Unlock_CorrectPassword_ReturnsService()
    {
        var km = new KeyManager(_tempDir);
        km.Setup("password123");

        var enc = km.Unlock("password123");
        Assert.NotNull(enc);
        Assert.True(enc.IsInitialized);
        enc.Dispose();
    }

    [Fact]
    public void KeyManager_Unlock_WrongPassword_ReturnsNull()
    {
        var km = new KeyManager(_tempDir);
        km.Setup("password123");

        var enc = km.Unlock("wrong-password");
        Assert.Null(enc);
    }

    [Fact]
    public void KeyManager_ChangePassword_Works()
    {
        var km = new KeyManager(_tempDir);
        km.Setup("old-password");

        Assert.True(km.ChangePassword("old-password", "new-password"));
        Assert.NotNull(km.Unlock("new-password"));
        Assert.Null(km.Unlock("old-password"));
    }

    [Fact]
    public void KeyManager_ChangePassword_WrongOld_ReturnsFalse()
    {
        var km = new KeyManager(_tempDir);
        km.Setup("password");

        Assert.False(km.ChangePassword("wrong", "new"));
    }

    [Fact]
    public void KeyManager_RemoveEncryption_DeletesConfig()
    {
        var km = new KeyManager(_tempDir);
        km.Setup("password");
        Assert.True(km.IsConfigured());

        km.RemoveEncryption();
        Assert.False(km.IsConfigured());
    }

    // ─── Data Export ───

    [Fact]
    public async Task Export_EmptyWorkspace_CreatesZip()
    {
        var exporter = new DataExport(_workspaceDir);
        var outputPath = Path.Combine(_tempDir, "export.zip");

        var result = await exporter.ExportAsync(outputPath);

        Assert.True(result.Success);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task Export_WithFiles_IncludesAll()
    {
        // Create some files
        Directory.CreateDirectory(Path.Combine(_workspaceDir, "events"));
        Directory.CreateDirectory(Path.Combine(_workspaceDir, "wiki"));
        File.WriteAllText(Path.Combine(_workspaceDir, "events", "e1.jsonl"), "{}");
        File.WriteAllText(Path.Combine(_workspaceDir, "wiki", "w1.md"), "# Test");

        var exporter = new DataExport(_workspaceDir);
        var outputPath = Path.Combine(_tempDir, "export.zip");
        var result = await exporter.ExportAsync(outputPath);

        Assert.True(result.Success);
        Assert.True(result.FileCount >= 2);
        Assert.True(result.TotalBytes > 0);
    }

    [Fact]
    public async Task Import_ValidZip_RestoresFiles()
    {
        // Create and export
        Directory.CreateDirectory(Path.Combine(_workspaceDir, "events"));
        File.WriteAllText(Path.Combine(_workspaceDir, "events", "test.jsonl"), "data");
        var exporter = new DataExport(_workspaceDir);
        var zipPath = Path.Combine(_tempDir, "export.zip");
        await exporter.ExportAsync(zipPath);

        // Clear workspace
        File.Delete(Path.Combine(_workspaceDir, "events", "test.jsonl"));

        // Import
        var result = await exporter.ImportAsync(zipPath);
        Assert.True(result.Success);
        Assert.True(result.FileCount >= 1);
    }

    // ─── Data Delete ───

    [Fact]
    public void Delete_EmptyWorkspace_Succeeds()
    {
        var deleter = new DataDelete(_workspaceDir);
        var result = deleter.DeleteAll();
        Assert.True(result.Success);
    }

    [Fact]
    public void Delete_WithFiles_DeletesAll()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceDir, "events"));
        Directory.CreateDirectory(Path.Combine(_workspaceDir, "wiki"));
        File.WriteAllText(Path.Combine(_workspaceDir, "events", "e1.jsonl"), "{}");
        File.WriteAllText(Path.Combine(_workspaceDir, "wiki", "w1.md"), "# Test");

        var deleter = new DataDelete(_workspaceDir);
        var result = deleter.DeleteAll();

        Assert.True(result.Success);
        Assert.True(result.FileCount >= 2);
        Assert.Contains("events", result.DirectoriesDeleted);
        Assert.Contains("wiki", result.DirectoriesDeleted);
        Assert.False(Directory.Exists(Path.Combine(_workspaceDir, "events")));
    }

    // ─── Sync Service ───

    [Fact]
    public async Task Sync_EmptyWorkspace_PrepareSucceeds()
    {
        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var sync = new SyncService(enc, _workspaceDir);
        var package = await sync.PrepareSyncAsync();

        Assert.NotEmpty(package.DeviceId);
        Assert.Empty(package.Entries);
    }

    [Fact]
    public async Task Sync_WithFiles_PrepareEncrypts()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceDir, "events"));
        File.WriteAllText(Path.Combine(_workspaceDir, "events", "test.jsonl"), "secret data");

        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var sync = new SyncService(enc, _workspaceDir);
        var package = await sync.PrepareSyncAsync();

        Assert.Single(package.Entries);
        Assert.NotEmpty(package.Entries[0].EncryptedContent);
        // Encrypted content should not contain plaintext
        Assert.DoesNotContain("secret data", package.Entries[0].EncryptedContent);
    }

    [Fact]
    public async Task Sync_ApplySync_RestoresFiles()
    {
        // Prepare
        Directory.CreateDirectory(Path.Combine(_workspaceDir, "events"));
        File.WriteAllText(Path.Combine(_workspaceDir, "events", "test.jsonl"), "original data");

        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var sync = new SyncService(enc, _workspaceDir);
        var package = await sync.PrepareSyncAsync();

        // Clear workspace
        File.Delete(Path.Combine(_workspaceDir, "events", "test.jsonl"));

        // Apply
        var result = await sync.ApplySyncAsync(package);
        Assert.True(result.Success);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal("original data", File.ReadAllText(Path.Combine(_workspaceDir, "events", "test.jsonl")));
    }

    [Fact]
    public async Task Sync_ApplySync_SkipsUnchanged()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceDir, "events"));
        File.WriteAllText(Path.Combine(_workspaceDir, "events", "test.jsonl"), "data");

        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var sync = new SyncService(enc, _workspaceDir);
        var package = await sync.PrepareSyncAsync();

        // Apply without changing files
        var result = await sync.ApplySyncAsync(package);
        Assert.True(result.Success);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.AppliedCount);
    }

    // ─── Edge Cases ───

    [Fact]
    public void Encryption_UnicodeString_Roundtrip()
    {
        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var text = "日本語テスト Über Café Ñoño 🔐";
        var result = enc.DecryptString(enc.EncryptString(text));
        Assert.Equal(text, result);
    }

    [Fact]
    public void Encryption_LongString_Roundtrip()
    {
        using var enc = new EncryptionService();
        enc.Initialize("password", "salt");

        var text = new string('A', 100_000);
        var result = enc.DecryptString(enc.EncryptString(text));
        Assert.Equal(text, result);
    }

    [Fact]
    public void KeyManager_Persistence_SurvivesRestart()
    {
        var km1 = new KeyManager(_tempDir);
        km1.Setup("password");

        var km2 = new KeyManager(_tempDir);
        Assert.True(km2.IsConfigured());
        Assert.NotNull(km2.Unlock("password"));
    }

    [Fact]
    public void Delete_NonexistentDir_Succeeds()
    {
        var deleter = new DataDelete(Path.Combine(_tempDir, "nonexistent"));
        var result = deleter.DeleteAll();
        Assert.True(result.Success);
    }

    [Fact]
    public void Security_GenerateSalt_IsBase64()
    {
        var salt = EncryptionService.GenerateSalt();
        var bytes = Convert.FromBase64String(salt);
        Assert.True(bytes.Length >= 32);
    }
}
