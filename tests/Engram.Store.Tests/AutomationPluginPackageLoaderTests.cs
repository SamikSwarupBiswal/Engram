using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Engram.Store.Automation.Plugins;

namespace Engram.Store.Tests;

public sealed class AutomationPluginPackageLoaderTests
{
    [Fact]
    public void LoadsFirstPartyManifestWithoutLaunchingAnEntryPoint()
    {
        using var fixture = new PackageFixture();

        var package = fixture.Load();

        Assert.Equal("engram.fixture", package.Manifest.Id);
        Assert.Equal(AutomationPluginTrust.FirstParty, package.Trust);
        Assert.Equal(AutomationPluginIsolation.TrustedInProcess, package.Manifest.Isolation);
        Assert.Null(package.EntryPointPath);
        Assert.Equal(Hash(File.ReadAllBytes(fixture.ManifestPath)), package.IntegrityHash);
    }

    [Fact]
    public void LoadsSignedPublisherPackageAndVerifiesExactEntryBytes()
    {
        using var fixture = new PackageFixture(signed: true);

        var package = fixture.Load();

        Assert.Equal(AutomationPluginTrust.VerifiedPublisher, package.Trust);
        Assert.Equal(AutomationPluginIsolation.ExternalProcess, package.Manifest.Isolation);
        Assert.Equal(fixture.EntryPointPath, package.EntryPointPath);
        Assert.Equal(Hash(File.ReadAllBytes(fixture.EntryPointPath)), package.IntegrityHash);
        Assert.Equal("fixture.capability.run", Assert.Single(package.Manifest.Capabilities).Id);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("capability")]
    [InlineData("argument")]
    [InlineData("integrity")]
    public void UnknownPropertiesAreRejectedAtEveryManifestLevel(string location)
    {
        using var fixture = new PackageFixture(signed: location == "integrity");
        var target = location switch
        {
            "capability" => fixture.Capability,
            "argument" => fixture.Argument,
            "integrity" => fixture.Json["integrity"]!.AsObject(),
            _ => fixture.Json
        };
        target["undeclaredAuthority"] = "never accepted";

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("displayName")]
    [InlineData("version")]
    [InlineData("publisher")]
    [InlineData("entryPoint")]
    public void RequiredManifestPropertiesCannotBeOmitted(string name)
    {
        using var fixture = new PackageFixture();
        fixture.Json.Remove(name);

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Theory]
    [InlineData("apiVersion", "0.9")]
    [InlineData("apiVersion", "2.0")]
    [InlineData("version", "not-a-version")]
    [InlineData("version", "1.0")]
    [InlineData("version", "01.0.0")]
    [InlineData("id", "Engram.Fixture")]
    [InlineData("id", "engram/fixture")]
    [InlineData("id", "engram fixture")]
    [InlineData("isolation", "UnknownIsolation")]
    [InlineData("publisher", "Another Publisher")]
    [InlineData("packageUrl", "http://example.invalid/package")]
    public void UnsupportedOrInvalidManifestValuesAreRejected(string name, string value)
    {
        using var fixture = new PackageFixture();
        fixture.Json[name] = value;

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Theory]
    [InlineData("timeoutMilliseconds", 99)]
    [InlineData("timeoutMilliseconds", 300_001)]
    [InlineData("maxConcurrency", 0)]
    [InlineData("maxConcurrency", 17)]
    public void ManifestExecutionBudgetsAreValidated(string name, int value)
    {
        using var fixture = new PackageFixture();
        fixture.Json[name] = value;

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Theory]
    [InlineData("empty-capabilities")]
    [InlineData("duplicate-capability")]
    [InlineData("invalid-capability")]
    [InlineData("low-priority")]
    [InlineData("high-priority")]
    [InlineData("duplicate-permission")]
    [InlineData("invalid-permission")]
    [InlineData("duplicate-argument")]
    [InlineData("invalid-argument")]
    [InlineData("invalid-argument-type")]
    [InlineData("short-max-length")]
    [InlineData("long-max-length")]
    [InlineData("inverted-range")]
    [InlineData("long-pattern")]
    public void CapabilitySchemaCannotSupplyInvalidOrConflictingDeclarations(string invalid)
    {
        using var fixture = new PackageFixture();
        switch (invalid)
        {
            case "empty-capabilities": fixture.Json["capabilities"] = new JsonArray(); break;
            case "duplicate-capability": fixture.Json["capabilities"]!.AsArray().Add(fixture.Capability.DeepClone()); break;
            case "invalid-capability": fixture.Capability["id"] = "invalid capability"; break;
            case "low-priority": fixture.Capability["priority"] = -1; break;
            case "high-priority": fixture.Capability["priority"] = 1001; break;
            case "duplicate-permission": fixture.Capability["requiredPermissions"]!.AsArray().Add("OfficeRead"); break;
            case "invalid-permission": fixture.Capability["requiredPermissions"] = new JsonArray("UnknownPermission"); break;
            case "duplicate-argument": fixture.Capability["arguments"]!.AsArray().Add(fixture.Argument.DeepClone()); break;
            case "invalid-argument": fixture.Argument["name"] = "path/name"; break;
            case "invalid-argument-type": fixture.Argument["type"] = "UnknownType"; break;
            case "short-max-length": fixture.Argument["maxLength"] = 0; break;
            case "long-max-length": fixture.Argument["maxLength"] = 1_048_577; break;
            case "inverted-range": fixture.Argument["minimum"] = 2; fixture.Argument["maximum"] = 1; break;
            case "long-pattern": fixture.Argument["pattern"] = new string('x', 513); break;
        }

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Fact]
    public void APackageCannotEscapeTheTrustedRootOrUseAPrefixSibling()
    {
        using var fixture = new PackageFixture();
        fixture.Save();
        var prefixSibling = Path.Combine(fixture.Root, "pack");
        Directory.CreateDirectory(prefixSibling);

        Assert.Throws<InvalidDataException>(() => new AutomationPluginPackageLoader().LoadPackage(
            fixture.DirectoryPath, prefixSibling, AutomationPluginTrust.FirstParty));
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("nested/../../outside.bin")]
    [InlineData("absolute")]
    public void ExternalEntryPointMustStayWithinItsPackage(string entryPoint)
    {
        using var fixture = new PackageFixture(signed: true);
        File.WriteAllText(Path.Combine(fixture.Root, "outside.bin"), "inert outside fixture");
        fixture.Json["entryPoint"] = entryPoint == "absolute" ? fixture.EntryPointPath : entryPoint;

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Fact]
    public void MissingExternalEntryPointFailsBeforeSignatureVerification()
    {
        using var fixture = new PackageFixture(signed: true);
        fixture.Json["entryPoint"] = "missing.bin";

        var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
        Assert.Contains("does not exist", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlteredEntryBytesCannotUseTheOriginalPublisherSignature()
    {
        using var fixture = new PackageFixture(signed: true);
        File.AppendAllText(fixture.EntryPointPath, "tampered bytes");

        var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
        Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("permission")]
    [InlineData("timeout")]
    [InlineData("argument")]
    public void AlteredManifestAuthorityCannotUseTheOriginalPublisherSignature(string changed)
    {
        using var fixture = new PackageFixture(signed: true);
        switch (changed)
        {
            case "version": fixture.Json["version"] = "1.1.0"; break;
            case "permission": fixture.Capability["requiredPermissions"]!.AsArray().Add("Shell"); break;
            case "timeout": fixture.Json["timeoutMilliseconds"] = 45_000; break;
            case "argument": fixture.Argument["maxLength"] = 8000; break;
        }

        var error = Assert.Throws<InvalidDataException>(() => fixture.Load());
        Assert.Contains("signature", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownPublisherKeysDoNotBecomeTrustedThroughManifestMetadata()
    {
        using var fixture = new PackageFixture(signed: true);

        var error = Assert.Throws<InvalidDataException>(() => fixture.Load(new AutomationPluginPackageLoader()));
        Assert.Contains("not trusted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not base64")]
    [InlineData("AA==")]
    public void MalformedOrWrongSignaturesAreRejected(string signature)
    {
        using var fixture = new PackageFixture(signed: true);
        fixture.Json["integrity"]!["signature"] = signature;

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Fact]
    public void PublisherTrustCannotEnableAnInProcessPackage()
    {
        using var fixture = new PackageFixture();
        fixture.Json["publisher"] = "Vendor Fixture";

        Assert.Throws<InvalidDataException>(() => fixture.Load(trust: AutomationPluginTrust.VerifiedPublisher));
    }

    [Fact]
    public void MissingIntegrityMetadataCannotLoadAnExternalPackage()
    {
        using var fixture = new PackageFixture(signed: true);
        fixture.Json.Remove("integrity");

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Fact]
    public void DirectoryDiscoveryLoadsOnlyManifestPackagesAndAllowsAMissingRoot()
    {
        using var fixture = new PackageFixture();
        fixture.Save();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "no-manifest"));
        var loader = new AutomationPluginPackageLoader();

        Assert.Equal("engram.fixture", Assert.Single(loader.LoadDirectory(fixture.Root, AutomationPluginTrust.FirstParty)).Manifest.Id);
        Assert.Empty(loader.LoadDirectory(Path.Combine(fixture.Root, "not-present"), AutomationPluginTrust.FirstParty));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("entryPoint")]
    [InlineData("capabilities")]
    [InlineData("capability")]
    [InlineData("capability-id")]
    [InlineData("permissions")]
    [InlineData("arguments")]
    [InlineData("argument")]
    [InlineData("argument-name")]
    public void NullRequiredFieldsAndCollectionItemsFailWithAControlledValidationError(string field)
    {
        using var fixture = new PackageFixture();
        switch (field)
        {
            case "capability": fixture.Json["capabilities"] = new JsonArray((JsonNode?)null); break;
            case "capability-id": fixture.Capability["id"] = null; break;
            case "permissions": fixture.Capability["requiredPermissions"] = null; break;
            case "arguments": fixture.Capability["arguments"] = null; break;
            case "argument": fixture.Capability["arguments"] = new JsonArray((JsonNode?)null); break;
            case "argument-name": fixture.Argument["name"] = null; break;
            default: fixture.Json[field] = null; break;
        }

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Theory]
    [InlineData("entryPointSha256")]
    [InlineData("publisherKeyId")]
    [InlineData("signature")]
    public void NullIntegrityFieldsCannotBypassValidationOrLeakRuntimeExceptions(string field)
    {
        using var fixture = new PackageFixture(signed: true);
        fixture.Json["integrity"]![field] = null;

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Theory]
    [InlineData("trust")]
    [InlineData("isolation")]
    [InlineData("permission")]
    [InlineData("argument-type")]
    public void UndefinedNumericEnumsDoNotAcquireAuthority(string field)
    {
        using var fixture = new PackageFixture(signed: field == "trust");
        switch (field)
        {
            case "isolation":
                fixture.Json["isolation"] = 99;
                fixture.Json["entryPoint"] = "entry.bin";
                File.WriteAllText(fixture.EntryPointPath, "inert undefined-isolation fixture");
                fixture.Resign();
                break;
            case "permission": fixture.Capability["requiredPermissions"] = new JsonArray(99); break;
            case "argument-type": fixture.Argument["type"] = 99; break;
        }

        Assert.Throws<InvalidDataException>(() => fixture.Load(trust: field == "trust" ? (AutomationPluginTrust)99 : null));
    }

    [Theory]
    [InlineData("id", 128)]
    [InlineData("displayName", 256)]
    [InlineData("publisher", 256)]
    [InlineData("description", 4096)]
    [InlineData("entryPoint", 1024)]
    [InlineData("packageUrl", 2048)]
    [InlineData("capability-id", 128)]
    public void ManifestTextLimitsRejectOversizedValues(string field, int limit)
    {
        using var fixture = new PackageFixture(signed: field == "publisher");
        switch (field)
        {
            case "id": fixture.Json["id"] = "a." + new string('b', limit - 1); break;
            case "description": fixture.Capability["description"] = new string('x', limit + 1); break;
            case "entryPoint": fixture.Json["entryPoint"] = "builtin://" + new string('x', limit + 1 - "builtin://".Length); break;
            case "packageUrl":
                const string prefix = "https://example.invalid/";
                fixture.Json["packageUrl"] = prefix + new string('x', limit + 1 - prefix.Length);
                break;
            case "capability-id": fixture.Capability["id"] = "a." + new string('b', limit - 1); break;
            default: fixture.Json[field] = new string('x', limit + 1); break;
        }
        if (field == "publisher") fixture.Resign();

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("capability-id")]
    [InlineData("argument-name")]
    public void IdentifiersCannotHideATrailingNewline(string field)
    {
        using var fixture = new PackageFixture();
        switch (field)
        {
            case "id": fixture.Json["id"] = "engram.fixture\n"; break;
            case "capability-id": fixture.Capability["id"] = "fixture.capability.run\n"; break;
            case "argument-name": fixture.Argument["name"] = "bindingId\n"; break;
        }

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Fact]
    public void CapabilityCountIsBoundedAndTheBoundaryRemainsUsable()
    {
        using var fixture = new PackageFixture();
        var template = fixture.Capability.DeepClone();
        var capabilities = new JsonArray();
        for (var index = 0; index < 128; index++)
        {
            var capability = template.DeepClone();
            capability["id"] = "fixture.capability" + index + ".run";
            capabilities.Add(capability);
        }
        fixture.Json["capabilities"] = capabilities;
        Assert.Equal(128, fixture.Load().Manifest.Capabilities.Count);
        var overflow = template.DeepClone();
        overflow["id"] = "fixture.overflow.run";
        capabilities.Add(overflow);

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Fact]
    public void ArgumentCountIsBoundedAndTheBoundaryRemainsUsable()
    {
        using var fixture = new PackageFixture();
        var template = fixture.Argument.DeepClone();
        var arguments = new JsonArray();
        for (var index = 0; index < 64; index++)
        {
            var argument = template.DeepClone();
            argument["name"] = "argument" + index;
            arguments.Add(argument);
        }
        fixture.Capability["arguments"] = arguments;
        Assert.Equal(64, Assert.Single(fixture.Load().Manifest.Capabilities).Arguments.Count);
        var overflow = template.DeepClone();
        overflow["name"] = "overflow";
        arguments.Add(overflow);

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Fact]
    public void ManifestByteLimitIsEnforcedBeforeAnOversizedManifestIsRead()
    {
        using var fixture = new PackageFixture();
        fixture.Save();
        const int limit = 256 * 1024;
        File.AppendAllText(fixture.ManifestPath, new string(' ', limit - (int)new FileInfo(fixture.ManifestPath).Length));
        var loader = new AutomationPluginPackageLoader();
        Assert.Equal("engram.fixture", loader.LoadPackage(fixture.DirectoryPath, fixture.Root, AutomationPluginTrust.FirstParty).Manifest.Id);
        File.AppendAllText(fixture.ManifestPath, " ");

        Assert.Throws<InvalidDataException>(() => loader.LoadPackage(fixture.DirectoryPath, fixture.Root, AutomationPluginTrust.FirstParty));
    }

    [Fact]
    public void AValidlySignedOversizedEntryPointIsRejectedByItsByteLimit()
    {
        using var fixture = new PackageFixture(signed: true);
        using (var stream = new FileStream(fixture.EntryPointPath, FileMode.Open, FileAccess.Write))
            stream.SetLength(128L * 1024 * 1024 + 1);
        fixture.Resign();

        Assert.Throws<InvalidDataException>(() => fixture.Load());
    }

    [Theory]
    [InlineData("package")]
    [InlineData("manifest")]
    [InlineData("entry")]
    [InlineData("trusted-root-ancestor")]
    public void SymbolicLinksCannotRedirectPackageFilesOrTrustedRootComponents(string component)
    {
        using var fixture = new PackageFixture(signed: component == "entry");
        fixture.Save();
        var package = fixture.DirectoryPath;
        var root = fixture.Root;
        switch (component)
        {
            case "package":
                package = Path.Combine(root, "linked-package");
                Directory.CreateSymbolicLink(package, fixture.DirectoryPath);
                break;
            case "manifest":
                var manifestTarget = Path.Combine(root, "real-manifest.json");
                File.Move(fixture.ManifestPath, manifestTarget);
                File.CreateSymbolicLink(fixture.ManifestPath, manifestTarget);
                break;
            case "entry":
                var entryTarget = Path.Combine(root, "real-entry.bin");
                File.Move(fixture.EntryPointPath, entryTarget);
                File.CreateSymbolicLink(fixture.EntryPointPath, entryTarget);
                break;
            case "trusted-root-ancestor":
                var realRoot = Directory.CreateDirectory(Path.Combine(root, "real-root", "plugins")).FullName;
                var realPackage = Directory.CreateDirectory(Path.Combine(realRoot, "package")).FullName;
                File.Copy(fixture.ManifestPath, Path.Combine(realPackage, "engram-plugin.json"));
                var linkedRoot = Path.Combine(root, "linked-root");
                Directory.CreateSymbolicLink(linkedRoot, Path.GetDirectoryName(realRoot)!);
                root = Path.Combine(linkedRoot, "plugins");
                package = Path.Combine(root, "package");
                break;
        }

        Assert.Throws<InvalidDataException>(() => fixture.LoadSaved(packageDirectory: package, trustedRoot: root));
    }

    [Theory]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10")]
    [InlineData("1.0.0-A", "1.0.0-a")]
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha", "1.0.0")]
    [InlineData("1.0.0-999999999999999999999", "1.0.0-1000000000000000000000")]
    public void SemanticVersionOrderingUsesIdentifierRules(string lower, string higher)
    {
        var first = PluginSemanticVersion.Parse(lower);
        var second = PluginSemanticVersion.Parse(higher);

        Assert.True(first.CompareTo(second) < 0);
        Assert.True(second.CompareTo(first) > 0);
    }

    [Fact]
    public void SemanticVersionBuildMetadataDoesNotChangePrecedence()
    {
        Assert.Equal(0, PluginSemanticVersion.Parse("1.0.0+001").CompareTo(PluginSemanticVersion.Parse("1.0.0+other")));
        Assert.Equal(0, PluginSemanticVersion.Parse("1.0.0-beta.2+build.9").CompareTo(PluginSemanticVersion.Parse("1.0.0-beta.2")));
    }

    [Theory]
    [InlineData("999999999999999999999.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-alpha..1")]
    [InlineData("1.0.0-.alpha")]
    [InlineData("1.0.0-alpha.")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-alpha.01")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+meta..x")]
    [InlineData("1.0.0+.x")]
    [InlineData("1.0.0\n")]
    public void InvalidSemanticVersionsProduceAControlledValidationError(string version)
    {
        Assert.Throws<InvalidDataException>(() => PluginSemanticVersion.Parse(version));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class PackageFixture : IDisposable
    {
        private static readonly JsonSerializerOptions ManifestJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        private AutomationPluginPackageLoader _loader;
        private readonly AutomationPluginTrust _trust;
        public string Root { get; } = Directory.CreateTempSubdirectory("engram-plugin-loader-").FullName;
        public string DirectoryPath { get; }
        public string ManifestPath => Path.Combine(DirectoryPath, "engram-plugin.json");
        public string EntryPointPath => Path.Combine(DirectoryPath, "entry.bin");
        public JsonObject Json { get; }
        public JsonObject Capability => Json["capabilities"]![0]!.AsObject();
        public JsonObject Argument => Capability["arguments"]![0]!.AsObject();

        public PackageFixture(bool signed = false)
        {
            DirectoryPath = Directory.CreateDirectory(Path.Combine(Root, "package")).FullName;
            _trust = signed ? AutomationPluginTrust.VerifiedPublisher : AutomationPluginTrust.FirstParty;
            var manifest = new AutomationPluginManifest
            {
                Id = signed ? "vendor.fixture" : "engram.fixture", DisplayName = "Loader fixture", Version = "1.0.0",
                Publisher = signed ? "Vendor Fixture" : "Engram Project",
                Isolation = signed ? AutomationPluginIsolation.ExternalProcess : AutomationPluginIsolation.TrustedInProcess,
                EntryPoint = signed ? "entry.bin" : "builtin://fixture",
                Capabilities =
                [
                    new() { Id = "fixture.capability.run", RequiredPermissions = [AutomationPluginPermission.OfficeRead],
                        Arguments = [new() { Name = "bindingId", MaxLength = 32 }] }
                ]
            };
            Json = JsonSerializer.SerializeToNode(manifest, ManifestJson)!.AsObject();
            if (signed)
            {
                File.WriteAllText(EntryPointPath, "inert package bytes; never executed", new UTF8Encoding(false));
                var hash = Hash(File.ReadAllBytes(EntryPointPath));
                using var rsa = RSA.Create(2048);
                var signature = rsa.SignData(Encoding.UTF8.GetBytes(AutomationPluginPackageLoader.SignaturePayload(manifest, hash)),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                Json["integrity"] = JsonSerializer.SerializeToNode(new PluginIntegrityDefinition
                {
                    EntryPointSha256 = hash, PublisherKeyId = "fixture-key", Signature = Convert.ToBase64String(signature)
                }, ManifestJson);
                _loader = new AutomationPluginPackageLoader(new Dictionary<string, string> { ["fixture-key"] = rsa.ExportRSAPublicKeyPem() });
            }
            else _loader = new AutomationPluginPackageLoader();
        }

        public void Save() => File.WriteAllText(ManifestPath, Json.ToJsonString(), new UTF8Encoding(false));

        public AutomationPluginPackage Load(AutomationPluginPackageLoader? loader = null, AutomationPluginTrust? trust = null)
        {
            Save();
            return LoadSaved(loader, trust);
        }

        public AutomationPluginPackage LoadSaved(AutomationPluginPackageLoader? loader = null, AutomationPluginTrust? trust = null,
            string? packageDirectory = null, string? trustedRoot = null) =>
            (loader ?? _loader).LoadPackage(packageDirectory ?? DirectoryPath, trustedRoot ?? Root, trust ?? _trust);

        public void Resign()
        {
            var manifest = Json.Deserialize<AutomationPluginManifest>(ManifestJson)!;
            using var stream = File.OpenRead(EntryPointPath);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            using var rsa = RSA.Create(2048);
            Json["integrity"] = JsonSerializer.SerializeToNode(new PluginIntegrityDefinition
            {
                EntryPointSha256 = hash, PublisherKeyId = "fixture-key",
                Signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(AutomationPluginPackageLoader.SignaturePayload(manifest, hash)),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            }, ManifestJson);
            _loader = new AutomationPluginPackageLoader(new Dictionary<string, string> { ["fixture-key"] = rsa.ExportRSAPublicKeyPem() });
        }

        public void Dispose()
        {
            var root = Path.GetFullPath(Root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(root).StartsWith("engram-plugin-loader-", StringComparison.Ordinal))
                throw new InvalidOperationException("Fixture cleanup escaped its owned temporary directory.");
            Directory.Delete(root, recursive: true);
        }
    }
}
