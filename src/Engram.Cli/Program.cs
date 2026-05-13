using Engram.Store;

namespace Engram.Cli;

/// <summary>
/// Engram CLI — developer entrypoint for workspace management and event operations.
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
            "help" or "--help" or "-h" => PrintUsage(),
            _ => Error($"Unknown command: {args[0]}")
        };
    }

    private static int HandleInit(string[] args)
    {
        var root = args.Length > 1 ? args[1] : ".engram";

        try
        {
            var paths = new WorkspacePaths(root);
            var initializer = new WorkspaceInitializer();

            if (initializer.IsInitialized(paths))
            {
                Console.WriteLine($"Workspace already initialized at: {Path.GetFullPath(root)}");
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
        // Parse root path (first non-flag argument after "replay")
        var root = ".engram";
        var query = new ReplayQuery();

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from" when i + 1 < args.Length:
                    if (DateOnly.TryParse(args[++i], out var from))
                        query.FromDate = from;
                    else
                        return Error($"Invalid date format: {args[i]}");
                    break;
                case "--to" when i + 1 < args.Length:
                    if (DateOnly.TryParse(args[++i], out var to))
                        query.ToDate = to;
                    else
                        return Error($"Invalid date format: {args[i]}");
                    break;
                case "--source" when i + 1 < args.Length:
                    query.Source = args[++i];
                    break;
                case "--status" when i + 1 < args.Length:
                    query.ProcessingStatus = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith("--"))
                        root = args[i];
                    break;
            }
        }

        try
        {
            var paths = new WorkspacePaths(root);
            var initializer = new WorkspaceInitializer();

            if (!initializer.IsInitialized(paths))
            {
                return Error($"Workspace not initialized at: {Path.GetFullPath(root)}");
            }

            var enumerator = new ReplayEnumerator(paths);
            var events = enumerator.Enumerate(query);

            if (events.Count == 0)
            {
                Console.WriteLine("No raw events found matching filters.");
                return 0;
            }

            Console.WriteLine($"Replaying {events.Count} raw event(s):");
            Console.WriteLine();

            foreach (var evt in events)
            {
                Console.WriteLine($"  [{evt.CapturedAt:yyyy-MM-dd HH:mm:ss}] {evt.EventType}");
                Console.WriteLine($"    ID:     {evt.EventId}");
                Console.WriteLine($"    Source: {evt.Source}");
                Console.WriteLine($"    Hash:   {evt.Hash[..16]}...");
                if (!string.IsNullOrEmpty(evt.Text))
                {
                    var preview = evt.Text.Length > 80 ? evt.Text[..80] + "..." : evt.Text;
                    Console.WriteLine($"    Text:   {preview}");
                }
                Console.WriteLine();
            }

            return 0;
        }
        catch (Exception ex)
        {
            return Error($"Failed to replay events: {ex.Message}");
        }
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Engram - Personal Semantic Operating Layer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  engram init [path]                     Initialize .engram workspace");
        Console.WriteLine("  engram replay [path] [options]         Enumerate and display raw events");
        Console.WriteLine("  engram help                            Show this help");
        Console.WriteLine();
        Console.WriteLine("Replay options:");
        Console.WriteLine("  --from YYYY-MM-DD                      Include events from this date");
        Console.WriteLine("  --to YYYY-MM-DD                        Include events up to this date");
        Console.WriteLine("  --source <name>                        Filter by event source");
        Console.WriteLine("  --status <status>                      Filter by processing status");
        return 0;
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
