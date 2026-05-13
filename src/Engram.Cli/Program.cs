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
        var root = args.Length > 1 ? args[1] : ".engram";

        try
        {
            var paths = new WorkspacePaths(root);
            var initializer = new WorkspaceInitializer();

            if (!initializer.IsInitialized(paths))
            {
                return Error($"Workspace not initialized at: {Path.GetFullPath(root)}");
            }

            var enumerator = new ReplayEnumerator(paths);
            var events = enumerator.EnumerateAll();

            if (events.Count == 0)
            {
                Console.WriteLine("No raw events found.");
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
        Console.WriteLine("  engram init [path]     Initialize .engram workspace (default: .engram)");
        Console.WriteLine("  engram replay [path]   Enumerate and display raw events");
        Console.WriteLine("  engram help            Show this help");
        return 0;
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }
}
