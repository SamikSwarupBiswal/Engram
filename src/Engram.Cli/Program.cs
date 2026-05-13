using Engram.Store;
using Engram.Store.Cloud;

namespace Engram.Cli;

/// <summary>
/// Engram CLI — local-first semantic memory layer.
/// Provides commands for workspace management, search, briefs, and cloud diagnostics.
/// </summary>
public class Program
{
    public static Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return Task.FromResult(0);
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            var result = command switch
            {
                "init" => HandleInit(args),
                "status" => HandleStatus(args),
                "search" => HandleSearch(args),
                "brief" => HandleBrief(args),
                "cloud" => HandleCloud(args),
                "version" => HandleVersion(),
                "help" or "--help" or "-h" => HandleHelp(),
                _ => UnknownCommand(command)
            };
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static int HandleInit(string[] args)
    {
        var workspacePath = args.Length > 1 ? args[1] : ".engram";

        if (Directory.Exists(workspacePath))
        {
            Console.WriteLine($"Workspace already exists at: {Path.GetFullPath(workspacePath)}");
            return 0;
        }

        Directory.CreateDirectory(workspacePath);
        Directory.CreateDirectory(Path.Combine(workspacePath, "raw"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "wiki"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "logs"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "cache"));
        Directory.CreateDirectory(Path.Combine(workspacePath, "runs"));

        Console.WriteLine($"Engram workspace initialized at: {Path.GetFullPath(workspacePath)}");
        Console.WriteLine("  raw/    — immutable event history");
        Console.WriteLine("  wiki/   — metabolized memory nodes");
        Console.WriteLine("  logs/   — audit and diagnostic logs");
        Console.WriteLine("  cache/  — clean research cache");
        Console.WriteLine("  runs/   — agent run state");
        return 0;
    }

    private static int HandleStatus(string[] args)
    {
        var workspacePath = args.Length > 1 ? args[1] : ".engram";

        if (!Directory.Exists(workspacePath))
        {
            Console.Error.WriteLine("No Engram workspace found. Run 'engram init' first.");
            return 1;
        }

        var config = new EngramConfig();
        var rawCount = Directory.Exists(Path.Combine(workspacePath, "raw"))
            ? Directory.GetFiles(Path.Combine(workspacePath, "raw"), "*.json", SearchOption.AllDirectories).Length
            : 0;
        var wikiCount = Directory.Exists(Path.Combine(workspacePath, "wiki"))
            ? Directory.GetFiles(Path.Combine(workspacePath, "wiki"), "*.md", SearchOption.AllDirectories).Length
            : 0;

        Console.WriteLine("Engram Status");
        Console.WriteLine($"  Workspace:  {Path.GetFullPath(workspacePath)}");
        Console.WriteLine($"  Tier:       {config.Tier}");
        Console.WriteLine($"  Cloud:      {(config.CloudEnabled ? "enabled" : "disabled")}");
        Console.WriteLine($"  Raw events: {rawCount}");
        Console.WriteLine($"  Wiki nodes: {wikiCount}");
        return 0;
    }

    private static int HandleSearch(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: engram search <query>");
            return 1;
        }

        var query = string.Join(" ", args.Skip(1));
        var wikiPath = ".engram/wiki";

        if (!Directory.Exists(wikiPath))
        {
            Console.Error.WriteLine("No workspace found. Run 'engram init' first.");
            return 1;
        }

        var files = Directory.GetFiles(wikiPath, "*.md", SearchOption.AllDirectories);
        var matches = new List<(string file, string snippet)>();

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            if (content.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                var lines = content.Split('\n');
                var matchLine = Array.FindIndex(lines, l => l.Contains(query, StringComparison.OrdinalIgnoreCase));
                var snippet = matchLine >= 0
                    ? string.Join(" ", lines.Skip(Math.Max(0, matchLine - 1)).Take(3))
                    : content[..Math.Min(200, content.Length)];
                matches.Add((Path.GetFileName(file), snippet.Trim()));
            }
        }

        if (matches.Count == 0)
        {
            Console.WriteLine($"No results for: {query}");
            return 0;
        }

        Console.WriteLine($"Found {matches.Count} result(s) for: {query}");
        Console.WriteLine();
        foreach (var (file, snippet) in matches)
        {
            Console.WriteLine($"  {file}");
            Console.WriteLine($"    {snippet}");
            Console.WriteLine();
        }
        return 0;
    }

    private static int HandleBrief(string[] args)
    {
        Console.WriteLine("Briefs require the full Engram.Service to be running.");
        Console.WriteLine("Start the service first, then use the tray widget or Alt+Space.");
        return 0;
    }

    private static int HandleCloud(string[] args)
    {
        var config = new EngramConfig();

        if (args.Length < 2 || args[1] == "status")
        {
            Console.WriteLine("Cloud Status");
            Console.WriteLine($"  Tier:         {config.Tier}");
            Console.WriteLine($"  Cloud enabled: {config.CloudEnabled}");
            Console.WriteLine($"  Daily budget:  ${config.DailyBudgetUsd:F2}");
            Console.WriteLine($"  Monthly budget: ${config.MonthlyBudgetUsd:F2}");
            Console.WriteLine($"  Per-call limit: ${config.PerCallLimitUsd:F2}");
            return 0;
        }

        Console.WriteLine("Usage: engram cloud [status]");
        return 0;
    }

    private static int HandleVersion()
    {
        Console.WriteLine("Engram v1.0.0");
        return 0;
    }

    private static int HandleHelp()
    {
        PrintUsage();
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Engram — local-first semantic memory layer");
        Console.WriteLine();
        Console.WriteLine("Usage: engram <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  init [path]   Initialize a new .engram workspace");
        Console.WriteLine("  status [path] Show workspace status and stats");
        Console.WriteLine("  search <q>    Search wiki memory");
        Console.WriteLine("  brief         Morning/evening brief (requires service)");
        Console.WriteLine("  cloud [status] Show cloud configuration and budget");
        Console.WriteLine("  version       Show version");
        Console.WriteLine("  help          Show this help");
    }
}
