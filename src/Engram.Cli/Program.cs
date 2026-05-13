using Engram.Store;

namespace Engram.Cli;

/// <summary>
/// Engram CLI — production entrypoint for workspace management and event operations.
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        return args[0] switch
        {
            "init" => HandleInit(args),
            "replay" => HandleReplay(args),
            "verify" => HandleVerify(args),
            "config" => HandleConfig(args),
            "help" or "--help" or "-h" => PrintUsage(),
            _ => Error($"Unknown command: {args[0]}")
        };
    }

    private static int HandleInit(string[] args)
    {
        var root = GetRoot(args);

        try
        {
            var paths = new WorkspacePaths(root);
            var initializer = new WorkspaceInitializer();

            if (initializer.IsInitialized(paths))
            {
                Console.WriteLine($"Workspace already initialized at: {Path.GetFullPath(root)}");
                // Still clean up orphans
                var cleaned = initializer.CleanupOrphanedTempFiles(paths);
                if (cleaned > 0)
                    Console.WriteLine($"  Cleaned {cleaned} orphaned .tmp file(s)");
                return 0;
            }

            initializer.Initialize(paths);
            Console.WriteLine($"Engram workspace initialized at: {Path.GetFullPath(root)}");
            Console.WriteLine("  raw/       - immutable event history");
            Console.WriteLine("  wiki/      - metabolized memory");
            Console.WriteLine("  runs/      - agent run logs");
            Console.WriteLine("  config/    - workspace configuration");
            Console.WriteLine("  logs/      - service logs");
            Console.WriteLine("  archives/  - decayed/stale nodes");
            return 0;
        }
        catch (Exception ex)
        {
            return Error($"Failed to initialize workspace: {ex.Message}");
        }
    }

    private static int HandleReplay(string[] args)
    {
        var root = ".engram";
        var query = new ReplayQuery();
        var jsonOutput = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from" when i + 1 < args.Length:
                    if (DateOnly.TryParse(args[++i], out var from)) query.FromDate = from;
                    else return Error($"Invalid date: {args[i]}");
                    break;
                case "--to" when i + 1 < args.Length:
                    if (DateOnly.TryParse(args[++i], out var to)) query.ToDate = to;
                    else return Error($"Invalid date: {args[i]}");
                    break;
                case "--source" when i + 1 < args.Length:
                    query.Source = args[++i];
                    break;
                case "--status" when i + 1 < args.Length:
                    query.ProcessingStatus = args[++i];
                    break;
                case "--limit" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var limit)) query.Limit = limit;
                    else return Error($"Invalid limit: {args[i]}");
                    break;
                case "--offset" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var offset)) query.Offset = offset;
                    else return Error($"Invalid offset: {args[i]}");
                    break;
                case "--json":
                    jsonOutput = true;
                    break;
                default:
                    if (!args[i].StartsWith("--")) root = args[i];
                    break;
            }
        }

        try
        {
            var paths = new WorkspacePaths(root);
            if (!new WorkspaceInitializer().IsInitialized(paths))
                return Error($"Workspace not initialized at: {Path.GetFullPath(root)}");

            var enumerator = new ReplayEnumerator(paths);
            var events = enumerator.Enumerate(query);

            if (events.Count == 0)
            {
                if (jsonOutput) Console.WriteLine("[]");
                else Console.WriteLine("No raw events found matching filters.");
                return 0;
            }

            if (jsonOutput)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(events, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                    WriteIndented = true
                });
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"Replaying {events.Count} raw event(s):");
                Console.WriteLine();

                foreach (var evt in events)
                {
                    Console.WriteLine($"  [{evt.CapturedAt:yyyy-MM-dd HH:mm:ss}] {evt.EventType}");
                    Console.WriteLine($"    ID:     {evt.EventId}");
                    Console.WriteLine($"    Source: {evt.Source}");
                    Console.WriteLine($"    Hash:   {evt.Hash[..Math.Min(16, evt.Hash.Length)]}...");
                    if (!string.IsNullOrEmpty(evt.Text))
                    {
                        var preview = evt.Text.Length > 80 ? evt.Text[..80] + "..." : evt.Text;
                        Console.WriteLine($"    Text:   {preview}");
                    }
                    Console.WriteLine();
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            return Error($"Failed to replay: {ex.Message}");
        }
    }

    private static int HandleVerify(string[] args)
    {
        var root = GetRoot(args);

        try
        {
            var paths = new WorkspacePaths(root);
            if (!new WorkspaceInitializer().IsInitialized(paths))
                return Error($"Workspace not initialized at: {Path.GetFullPath(root)}");

            Console.WriteLine("Running integrity verification...");
            var enumerator = new ReplayEnumerator(paths);
            var result = enumerator.EnumerateWithIntegrityCheck();

            Console.WriteLine($"  Valid events:     {result.ValidEvents.Count}");
            Console.WriteLine($"  Corrupted events: {result.CorruptedEvents.Count}");

            if (result.CorruptedEvents.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Corrupted files:");
                foreach (var c in result.CorruptedEvents)
                {
                    Console.WriteLine($"  {c.FilePath}");
                    Console.WriteLine($"    Reason: {c.Reason}");
                }
                return 1;
            }

            Console.WriteLine("All events pass integrity verification.");
            return 0;
        }
        catch (Exception ex)
        {
            return Error($"Verification failed: {ex.Message}");
        }
    }

    private static int HandleConfig(string[] args)
    {
        var root = GetRoot(args);

        try
        {
            var paths = new WorkspacePaths(root);
            if (!new WorkspaceInitializer().IsInitialized(paths))
                return Error($"Workspace not initialized at: {Path.GetFullPath(root)}");

            var store = new EngramConfigStore(paths);
            var config = store.Load();

            Console.WriteLine("Engram Configuration:");
            Console.WriteLine($"  Version:                  {config.Version}");
            Console.WriteLine($"  Clipboard capture:        {config.ClipboardCaptureEnabled}");
            Console.WriteLine($"  Active window capture:    {config.ActiveWindowCaptureEnabled}");
            Console.WriteLine($"  File watcher:             {config.FileWatcherEnabled}");
            Console.WriteLine($"  Excluded apps:            {(config.ExcludedApps.Count == 0 ? "(none)" : string.Join(", ", config.ExcludedApps))}");
            Console.WriteLine($"  Watched paths:            {(config.WatchedPaths.Count == 0 ? "(none)" : string.Join(", ", config.WatchedPaths))}");
            return 0;
        }
        catch (Exception ex)
        {
            return Error($"Failed to read config: {ex.Message}");
        }
    }

    private static string GetRoot(string[] args)
    {
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--") && args[i - 1] != "--from" && args[i - 1] != "--to"
                && args[i - 1] != "--source" && args[i - 1] != "--status"
                && args[i - 1] != "--limit" && args[i - 1] != "--offset")
            {
                return args[i];
            }
        }
        return ".engram";
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Engram - Personal Semantic Operating Layer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  engram init [path]                     Initialize workspace");
        Console.WriteLine("  engram replay [path] [options]         Enumerate raw events");
        Console.WriteLine("  engram verify [path]                   Verify event integrity");
        Console.WriteLine("  engram config [path]                   Show configuration");
        Console.WriteLine("  engram help                            Show this help");
        Console.WriteLine();
        Console.WriteLine("Replay options:");
        Console.WriteLine("  --from YYYY-MM-DD                      From date (inclusive)");
        Console.WriteLine("  --to YYYY-MM-DD                        To date (inclusive)");
        Console.WriteLine("  --source <name>                        Filter by source");
        Console.WriteLine("  --status <status>                      Filter by processing status");
        Console.WriteLine("  --limit <n>                            Max events to return");
        Console.WriteLine("  --offset <n>                           Skip first N events");
        Console.WriteLine("  --json                                 Output as JSON");
        return 0;
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
