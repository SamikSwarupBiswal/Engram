using System;
using System.IO;
using System.Linq;

namespace Engram.Store.Automation;

/// <summary>
/// Enforces sandbox execution containment zones on absolute files and paths
/// to prevent dangerous writes outside authorized workspaces.
/// </summary>
public class ContainmentGuard
{
    private readonly string[] _allowedPaths;
    private readonly string[] _blockedKeywords = { "system32", "registry", "credential", "sam", "etc/hosts", "etc\\hosts" };

    public ContainmentGuard()
    {
        var userDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var tempPath = Path.GetTempPath();

        _allowedPaths = new[]
        {
            Path.Combine(userDocuments, "Engram"),
            userDownloads,
            tempPath
        }.Select(NormalizePath).ToArray();
    }

    public ContainmentGuard(string[] customAllowedPaths)
    {
        if (customAllowedPaths == null) throw new ArgumentNullException(nameof(customAllowedPaths));
        _allowedPaths = customAllowedPaths.Select(NormalizePath).ToArray();
    }

    /// <summary>
    /// Verifies if a file path is safe for operations. Throws InvalidOperationException if unsafe.
    /// </summary>
    public void VerifyPathSafety(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var normalizedPath = NormalizePath(path);

        var parts = normalizedPath.Split('/');
        bool hasBlockedKeyword = parts.Any(p => 
            p.Equals("system32", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("sam", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("registry", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("credential", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("credentials", StringComparison.OrdinalIgnoreCase)
        );

        if (hasBlockedKeyword || 
            normalizedPath.Contains("etc/hosts", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("etc\\hosts", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Safety Violation: Access to blocked zone '{path}' is strictly prohibited.");
        }

        bool isAllowed = false;
        foreach (var allowed in _allowedPaths)
        {
            if (normalizedPath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            {
                isAllowed = true;
                break;
            }
        }

        if (Path.IsPathRooted(normalizedPath) && !isAllowed)
        {
            throw new InvalidOperationException($"Safety Violation: Absolute path '{path}' lies outside the allowed containment zones.");
        }
    }

    /// <summary>
    /// Verifies if a URL is safe. Throws InvalidOperationException if unsafe.
    /// </summary>
    public void VerifyUrlSafety(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var filePath = url.Substring(7);
            VerifyPathSafety(filePath);
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        }
        catch
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }
    }
}
