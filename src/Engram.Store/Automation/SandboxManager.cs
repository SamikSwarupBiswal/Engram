using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

public class SandboxManager
{
    private readonly HashSet<string> _allowedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blacklistedCommands = new(StringComparer.OrdinalIgnoreCase);
    private bool _isSimulationMode = true;

    public bool IsSimulationMode
    {
        get => _isSimulationMode;
        set => _isSimulationMode = value;
    }

    public SandboxManager()
    {
        // Add default allowed directories: system temp and current workspace directory
        _allowedDirectories.Add(Path.GetTempPath());
        _allowedDirectories.Add(Environment.CurrentDirectory);

        // Add blacklisted commands/executables
        _blacklistedCommands.Add("rmdir");
        _blacklistedCommands.Add("del");
        _blacklistedCommands.Add("format");
        _blacklistedCommands.Add("sudo");
        _blacklistedCommands.Add("rm");
        _blacklistedCommands.Add("shutdown");
        _blacklistedCommands.Add("mkfs");
        _blacklistedCommands.Add("fdisk");
    }

    public void AddAllowedDirectory(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _allowedDirectories.Add(Path.GetFullPath(path));
    }

    public bool ValidatePathSafety(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var fullPath = Path.GetFullPath(path);

        // Path is safe if it starts with any of the allowed directory paths
        return _allowedDirectories.Any(allowed =>
        {
            var allowedFull = Path.GetFullPath(allowed);
            // Append trailing separator to avoid partial name matches (e.g. C:\temp vs C:\temp2)
            if (!allowedFull.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                allowedFull += Path.DirectorySeparatorChar;
            }
            return fullPath.StartsWith(allowedFull, StringComparison.OrdinalIgnoreCase) || 
                   fullPath.Equals(Path.GetFullPath(allowed), StringComparison.OrdinalIgnoreCase);
        });
    }

    public bool ValidateCommandSafety(string command)
    {
        if (string.IsNullOrEmpty(command)) return true;

        var tokens = command.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return true;

        var firstToken = Path.GetFileNameWithoutExtension(tokens[0]);

        return !_blacklistedCommands.Contains(firstToken);
    }

    public Task<bool> VerifyPlanAsync(ExecutionPlan plan)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));

        // Dry-run plan verification
        foreach (var step in plan.Steps.Values)
        {
            // If action involves writing a file, check the path safety
            if (step.Action.Type == ActionType.Upload || step.Action.Type == ActionType.Download)
            {
                if (!string.IsNullOrEmpty(step.Action.Value) && !ValidatePathSafety(step.Action.Value))
                {
                    return Task.FromResult(false);
                }
            }

            // Verify target selector (basic safety check)
            if (step.Action.Target != null && !string.IsNullOrEmpty(step.Action.Target.Selector))
            {
                if (step.Action.Target.Selector.Contains("script", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(false);
                }
            }
        }

        return Task.FromResult(true);
    }
}
