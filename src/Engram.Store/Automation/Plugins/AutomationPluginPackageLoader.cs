using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Engram.Store.Automation.Plugins;

public sealed partial class AutomationPluginPackageLoader
{
    private const int MaximumManifestBytes = 256 * 1024;
    private const long MaximumEntryPointBytes = 128L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IReadOnlyDictionary<string, string> _trustedPublisherKeys;

    public AutomationPluginPackageLoader(IReadOnlyDictionary<string, string>? trustedPublisherKeys = null) =>
        _trustedPublisherKeys = trustedPublisherKeys ?? new Dictionary<string, string>();

    public IReadOnlyList<AutomationPluginPackage> LoadDirectory(
        string pluginsRoot,
        AutomationPluginTrust trust)
    {
        if (!Enum.IsDefined(trust)) throw new InvalidDataException("Plugin trust value is not supported.");
        var root = Path.GetFullPath(pluginsRoot);
        EnsureNoReparsePoints(root);
        if (!Directory.Exists(root)) return Array.Empty<AutomationPluginPackage>();
        return Directory.GetDirectories(root)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(path => File.Exists(Path.Combine(path, "engram-plugin.json")))
            .Select(path => LoadPackage(path, root, trust))
            .ToArray();
    }

    public AutomationPluginPackage LoadPackage(
        string packageDirectory,
        string trustedRoot,
        AutomationPluginTrust trust)
    {
        if (!Enum.IsDefined(trust)) throw new InvalidDataException("Plugin trust value is not supported.");
        var root = Path.GetFullPath(trustedRoot);
        var directory = Path.GetFullPath(packageDirectory);
        EnsureInsideRoot(directory, root);
        EnsureNoReparsePoints(directory);
        var manifestPath = Path.Combine(directory, "engram-plugin.json");
        EnsureNoReparsePoints(manifestPath);
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"Plugin package is missing engram-plugin.json: {directory}");

        var manifestBytes = ReadManifest(manifestPath);
        AutomationPluginManifest manifest;
        try
        {
            using var reader = new StreamReader(new MemoryStream(manifestBytes, writable: false), Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            manifest = JsonSerializer.Deserialize<AutomationPluginManifest>(
                reader.ReadToEnd(), JsonOptions)
                ?? throw new InvalidDataException("engram-plugin.json is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid engram-plugin.json in '{directory}': {ex.Message}", ex);
        }

        ValidateManifest(manifest, trust);
        string? entryPointPath = null;
        string integrityHash;
        if (manifest.Isolation == AutomationPluginIsolation.TrustedInProcess)
        {
            if (trust != AutomationPluginTrust.FirstParty ||
                !manifest.EntryPoint.StartsWith("builtin://", StringComparison.Ordinal))
                throw new InvalidDataException("Only core-registered first-party plugins may run in-process.");
            integrityHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        }
        else
        {
            if (Path.IsPathRooted(manifest.EntryPoint) || manifest.EntryPoint.Contains(':'))
                throw new InvalidDataException("External plugin entryPoint must be relative to its package.");
            entryPointPath = Path.GetFullPath(Path.Combine(directory, manifest.EntryPoint));
            EnsureInsideRoot(entryPointPath, directory);
            EnsureNoReparsePoints(entryPointPath);
            if (!File.Exists(entryPointPath))
                throw new InvalidDataException($"Plugin entry point does not exist: {manifest.EntryPoint}");
            var integrity = manifest.Integrity
                ?? throw new InvalidDataException("External plugins require integrity metadata and a publisher signature.");
            integrityHash = HashEntryPoint(entryPointPath);
            if (!FixedTimeHexEquals(integrityHash, integrity.EntryPointSha256))
                throw new InvalidDataException("Plugin entry point SHA-256 does not match its signed manifest.");
            VerifyPublisherSignature(manifest, integrityHash, integrity);
        }

        return new AutomationPluginPackage(manifest, directory, entryPointPath, trust, integrityHash);
    }

    private void VerifyPublisherSignature(
        AutomationPluginManifest manifest,
        string integrityHash,
        PluginIntegrityDefinition integrity)
    {
        if (!_trustedPublisherKeys.TryGetValue(integrity.PublisherKeyId, out var publicKeyPem))
            throw new InvalidDataException($"Plugin publisher key '{integrity.PublisherKeyId}' is not trusted.");
        byte[] signature;
        try { signature = Convert.FromBase64String(integrity.Signature); }
        catch (FormatException ex) { throw new InvalidDataException("Plugin signature is not valid base64.", ex); }
        var payload = Encoding.UTF8.GetBytes(SignaturePayload(manifest, integrityHash));
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicKeyPem);
            if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new InvalidDataException("Plugin publisher signature verification failed.");
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new InvalidDataException("Plugin publisher key or signature is invalid.", ex);
        }
    }

    internal static string SignaturePayload(AutomationPluginManifest manifest, string sha256)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            manifest.Id,
            manifest.DisplayName,
            manifest.Version,
            manifest.Publisher,
            manifest.ApiVersion,
            Isolation = manifest.Isolation.ToString(),
            manifest.EntryPoint,
            manifest.PackageUrl,
            manifest.TimeoutMilliseconds,
            manifest.MaxConcurrency,
            Capabilities = manifest.Capabilities
                .OrderBy(capability => capability.Id, StringComparer.Ordinal)
                .Select(capability => new
                {
                    capability.Id,
                    capability.Description,
                    capability.Priority,
                    Permissions = capability.RequiredPermissions.Select(value => value.ToString()).OrderBy(value => value),
                    Arguments = capability.Arguments.OrderBy(argument => argument.Name, StringComparer.Ordinal).Select(argument => new
                    {
                        argument.Name,
                        Type = argument.Type.ToString(),
                        argument.Required,
                        argument.MaxLength,
                        argument.Minimum,
                        argument.Maximum,
                        argument.Pattern
                    })
                })
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return canonical + "\n" + sha256.ToLowerInvariant();
    }

    private static void ValidateManifest(AutomationPluginManifest manifest, AutomationPluginTrust trust)
    {
        RequireText(manifest.Id, "id", 128);
        if (!IdPattern().IsMatch(manifest.Id))
            throw new InvalidDataException($"Plugin id '{manifest.Id}' must be lowercase reverse-DNS form.");
        RequireText(manifest.DisplayName, "displayName", 256);
        RequireText(manifest.Publisher, "publisher", 256);
        RequireText(manifest.EntryPoint, "entryPoint", 1024);
        if (!Enum.IsDefined(manifest.Isolation)) throw new InvalidDataException("Plugin isolation value is not supported.");
        _ = PluginSemanticVersion.Parse(manifest.Version);
        if (!string.Equals(manifest.ApiVersion, AutomationPluginApi.CurrentVersion, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Plugin '{manifest.Id}' targets API {manifest.ApiVersion}; Engram supports {AutomationPluginApi.CurrentVersion}.");
        if (manifest.TimeoutMilliseconds is < 100 or > 300_000)
            throw new InvalidDataException("Plugin timeoutMilliseconds must be between 100 and 300000.");
        if (manifest.MaxConcurrency is < 1 or > 16)
            throw new InvalidDataException("Plugin maxConcurrency must be between 1 and 16.");
        if (manifest.Capabilities is null || manifest.Capabilities.Count is < 1 or > 128)
            throw new InvalidDataException($"Plugin '{manifest.Id}' must declare between 1 and 128 capabilities.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var capability in manifest.Capabilities)
        {
            if (capability is null) throw new InvalidDataException("Plugin capabilities cannot contain null entries.");
            RequireText(capability.Id, "capability id", 128);
            RequireText(capability.Description, "capability description", 4096, allowEmpty: true);
            if (!CapabilityPattern().IsMatch(capability.Id) || !ids.Add(capability.Id))
                throw new InvalidDataException($"Plugin '{manifest.Id}' has an invalid or duplicate capability '{capability.Id}'.");
            if (capability.Priority is < 0 or > 1000)
                throw new InvalidDataException($"Capability '{capability.Id}' priority must be between 0 and 1000.");
            if (capability.RequiredPermissions is null ||
                capability.RequiredPermissions.Count > Enum.GetValues<AutomationPluginPermission>().Length ||
                capability.RequiredPermissions.Any(permission => !Enum.IsDefined(permission)) ||
                capability.RequiredPermissions.Count != capability.RequiredPermissions.Distinct().Count())
                throw new InvalidDataException($"Capability '{capability.Id}' contains null, unsupported or duplicate permissions.");
            if (capability.Arguments is null || capability.Arguments.Count > 64)
                throw new InvalidDataException($"Capability '{capability.Id}' arguments must be a non-null list of at most 64 entries.");
            var argumentNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var argument in capability.Arguments)
            {
                if (argument is null) throw new InvalidDataException("Capability arguments cannot contain null entries.");
                RequireText(argument.Name, "argument name", 64);
                if (!Enum.IsDefined(argument.Type)) throw new InvalidDataException("Capability argument type is not supported.");
                if (!ArgumentPattern().IsMatch(argument.Name) || !argumentNames.Add(argument.Name))
                    throw new InvalidDataException($"Capability '{capability.Id}' has an invalid or duplicate argument '{argument.Name}'.");
                if (argument.MaxLength is < 1 or > 1_048_576)
                    throw new InvalidDataException($"Capability argument '{argument.Name}' has an invalid maxLength.");
                if (argument.Minimum.HasValue && argument.Maximum.HasValue && argument.Minimum > argument.Maximum)
                    throw new InvalidDataException($"Capability argument '{argument.Name}' has an inverted numeric range.");
                if (argument.Pattern is { Length: > 512 })
                    throw new InvalidDataException($"Capability argument '{argument.Name}' pattern exceeds 512 characters.");
            }
        }
        if (manifest.PackageUrl is { Length: > 2048 })
            throw new InvalidDataException("Plugin packageUrl exceeds 2048 characters.");
        if (!string.IsNullOrWhiteSpace(manifest.PackageUrl) &&
            (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidDataException("Plugin packageUrl must use HTTPS.");
        if (trust == AutomationPluginTrust.VerifiedPublisher &&
            manifest.Isolation != AutomationPluginIsolation.ExternalProcess)
            throw new InvalidDataException("Publisher plugins must use ExternalProcess isolation.");
        if (trust == AutomationPluginTrust.FirstParty &&
            !manifest.Publisher.Equals("Engram Project", StringComparison.Ordinal))
            throw new InvalidDataException("First-party plugins must use the Engram Project publisher identity.");
        if (manifest.Integrity is { } integrity)
        {
            RequireText(integrity.PublisherKeyId, "publisherKeyId", 128);
            RequireText(integrity.Signature, "signature", MaximumManifestBytes);
            if (integrity.EntryPointSha256 is not { Length: 64 } || integrity.EntryPointSha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("Plugin entryPointSha256 must contain exactly 64 hexadecimal characters.");
        }
    }

    private static void RequireText(string? value, string field, int maximumLength, bool allowEmpty = false)
    {
        if (value is null || value.Length > maximumLength || !allowEmpty && string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Plugin {field} must be non-null {(allowEmpty ? "text" : "nonempty text")} of at most {maximumLength} characters.");
    }

    private static byte[] ReadManifest(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = stream.Length;
        if (length > MaximumManifestBytes)
            throw new InvalidDataException("Plugin manifest exceeds the 256 KiB byte limit.");
        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Plugin manifest grew while it was being read.");
        return bytes;
    }

    private static string HashEntryPoint(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumEntryPointBytes)
            throw new InvalidDataException("Plugin entry point exceeds the 128 MiB byte limit.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, MaximumEntryPointBytes - total + 1))) > 0)
        {
            total += read;
            if (total > MaximumEntryPointBytes) throw new InvalidDataException("Plugin entry point exceeds the 128 MiB byte limit.");
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Plugin paths cannot contain symbolic links or reparse points.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static bool FixedTimeHexEquals(string actual, string expected)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(expected));
        }
        catch (FormatException) { return false; }
    }

    private static void EnsureInsideRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedRoot, comparison)) return;
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, comparison))
            throw new InvalidDataException($"Plugin path '{path}' escapes trusted root '{root}'.");
    }

    [GeneratedRegex("\\A[a-z0-9]+(?:[.-][a-z0-9]+)+\\z")]
    private static partial Regex IdPattern();
    [GeneratedRegex("\\A[a-z][a-z0-9]*(?:[.-][a-z0-9]+)+\\z")]
    private static partial Regex CapabilityPattern();
    [GeneratedRegex("\\A[a-z][A-Za-z0-9]{0,63}\\z")]
    private static partial Regex ArgumentPattern();
}

public readonly record struct PluginSemanticVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<PluginSemanticVersion>
{
    public static PluginSemanticVersion Parse(string value)
    {
        var match = Regex.Match(value ?? string.Empty,
            "\\A(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)" +
            "(?:-(?<pre>[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?\\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            throw new InvalidDataException($"Plugin version '{value}' is not valid supported SemVer.");
        var prerelease = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
        if (prerelease != null && prerelease.Split('.').Any(identifier => identifier.Length > 1 &&
                identifier[0] == '0' && identifier.All(char.IsAsciiDigit)))
            throw new InvalidDataException($"Plugin version '{value}' has a leading-zero numeric prerelease identifier.");
        return new(major, minor, patch, prerelease);
    }

    public int CompareTo(PluginSemanticVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (PreRelease == null && other.PreRelease != null) return 1;
        if (PreRelease != null && other.PreRelease == null) return -1;
        if (PreRelease == null) return 0;
        var identifiers = PreRelease.Split('.');
        var otherIdentifiers = other.PreRelease!.Split('.');
        for (var index = 0; index < Math.Min(identifiers.Length, otherIdentifiers.Length); index++)
        {
            var first = identifiers[index]; var second = otherIdentifiers[index];
            var numeric = first.All(char.IsAsciiDigit); var otherNumeric = second.All(char.IsAsciiDigit);
            int comparison;
            if (numeric && otherNumeric)
            {
                comparison = first.Length.CompareTo(second.Length);
                if (comparison == 0) comparison = string.Compare(first, second, StringComparison.Ordinal);
            }
            else comparison = numeric != otherNumeric ? numeric ? -1 : 1 : string.Compare(first, second, StringComparison.Ordinal);
            if (comparison != 0) return comparison;
        }
        return identifiers.Length.CompareTo(otherIdentifiers.Length);
    }
}
