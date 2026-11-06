namespace Engram.Store.Automation;

internal sealed record TextFileOutputBinding(string Path, string Content)
{
    internal bool Matches(AutomationAction action) =>
        action.Type == ActionType.FileWrite && string.IsNullOrWhiteSpace(action.CapabilityId) &&
        string.Equals(action.Value, Path, StringComparison.Ordinal) &&
        string.Equals(action.Target?.Text, Content, StringComparison.Ordinal) &&
        string.IsNullOrWhiteSpace(action.Target?.Selector) &&
        action.Target?.X == null && action.Target?.Y == null && action.Target?.X2 == null && action.Target?.Y2 == null;

    internal static void Bind(AutomationAction action, Func<string, string>? resolveFolder = null)
    {
        var destination = action.Value ?? string.Empty;
        var separator = destination.IndexOf('/');
        if (separator <= 0 || separator == destination.Length - 1)
            throw new InvalidOperationException("Choose Desktop or Documents and a name for the text file.");
        var folderId = destination[..separator].ToLowerInvariant();
        if (folderId is not ("desktop" or "documents"))
            throw new InvalidOperationException("This text-file tool supports Desktop and Documents.");
        var fileName = destination[(separator + 1)..];
        if (fileName.Length > 200 || string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains('/') || fileName.Contains('\\') || fileName.EndsWith('.') || fileName.EndsWith(' '))
            throw new InvalidOperationException("Choose a valid file name without folders or special path characters.");
        var stem = fileName.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))
            throw new InvalidOperationException("That file name is reserved by Windows. Choose another name.");
        var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ("" or ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".yaml" or ".yml" or ".log" or ".html" or ".htm"))
            throw new InvalidOperationException("Use a text format for this output. Office and PDF documents need their document tools.");
        var content = action.Target?.Text;
        if (content == null || content.Length > 100_000 || action.Target?.Selector != null ||
            action.Target?.X != null || action.Target?.Y != null || action.Target?.X2 != null || action.Target?.Y2 != null)
            throw new InvalidOperationException("Provide text content for the file without screen targets.");
        var folder = resolveFolder?.Invoke(folderId) ?? Environment.GetFolderPath(folderId == "desktop"
            ? Environment.SpecialFolder.DesktopDirectory : Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(folder) || !System.IO.Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
            throw new InvalidOperationException($"The {folderId} folder is unavailable. Choose an available location.");
        var path = System.IO.Path.Combine(folder, fileName);
        if (Directory.Exists(path)) throw new InvalidOperationException("A folder already has that name. Choose another file name.");
        action.Type = ActionType.FileWrite;
        action.Value = path;
        action.TextFileOutput = new(path, content);
        action.Description = $"Save {fileName} in {folderId}";
        action.RiskLevel = AutomationRiskLevel.High;
        action.Permission = ActionPermission.Pending;
        if (!File.Exists(path)) AutomationApprovalPolicy.GrantAutomaticExecution(action);
    }
}
