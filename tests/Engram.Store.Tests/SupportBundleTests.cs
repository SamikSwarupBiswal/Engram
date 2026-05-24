using System;
using System.IO;
using System.IO.Compression;
using Engram.Store.Security;
using Xunit;

namespace Engram.Store.Tests;

public class SupportBundleTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspacePaths _paths;
    private readonly SupportBundleGenerator _generator;

    public SupportBundleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram_bundle_test_" + Guid.NewGuid().ToString("n")[..8]);
        _paths = new WorkspacePaths(_tempDir);
        
        Directory.CreateDirectory(_paths.Logs);
        Directory.CreateDirectory(_paths.Config);

        _generator = new SupportBundleGenerator(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void RedactSensitiveInformation_RedactsEmails()
    {
        var input = "Contact support at test.user@example.com for details.";
        var result = _generator.RedactSensitiveInformation(input);
        Assert.Contains("[REDACTED_EMAIL]", result);
        Assert.DoesNotContain("test.user@example.com", result);
    }

    [Fact]
    public void RedactSensitiveInformation_RedactsSecrets()
    {
        var input = "var key = \"api_key: \\\"secret-token-12345\\\"\";";
        var inputKeyValue = "api-key: \"mysecrettoken\"\npassword: \"hunter2\"\nauth_token: \"12345\"";
        
        var result = _generator.RedactSensitiveInformation(inputKeyValue);
        Assert.Contains("[REDACTED_SECRET]", result);
        Assert.DoesNotContain("mysecrettoken", result);
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("12345", result);
    }

    [Fact]
    public void RedactSensitiveInformation_RedactsUserHomeName()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userName = Path.GetFileName(userProfile);

        if (!string.IsNullOrEmpty(userName) && userName.Length > 2)
        {
            var input = $"File stored at C:\\Users\\{userName}\\Documents\\file.txt";
            var result = _generator.RedactSensitiveInformation(input);
            Assert.Contains("[USER]", result);
            Assert.DoesNotContain(userName, result);
        }
    }

    [Fact]
    public void GenerateBundle_CreatesValidZipFile()
    {
        // 1. Create a log file with sensitive info
        var logFile = Path.Combine(_paths.Logs, "system.log");
        File.WriteAllText(logFile, "Failed to connect to API with api_key: \"supersecret\" for user@test.com");

        // 2. Create version.json
        var versionFile = Path.Combine(_paths.Config, "version.json");
        File.WriteAllText(versionFile, "{\"system_version\": \"1.0.0\"}");

        // 3. Generate support bundle zip
        var zipOutputPath = Path.Combine(_tempDir, "output_bundles");
        var bundlePath = _generator.GenerateBundle(zipOutputPath);

        Assert.True(File.Exists(bundlePath));
        Assert.Equal(".zip", Path.GetExtension(bundlePath));

        // 4. Verify contents of zip file
        var extractDir = Path.Combine(_tempDir, "extracted");
        ZipFile.ExtractToDirectory(bundlePath, extractDir);

        Assert.True(File.Exists(Path.Combine(extractDir, "version.json")));
        Assert.True(File.Exists(Path.Combine(extractDir, "diagnostics_report.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "logs", "system.log")));

        // Check if logs are correctly redacted inside the zip
        var logContent = File.ReadAllText(Path.Combine(extractDir, "logs", "system.log"));
        Assert.Contains("[REDACTED_SECRET]", logContent);
        Assert.Contains("[REDACTED_EMAIL]", logContent);
        Assert.DoesNotContain("supersecret", logContent);
        Assert.DoesNotContain("user@test.com", logContent);
    }
}
