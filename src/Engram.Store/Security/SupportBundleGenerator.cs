using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Engram.Store.Security;

public class SupportBundleGenerator
{
    private readonly WorkspacePaths _paths;
    private static readonly Regex EmailRegex = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
    private static readonly Regex KeyRegex = new(@"(api[-_]key|password|secret|auth[-_]token|token)\s*[:=]\s*""[^""]+""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SupportBundleGenerator(WorkspacePaths paths)
    {
        _paths = paths;
    }

    public string GenerateBundle(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var bundleName = $"engram_support_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.zip";
        var outputPath = Path.Combine(outputDirectory, bundleName);

        var tempDir = Path.Combine(Path.GetTempPath(), $"engram_diag_{Guid.NewGuid():n}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Copy and redact logs
            CopyAndRedactDirectory(_paths.Logs, Path.Combine(tempDir, "logs"));

            // 2. Export system configuration
            var versionInfoPath = Path.Combine(_paths.Config, "version.json");
            if (File.Exists(versionInfoPath))
            {
                File.Copy(versionInfoPath, Path.Combine(tempDir, "version.json"));
            }

            // 3. Generate system diagnostics report
            var reportText = $"Generated At: {DateTimeOffset.UtcNow}\n" +
                             $"OS: {Environment.OSVersion}\n" +
                             $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\n" +
                             $"Process Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64)}\n";
            File.WriteAllText(Path.Combine(tempDir, "diagnostics_report.txt"), reportText);

            // 4. Zip the temporary directory
            ZipFile.CreateFromDirectory(tempDir, outputPath);
            return outputPath;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private void CopyAndRedactDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var content = File.ReadAllText(file);
            var redacted = RedactSensitiveInformation(content);
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.WriteAllText(destFile, redacted);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSub = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyAndRedactDirectory(subDir, destSub);
        }
    }

    public string RedactSensitiveInformation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // 1. Redact Emails
        var clean = EmailRegex.Replace(text, "[REDACTED_EMAIL]");

        // 2. Redact API keys and passwords
        clean = KeyRegex.Replace(clean, "$1 = \"[REDACTED_SECRET]\"");

        // 3. Redact user home directory names
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userName = Path.GetFileName(userProfile);
        if (!string.IsNullOrEmpty(userName) && userName.Length > 2)
        {
            clean = clean.Replace(userName, "[USER]");
        }

        return clean;
    }
}
