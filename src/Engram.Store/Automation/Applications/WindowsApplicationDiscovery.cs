using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Win32;

namespace Engram.Store.Automation.Applications;

public interface IApplicationDiscovery
{
    Task<IReadOnlyList<InstalledApplication>> DiscoverAsync(CancellationToken ct = default);
}

public sealed class WindowsApplicationDiscovery : IApplicationDiscovery
{
    public Task<IReadOnlyList<InstalledApplication>> DiscoverAsync(CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<InstalledApplication>>(() => Discover(ct), ct);
    }

    private static IReadOnlyList<InstalledApplication> Discover(CancellationToken ct)
    {
        var discovered = new List<InstalledApplication>();
        if (OperatingSystem.IsWindows())
        {
            DiscoverAppPaths(discovered, ct);
            DiscoverUninstallRegistry(discovered, ct);
            DiscoverStartMenuShortcuts(discovered, ct);
            DiscoverPackagedApplications(discovered, ct);
        }
        DiscoverRunningProcesses(discovered, ct);
        return Merge(discovered);
    }

    internal static IReadOnlyList<InstalledApplication> Merge(IEnumerable<InstalledApplication> applications)
    {
        var merged = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
        foreach (var application in applications)
        {
            var key = BuildMergeKey(application);
            if (!merged.TryGetValue(key, out var existing))
            {
                merged[key] = application;
                continue;
            }

            existing.Name = Choose(existing.Name, application.Name) ?? existing.Name;
            existing.ExecutablePath = Choose(existing.ExecutablePath, application.ExecutablePath);
            existing.LaunchTarget = Choose(existing.LaunchTarget, application.LaunchTarget);
            existing.ProcessName = Choose(existing.ProcessName, application.ProcessName);
            existing.Version = Choose(existing.Version, application.Version);
            existing.Publisher = Choose(existing.Publisher, application.Publisher);
            existing.IsLaunchable |= application.IsLaunchable;
            existing.Sources.UnionWith(application.Sources);
            existing.Aliases.UnionWith(application.Aliases);
            existing.Aliases.Add(application.Name);
            existing.LastSeenAt = DateTimeOffset.UtcNow;
        }

        return merged.Values.OrderBy(application => application.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildMergeKey(InstalledApplication application)
    {
        if (!string.IsNullOrWhiteSpace(application.ExecutablePath))
            return "exe:" + NormalizeExecutable(application.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(application.LaunchTarget))
            return "launch:" + application.LaunchTarget.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(application.ProcessName))
            return "process:" + application.ProcessName.Trim().ToLowerInvariant();
        return "name:" + application.Name.Trim().ToLowerInvariant();
    }

    private static string? Choose(string? current, string? candidate) =>
        string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(candidate) ? candidate : current;

    private static void DiscoverAppPaths(ICollection<InstalledApplication> output, CancellationToken ct)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var appPaths = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                if (appPaths == null) continue;
                foreach (var subKeyName in appPaths.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using var appKey = appPaths.OpenSubKey(subKeyName);
                    var path = appKey?.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    path = path.Trim('"');
                    var name = Path.GetFileNameWithoutExtension(subKeyName);
                    output.Add(Create(name, path, path, Path.GetFileNameWithoutExtension(path), null, null,
                        ApplicationDiscoverySource.AppPaths, File.Exists(path)));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A protected or malformed registry entry must not abort inventory discovery.
            }
        }
    }

    private static void DiscoverUninstallRegistry(ICollection<InstalledApplication> output, CancellationToken ct)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using var appKey = uninstall.OpenSubKey(subKeyName);
                    var name = appKey?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var icon = CleanDisplayIcon(appKey?.GetValue("DisplayIcon") as string);
                    var processName = icon == null ? null : Path.GetFileNameWithoutExtension(icon);
                    output.Add(Create(name, icon, icon, processName,
                        appKey?.GetValue("DisplayVersion") as string,
                        appKey?.GetValue("Publisher") as string,
                        ApplicationDiscoverySource.UninstallRegistry,
                        icon != null && File.Exists(icon)));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Continue across inaccessible registry views.
            }
        }
    }

    private static void DiscoverStartMenuShortcuts(ICollection<InstalledApplication> output, CancellationToken ct)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        object? shell = null;
        try
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null) shell = Activator.CreateInstance(shellType);
            }
            catch { }

            foreach (var root in roots)
            {
                foreach (var shortcutPath in EnumerateAccessibleFiles(root, "*.lnk", ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileNameWithoutExtension(shortcutPath);
                    string? target = null;
                    object? shortcut = null;
                    try
                    {
                        if (shell != null)
                        {
                            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
                            target = ((dynamic)shortcut).TargetPath as string;
                        }
                    }
                    catch { }
                    finally
                    {
                        ReleaseShellObject(shortcut);
                    }

                    var isStore = target?.Contains("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) == true;
                    var executable = !isStore && !string.IsNullOrWhiteSpace(target) ? target : null;
                    var processName = executable == null ? null : Path.GetFileNameWithoutExtension(executable);
                    output.Add(Create(name, executable, shortcutPath, processName, null, null,
                        isStore ? ApplicationDiscoverySource.MicrosoftStore : ApplicationDiscoverySource.StartMenu,
                        File.Exists(shortcutPath)));
                }
            }
        }
        finally
        {
            ReleaseShellObject(shell);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DiscoverPackagedApplications(ICollection<InstalledApplication> output, CancellationToken ct)
    {
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return;
            folder = ((dynamic)shell).NameSpace(PackagedWindowsApplication.AppsFolder);
            if (folder == null) return;
            items = ((dynamic)folder).Items();
            if (items == null) return;

            int count = ((dynamic)items).Count;
            for (var index = 0; index < count; index++)
            {
                ct.ThrowIfCancellationRequested();
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(index);
                    if (item == null) continue;
                    dynamic entry = item;
                    var application = CreatePackagedApplication(entry.Name as string, entry.Path as string,
                        entry.ExtendedProperty("System.AppUserModel.ID") as string);
                    if (application != null) output.Add(application);
                }
                catch (Exception ex) when (ex is COMException or RuntimeBinderException)
                {
                    // An inaccessible shell item does not suppress the rest of the inventory.
                }
                finally
                {
                    ReleaseShellObject(item);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException)
        {
            // Keep the registry, shortcut, and running-process inventory when AppsFolder is unavailable.
        }
        finally
        {
            ReleaseShellObject(items);
            ReleaseShellObject(folder);
            ReleaseShellObject(shell);
        }
    }

    internal static InstalledApplication? CreatePackagedApplication(string? name, string? path, string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appUserModelId) ||
            !string.Equals(path, appUserModelId, StringComparison.OrdinalIgnoreCase))
            return null;

        var launchTarget = PackagedWindowsApplication.CreateLaunchTarget(appUserModelId);
        if (!PackagedWindowsApplication.TryGetId(launchTarget, out var id)) return null;

        var application = Create(name, null, launchTarget, null, null, null, ApplicationDiscoverySource.MicrosoftStore, true);
        application.Aliases.Add(id);
        return application;
    }

    [SupportedOSPlatform("windows")]
    private static void ReleaseShellObject(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }

    internal static IReadOnlyList<string> EnumerateAccessibleFiles(
        string root,
        string searchPattern,
        CancellationToken ct,
        Func<string, string, string[]>? getFiles = null,
        Func<string, string[]>? getDirectories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        getFiles ??= static (directory, pattern) => Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        getDirectories ??= static directory => Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly);

        var files = new List<string>();
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string canonical;
            try { canonical = Path.GetFullPath(directory); }
            catch (Exception ex) when (IsEnumerationFailure(ex)) { continue; }
            if (!visited.Add(canonical)) continue;

            try { files.AddRange(getFiles(directory, searchPattern)); }
            catch (Exception ex) when (IsEnumerationFailure(ex)) { }

            string[] children;
            try { children = getDirectories(directory); }
            catch (Exception ex) when (IsEnumerationFailure(ex)) { continue; }
            for (var index = children.Length - 1; index >= 0; index--)
                pending.Push(children[index]);
        }

        return files;
    }

    private static bool IsEnumerationFailure(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or SecurityException;

    private static void DiscoverRunningProcesses(ICollection<InstalledApplication> output, CancellationToken ct)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    var path = process.MainModule?.FileName;
                    var version = path != null && File.Exists(path)
                        ? FileVersionInfo.GetVersionInfo(path).FileVersion
                        : null;
                    output.Add(Create(
                        string.IsNullOrWhiteSpace(process.MainWindowTitle) ? process.ProcessName : process.MainWindowTitle,
                        path,
                        path,
                        process.ProcessName,
                        version,
                        null,
                        ApplicationDiscoverySource.RunningProcess,
                        path != null));
                }
                catch { }
            }
        }
    }

    private static InstalledApplication Create(
        string name,
        string? executablePath,
        string? launchTarget,
        string? processName,
        string? version,
        string? publisher,
        ApplicationDiscoverySource source,
        bool launchable)
    {
        var application = new InstalledApplication
        {
            ApplicationId = InstalledApplication.CreateStableId(name, executablePath, launchTarget, processName),
            Name = name.Trim(),
            ExecutablePath = executablePath,
            LaunchTarget = launchTarget,
            ProcessName = processName,
            Version = version,
            Publisher = publisher,
            IsLaunchable = launchable,
            Sources = new HashSet<ApplicationDiscoverySource> { source }
        };
        application.Aliases.Add(application.Name);
        if (!string.IsNullOrWhiteSpace(processName)) application.Aliases.Add(processName);
        return application;
    }

    private static string? CleanDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = value.Trim().Trim('"');
        var comma = cleaned.LastIndexOf(',');
        if (comma > 2 && int.TryParse(cleaned[(comma + 1)..], out _)) cleaned = cleaned[..comma];
        return Environment.ExpandEnvironmentVariables(cleaned.Trim('"'));
    }

    private static string NormalizeExecutable(string value)
    {
        try { return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant(); }
        catch { return value.Trim().ToLowerInvariant(); }
    }
}
