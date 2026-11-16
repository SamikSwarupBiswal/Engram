using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Engram.Store.Automation;

public sealed record ExcelSalesPoint(string Month, decimal Value);
public sealed class ExcelChartBindingRequest
{
    public ExcelSalesPoint[] SalesData { get; set; } = Array.Empty<ExcelSalesPoint>();
}
public sealed record ExcelChartBinding(string BindingId, IReadOnlyList<ExcelSalesPoint> SalesData, DateTimeOffset BoundAt);
public sealed record ExcelWorkbookBinding(string BindingId, ExcelWorkbookDefinition Definition, DateTimeOffset BoundAt);

public sealed record ExcelFormattingBinding(
    string BindingId, int ProcessId, long WindowHandle, string WorkbookName,
    string WorksheetName, string PreStateSha256, DateTimeOffset BoundAt)
{
    public ExcelWorksheetFormattingDefinition? Definition { get; init; }
}

internal sealed record ExcelFormattingCapture(int ProcessId, long WindowHandle, string WorkbookName,
    string WorksheetName, string PreStateSha256, int FirstRow, int FirstColumn, int RowCount, int ColumnCount);

internal interface IExcelFormattingSession : IDisposable
{
    int ProcessId { get; }
    long WindowHandle { get; }
    void Apply(ExcelFormattingBinding binding, CancellationToken ct);
    IReadOnlyDictionary<string, string> Verify(ExcelFormattingBinding binding, CancellationToken ct);
}

public sealed record ExcelWorkbookWindow(long WindowHandle, int ProcessId, string Title);

public sealed class ExcelCsvCleanupBindingRequest
{
    public string Path { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
}
public sealed record ExcelCsvCleanupBinding(
    string BindingId, string SourcePath, string SourceSha256, string OutputPath, DateTimeOffset BoundAt);

public sealed record ExcelOperationResult(
    string Output, bool Verified, AutomationOwnershipHandle? Ownership,
    IReadOnlyDictionary<string, string> Evidence);

/// <summary>
/// Holds immutable, caller-supplied Excel context and executes only typed operations
/// against the workbook identity that was bound. No prompt text or fixture values are
/// interpreted here.
/// </summary>
public sealed class ExcelAdvancedContextBindingRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ExcelChartBinding> _charts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExcelFormattingBinding> _formats = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExcelCsvCleanupBinding> _csv = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExcelComSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IExcelFormattingSession> _formattingSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _formattingOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _startedFormats = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long?, CancellationToken, ExcelFormattingCapture> _captureFormatting;
    private readonly Func<ExcelFormattingBinding, CancellationToken, IExcelFormattingSession> _openFormatting;
    private readonly Func<ExcelWorkbookBinding, CancellationToken, ExcelComSession> _createWorkbook;
    private int _capturingFormats;
    private readonly Dictionary<string, ExcelWorkbookBinding> _workbooks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _creatingWorkbooks = new(StringComparer.Ordinal);
    private long _workbookBytes;
    private int _workbookCells;

    public ExcelChartBinding? CurrentChart { get; private set; }
    public ExcelFormattingBinding? CurrentFormatting { get; private set; }
    public ExcelCsvCleanupBinding? CurrentCsvCleanup { get; private set; }

    public ExcelAdvancedContextBindingRegistry() : this(ExcelCom.CaptureFormatting,
        (binding, ct) => new ExcelFormattingSession(ExcelCom.OpenBoundWorkbook(binding, ct))) { }

    internal ExcelAdvancedContextBindingRegistry(Func<long?, CancellationToken, ExcelFormattingCapture> captureFormatting,
        Func<ExcelFormattingBinding, CancellationToken, IExcelFormattingSession> openFormatting,
        Func<ExcelWorkbookBinding, CancellationToken, ExcelComSession>? createWorkbook = null)
    {
        _captureFormatting = captureFormatting ?? throw new ArgumentNullException(nameof(captureFormatting));
        _openFormatting = openFormatting ?? throw new ArgumentNullException(nameof(openFormatting));
        _createWorkbook = createWorkbook ?? ExcelCom.CreateWorkbook;
    }

    public ExcelWorkbookBinding BindWorkbook(ExcelWorkbookDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_sync)
        {
            if (_workbooks.Count >= 32 || _workbookCells + definition.Cells.Count > 40_000 ||
                _workbookBytes + definition.EstimatedBytes > 8 * 1024 * 1024)
                throw new InvalidOperationException("Finish or release earlier prepared Excel workbooks before planning more.");
            var binding = new ExcelWorkbookBinding(Guid.NewGuid().ToString("N"), definition, DateTimeOffset.UtcNow);
            _workbooks.Add(binding.BindingId, binding);
            _workbookCells += definition.Cells.Count;
            _workbookBytes += definition.EstimatedBytes;
            return binding;
        }
    }

    /// <summary>Releases only an unused preparation; it never closes a workbook.</summary>
    public bool ReleaseWorkbookBinding(string bindingId)
    {
        lock (_sync)
        {
            if (_sessions.ContainsKey(bindingId) || _creatingWorkbooks.Contains(bindingId)) return false;
            return RemoveWorkbookBinding(bindingId);
        }
    }

    private bool RemoveWorkbookBinding(string bindingId)
    {
        if (!_workbooks.Remove(bindingId, out var binding)) return false;
        _workbookCells -= binding.Definition.Cells.Count;
        _workbookBytes -= binding.Definition.EstimatedBytes;
        _charts.Remove(bindingId);
        if (CurrentChart?.BindingId == bindingId) CurrentChart = null;
        return true;
    }

    public ExcelOperationResult CreateWorkbook(string bindingId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ExcelWorkbookBinding binding;
        lock (_sync)
        {
            binding = Get(_workbooks, bindingId, "workbook");
            if (_sessions.ContainsKey(bindingId) || !_creatingWorkbooks.Add(bindingId))
                throw new InvalidOperationException("This Excel workbook binding has already started.");
        }
        ExcelComSession? session = null;
        try
        {
            session = _createWorkbook(binding, ct);
            ct.ThrowIfCancellationRequested();
            lock (_sync) _sessions.Add(bindingId, session);
            return Result($"Created a new Excel workbook with {binding.Definition.Cells.Count} model-proposed data cells. Read-back verification is pending.",
                false, session, new Dictionary<string, string> { ["bindingId"] = bindingId, ["inputProvenance"] = "model-proposed-data" });
        }
        catch
        {
            ExcelCom.Close(ref session);
            lock (_sync) RemoveWorkbookBinding(bindingId);
            throw;
        }
        finally { lock (_sync) _creatingWorkbooks.Remove(bindingId); }
    }

    public ExcelOperationResult VerifyWorkbook(string bindingId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ExcelWorkbookBinding binding;
        ExcelComSession session;
        lock (_sync)
        {
            binding = Get(_workbooks, bindingId, "workbook");
            session = Get(_sessions, bindingId, "Excel session");
        }
        var evidence = ExcelCom.VerifyWorkbook(binding, session, ct);
        return Result($"Verified Excel worksheet {binding.Definition.WorksheetName}: exact data cell types and values, local SUM formulas and calculated results.",
            true, session, evidence);
    }

    public ExcelChartBinding BindChart(ExcelChartBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SalesData is not { Length: 5 })
            throw new InvalidOperationException("Excel chart binding requires exactly five month/value pairs.");
        var points = request.SalesData.Select(point => point is null
            ? throw new InvalidOperationException("Excel chart data must contain month/value pairs.")
            : new ExcelSalesPoint(RequireCellText(point.Month, "month"), point.Value)).ToArray();
        if (points.Select(point => point.Month).Distinct(StringComparer.OrdinalIgnoreCase).Count() != points.Length)
            throw new InvalidOperationException("Excel chart month labels must be unique.");
        if (points.Any(point => point.Value is < -1_000_000_000m or > 1_000_000_000m))
            throw new InvalidOperationException("Excel chart binding contains an out-of-range value.");
        object?[][] rows = new object?[points.Length + 1][];
        rows[0] = ["Month", "Sales"];
        for (var index = 0; index < points.Length; index++) rows[index + 1] = [points[index].Month, points[index].Value];
        var lastRow = (points.Length + 1).ToString(CultureInfo.InvariantCulture);
        var definition = ExcelWorkbookDefinition.Parse(JsonSerializer.SerializeToElement(new
        {
            tool = "excel.workbook.create", worksheetName = "Sales", startCell = "A1", rows,
            formulas = Array.Empty<object>(),
            charts = new[] { new { type = "column", title = "Sales by Month", seriesName = "Sales",
                categoryRange = "A2:A" + lastRow, valueRange = "B2:B" + lastRow, categoryAxisTitle = "Month", valueAxisTitle = "Sales" } }
        }));
        var workbook = BindWorkbook(definition);
        var binding = new ExcelChartBinding(workbook.BindingId, Array.AsReadOnly(points), workbook.BoundAt);
        lock (_sync) { _charts[binding.BindingId] = binding; CurrentChart = binding; }
        return binding;
    }

    public static IReadOnlyList<ExcelWorkbookWindow> ListWorkbookWindows() => ExcelCom.ListWorkbookWindows();

    public ExcelFormattingBinding BindActiveFormatting(long? windowHandle = null) => BindFormatting(null, windowHandle, default);

    public ExcelFormattingBinding BindActiveFormatting(ExcelWorksheetFormattingDefinition definition,
        long? windowHandle = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return BindFormatting(definition, windowHandle, ct);
    }

    private ExcelFormattingBinding BindFormatting(ExcelWorksheetFormattingDefinition? definition, long? windowHandle, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_formats.Count + _capturingFormats >= 32)
                throw new InvalidOperationException("Finish or release earlier prepared Excel formatting before planning more.");
            _capturingFormats++;
        }
        try
        {
            var captured = _captureFormatting(windowHandle, ct);
            ct.ThrowIfCancellationRequested();
            ExcelCom.ValidateFormattingRegion(captured.FirstRow, captured.FirstColumn, captured.RowCount, captured.ColumnCount);
            definition ??= ExcelWorksheetFormattingDefinition.LegacyDefault(captured.FirstRow);
            ExcelCom.ValidateFormattingHeader(definition, captured.FirstRow, captured.RowCount);
            if (captured.ProcessId <= 0 || captured.WindowHandle <= 0 || string.IsNullOrWhiteSpace(captured.WorkbookName) ||
                string.IsNullOrWhiteSpace(captured.WorksheetName) || captured.PreStateSha256 is not { Length: 64 } ||
                captured.PreStateSha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("Selected Excel formatting capture has invalid identity metadata.");
            var binding = new ExcelFormattingBinding(Guid.NewGuid().ToString("N"), captured.ProcessId, captured.WindowHandle,
                captured.WorkbookName, captured.WorksheetName, captured.PreStateSha256, DateTimeOffset.UtcNow) { Definition = definition };
            lock (_sync) { _formats.Add(binding.BindingId, binding); CurrentFormatting = binding; }
            return binding;
        }
        finally { lock (_sync) _capturingFormats--; }
    }

    /// <summary>Releases an unused formatting preparation without closing the user's workbook.</summary>
    public bool ReleaseFormattingBinding(string bindingId)
    {
        lock (_sync)
        {
            if (_formattingOperations.Contains(bindingId) || _formattingSessions.ContainsKey(bindingId)) return false;
            return RemoveFormattingBinding(bindingId);
        }
    }

    private bool RemoveFormattingBinding(string bindingId)
    {
        if (!_formats.Remove(bindingId)) return false;
        _startedFormats.Remove(bindingId);
        if (CurrentFormatting?.BindingId == bindingId) CurrentFormatting = null;
        return true;
    }

    public ExcelCsvCleanupBinding BindCsvCleanup(ExcelCsvCleanupBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = Path.GetFullPath(request.Path ?? string.Empty);
        if (!File.Exists(source)) throw new FileNotFoundException("Selected CSV file does not exist.", source);
        if (!Path.GetExtension(source).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Excel cleanup binding accepts only a selected CSV file.");
        var output = string.IsNullOrWhiteSpace(request.OutputPath)
            ? NextAvailablePath(Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source) + "-cleaned.xlsx"))
            : Path.GetFullPath(request.OutputPath);
        if (!Path.GetExtension(output).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cleaned Excel output must be a distinct .xlsx workbook.");
        if (source.Equals(output, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cleaned Excel workbook must not overwrite the selected original CSV.");
        EnsureNewWorkbookDestination(output);
        var snapshot = ReadCsvSource(source);
        CsvDocument.Parse(snapshot.Text);
        var binding = new ExcelCsvCleanupBinding(Guid.NewGuid().ToString("N"), source, snapshot.Sha256, output, DateTimeOffset.UtcNow);
        lock (_sync) { _csv[binding.BindingId] = binding; CurrentCsvCleanup = binding; }
        return binding;
    }

    public ExcelOperationResult CreateChart(string bindingId)
    {
        lock (_sync) Get(_charts, bindingId, "chart");
        return CreateWorkbook(bindingId) with { Output = "Created the bound sales table and column chart. Read-back verification is pending." };
    }

    public ExcelOperationResult VerifyChart(string bindingId)
    {
        lock (_sync) Get(_charts, bindingId, "chart");
        return VerifyWorkbook(bindingId) with { Output = "Verified the bound sales table, literal category labels, and column chart against the prepared workbook." };
    }

    public ExcelOperationResult ApplyFormatting(string bindingId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ExcelFormattingBinding binding;
        lock (_sync)
        {
            binding = Get(_formats, bindingId, "formatting");
            if (!_startedFormats.Add(bindingId)) throw new InvalidOperationException("This Excel formatting binding has already started.");
            _formattingOperations.Add(bindingId);
        }
        IExcelFormattingSession? session = null;
        try
        {
            session = _openFormatting(binding, ct);
            ct.ThrowIfCancellationRequested();
            EnsureFormattingSession(binding, session);
            session.Apply(binding, ct);
            ct.ThrowIfCancellationRequested();
            lock (_sync) _formattingSessions.Add(bindingId, session);
            return FormattingResult($"Applied requested formatting to bound workbook {binding.WorkbookName}/{binding.WorksheetName}. Read-back verification is pending.",
                false, binding, new Dictionary<string, string> { ["preStateSha256"] = binding.PreStateSha256 });
        }
        catch { session?.Dispose(); throw; }
        finally { lock (_sync) _formattingOperations.Remove(bindingId); }
    }

    public ExcelOperationResult VerifyFormatting(string bindingId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ExcelFormattingBinding binding; IExcelFormattingSession session;
        lock (_sync)
        {
            binding = Get(_formats, bindingId, "formatting");
            if (_formattingOperations.Contains(bindingId)) throw new InvalidOperationException("This Excel formatting binding is already active.");
            session = Get(_formattingSessions, bindingId, "formatting session");
            _formattingOperations.Add(bindingId);
        }
        try
        {
            EnsureFormattingSession(binding, session);
            var evidence = session.Verify(binding, ct);
            ct.ThrowIfCancellationRequested();
            return FormattingResult($"Verified the requested worksheet formatting and unchanged cell content in {binding.WorkbookName}/{binding.WorksheetName}.", true, binding, evidence);
        }
        catch
        {
            lock (_sync) _formattingSessions.Remove(bindingId);
            session.Dispose();
            throw;
        }
        finally { lock (_sync) _formattingOperations.Remove(bindingId); }
    }

    private static void EnsureFormattingSession(ExcelFormattingBinding binding, IExcelFormattingSession session)
    {
        if (session.ProcessId != binding.ProcessId || session.WindowHandle != binding.WindowHandle)
            throw new InvalidDataException("Selected Excel formatting session changed process or workbook window.");
    }

    private static ExcelOperationResult FormattingResult(string output, bool verified, ExcelFormattingBinding binding,
        IReadOnlyDictionary<string, string> evidence) => new(output, verified,
        new AutomationOwnershipHandle("excel-workbook", binding.BindingId, binding.ProcessId, binding.WindowHandle), evidence);

    public ExcelOperationResult CleanCsv(string bindingId)
    {
        var binding = Get(_csv, bindingId, "CSV cleanup");
        EnsureNewWorkbookDestination(binding.OutputPath);
        var parsed = CsvDocument.Parse(ReadCsvSource(binding.SourcePath, binding.SourceSha256).Text);
        var cleaned = parsed.CleanAndSortByDateDescending();
        ExcelComSession? session = null;
        string? publishedSha256 = null;
        try
        {
            publishedSha256 = PublishNewCsvWorkbook(binding.OutputPath, stagingPath =>
            {
                ExcelComSession? staged = null;
                try
                {
                    staged = ExcelCom.CreateCleanedWorkbook(binding.BindingId, stagingPath, cleaned);
                    ExcelCom.VerifyCleanedWorkbook(binding with { OutputPath = stagingPath }, staged, cleaned);
                }
                finally { ExcelCom.Close(ref staged); }

                ReadCsvSource(binding.SourcePath, binding.SourceSha256);
            });
            EnsurePublishedWorkbookIdentity(binding.OutputPath, publishedSha256);
            session = ExcelCom.OpenCleanedWorkbook(binding.BindingId, binding.OutputPath) with { PublishedSha256 = publishedSha256 };
            ReadCsvSource(binding.SourcePath, binding.SourceSha256);
            var workbookEvidence = ExcelCom.VerifyCleanedWorkbook(binding, session, cleaned);
            EnsurePublishedWorkbookIdentity(binding.OutputPath, publishedSha256);
            var evidence = workbookEvidence.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            evidence["sourcePath"] = binding.SourcePath;
            evidence["sourceSha256"] = binding.SourceSha256;
            evidence["outputSha256"] = publishedSha256;
            evidence["outputSaved"] = "true";
            evidence["originalUnchanged"] = "true";
            evidence["verification"] = "trimmed;deduplicated;date-descending;exact-cell-matrix;original-hash-unchanged";
            lock (_sync) _sessions[bindingId] = session;
            return Result($"Created visible cleaned .xlsx workbook at {binding.OutputPath} with {evidence["rowCount"]} data rows; removed canonical duplicates, trimmed every field, sorted Date descending, and preserved original CSV SHA-256 {binding.SourceSha256}.", true, session, evidence);
        }
        catch (Exception ex)
        {
            ExcelCom.Close(ref session);
            if (publishedSha256 != null)
                throw new InvalidOperationException($"A workbook was published to '{binding.OutputPath}', but final verification failed: {ex.Message}", ex);
            throw;
        }
    }

    public ExcelOperationResult VerifyCsv(string bindingId)
    {
        var binding = Get(_csv, bindingId, "CSV cleanup");
        if (!File.Exists(binding.OutputPath)) throw new FileNotFoundException("Cleaned Excel workbook output is missing.", binding.OutputPath);
        var expected = CsvDocument.Parse(ReadCsvSource(binding.SourcePath, binding.SourceSha256).Text).CleanAndSortByDateDescending();
        var session = Get(_sessions, bindingId, "Excel session");
        var publishedSha256 = session.PublishedSha256 ?? throw new InvalidDataException("Cleaned workbook has no publication fingerprint.");
        var workbookEvidence = ExcelCom.VerifyCleanedWorkbook(binding, session, expected);
        EnsurePublishedWorkbookIdentity(binding.OutputPath, publishedSha256);
        var evidence = workbookEvidence.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        evidence["originalUnchanged"] = "true";
        evidence["sourceSha256"] = binding.SourceSha256;
        evidence["outputSha256"] = publishedSha256;
        evidence["outputSaved"] = "true";
        evidence["verification"] = "trimmed;deduplicated;date-descending;exact-cell-matrix;original-hash-unchanged";
        return Result($"Verified visible cleaned .xlsx workbook {binding.OutputPath}: exact {evidence["rowCount"]}-row by {evidence["columnCount"]}-column canonical cell matrix, duplicate-free trimmed fields in Date-descending order, and unchanged original SHA-256 {binding.SourceSha256}.", true,
            session, evidence);
    }

    public bool Cleanup(string bindingId)
    {
        IExcelFormattingSession? formattingSession;
        bool formattingFound;
        lock (_sync)
        {
            if (_formattingOperations.Contains(bindingId)) return false;
            formattingFound = _formats.ContainsKey(bindingId);
            if (formattingFound)
            {
                _formattingSessions.Remove(bindingId, out formattingSession);
                RemoveFormattingBinding(bindingId);
            }
            else formattingSession = null;
        }
        if (formattingFound) { formattingSession?.Dispose(); return true; }
        ExcelComSession? session;
        lock (_sync) { if (!_sessions.Remove(bindingId, out session)) return false; }
        try { ExcelCom.Close(ref session); }
        finally { lock (_sync) RemoveWorkbookBinding(bindingId); }
        return true;
    }

    private static ExcelOperationResult Result(string output, bool verified, ExcelComSession session, IReadOnlyDictionary<string, string> evidence) =>
        new(output, verified, new AutomationOwnershipHandle("excel-workbook", session.BindingId, session.ProcessId, session.WindowHandle, session.Path), evidence);
    private static T Get<T>(Dictionary<string, T> map, string id, string kind)
    {
        if (id.Length != 32 || id.Any(character => !Uri.IsHexDigit(character)) || !map.TryGetValue(id, out var value))
            throw new InvalidOperationException($"Unknown or invalid {kind} binding '{id}'.");
        return value;
    }
    private static string RequireCellText(string? value, string name)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length is 0 or > 128 || text.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new InvalidOperationException($"Excel {name} must be non-empty safe cell text.");
        return text;
    }
    private static string NextAvailablePath(string candidate)
    {
        if (!File.Exists(candidate)) return candidate;
        var directory = Path.GetDirectoryName(candidate)!; var stem = Path.GetFileNameWithoutExtension(candidate); var extension = Path.GetExtension(candidate);
        for (var index = 2; index < 10_000; index++) { var path = Path.Combine(directory, $"{stem}-{index}{extension}"); if (!File.Exists(path)) return path; }
        throw new IOException("Could not allocate a collision-free cleaned workbook output path.");
    }
    internal static string PublishNewCsvWorkbook(string outputPath, Action<string> writeStagedWorkbook)
    {
        EnsureNewWorkbookDestination(outputPath);
        var directory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(directory);
        var stagingPath = Path.Combine(directory, $".engram-csv-{Guid.NewGuid():N}.xlsx");
        var ownsStagingFile = false;
        try
        {
            // Excel can replace this reservation during SaveAs. The final destination
            // is never passed to SaveAs or to failure cleanup.
            using (new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                ownsStagingFile = true;
            writeStagedWorkbook(stagingPath);
            EnsureNoReparsePoints(stagingPath);
            var sha256 = Sha256File(stagingPath);
            EnsureNoReparsePoints(outputPath);
            File.Move(stagingPath, outputPath, overwrite: false);
            ownsStagingFile = false;
            return sha256;
        }
        finally
        {
            if (ownsStagingFile) File.Delete(stagingPath);
        }
    }

    private static void EnsureNewWorkbookDestination(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("Cleaned Excel workbook output path already exists.");
        EnsureNoReparsePoints(path);
    }

    private static void EnsurePublishedWorkbookIdentity(string path, string sha256)
    {
        EnsureNoReparsePoints(path);
        if (!Sha256File(path, FileShare.ReadWrite).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The published workbook differs from the verified staged workbook.");
    }

    private static void EnsureNoReparsePoints(string path)
    {
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Cleaned workbook paths must not contain symbolic links or reparse points.");
    }

    private static (string Text, string Sha256) ReadCsvSource(string path, string? expectedSha256 = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = stream.Length;
        if (length > CsvDocument.MaximumSourceBytes)
            throw new InvalidDataException("Selected CSV exceeds the 4 MiB source-file limit.");
        var bytes = new byte[checked((int)length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("Selected CSV grew while it was being read.");
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (expectedSha256 != null && !sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selected CSV changed after it was bound; no further cleanup was performed.");
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return (reader.ReadToEnd(), sha256);
    }

    internal static string Sha256File(string path, FileShare share = FileShare.Read)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, share);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal sealed record ExcelComSession(
    object Application,
    object Workbook,
    object Worksheet,
    string BindingId,
    int ProcessId,
    long WindowHandle,
    string? Path,
    bool OwnsApplication,
    string? PublishedSha256 = null);

internal sealed class ExcelFormattingSession : IExcelFormattingSession
{
    private ExcelComSession? _session;
    public int ProcessId { get; }
    public long WindowHandle { get; }
    public ExcelFormattingSession(ExcelComSession session)
    {
        if (session.OwnsApplication) throw new InvalidOperationException("Formatting must use an unowned user workbook session.");
        _session = session; ProcessId = session.ProcessId; WindowHandle = session.WindowHandle;
    }
    public void Apply(ExcelFormattingBinding binding, CancellationToken ct) =>
        ExcelCom.ApplyFormatting(binding, _session ?? throw new ObjectDisposedException(nameof(ExcelFormattingSession)), ct);
    public IReadOnlyDictionary<string, string> Verify(ExcelFormattingBinding binding, CancellationToken ct) =>
        ExcelCom.VerifyFormatting(binding, _session ?? throw new ObjectDisposedException(nameof(ExcelFormattingSession)), ct);
    public void Dispose() => ExcelCom.Close(ref _session);
}

internal static class ExcelCom
{
    private const int XlColumnClustered = 51;

    public static ExcelComSession CreateWorkbook(ExcelWorkbookBinding binding, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var app = CreateExcel(requireEmpty: true);
        object? workbooks = null; object? worksheets = null; object? workbook = null; object? worksheet = null;
        try
        {
            workbooks = ((dynamic)app).Workbooks;
            workbook = ((dynamic)workbooks).Add(-4167); // xlWBATWorksheet, never a user template.
            worksheets = ((dynamic)workbook).Worksheets;
            worksheet = ((dynamic)worksheets)[1];
            ((dynamic)worksheet).Name = binding.Definition.WorksheetName;
            WriteWorkbookDataCells(worksheet, binding.Definition.Cells, ct);
            foreach (var expected in binding.Definition.Formulas)
            {
                ct.ThrowIfCancellationRequested();
                object? cell = null;
                try { cell = ((dynamic)worksheet).Range[expected.Cell]; ((dynamic)cell).Formula = expected.Formula; }
                finally { Release(cell); }
            }
            foreach (var chart in binding.Definition.Charts)
            {
                ct.ThrowIfCancellationRequested();
                CreateWorkbookChart(worksheet, chart);
            }
            ((dynamic)worksheet).Calculate();
            ((dynamic)app).Caption = $"Engram {binding.BindingId}";
            ct.ThrowIfCancellationRequested();
            return Session(app, workbook, worksheet, binding.BindingId, null);
        }
        catch
        {
            ExcelComSession? failed = new(app, workbook!, worksheet!, binding.BindingId, 0, 0, null, true);
            Close(ref failed);
            throw;
        }
        finally { Release(worksheets); Release(workbooks); }
    }

    internal static void WriteWorkbookDataCells(object worksheet, IReadOnlyList<ExcelWorkbookCell> cells, CancellationToken ct)
    {
        foreach (var expected in cells)
        {
            ct.ThrowIfCancellationRequested();
            object? cell = null;
            try
            {
                cell = ((dynamic)worksheet).Range[expected.Address];
                if (expected.Kind == ExcelWorkbookCellKind.Text) ((dynamic)cell).NumberFormat = "@";
                ((dynamic)cell).Value2 = expected.Value;
            }
            finally { Release(cell); }
        }
    }

    public static IReadOnlyDictionary<string, string> VerifyWorkbook(ExcelWorkbookBinding binding,
        ExcelComSession session, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!session.OwnsApplication || session.BindingId != binding.BindingId)
            throw new InvalidDataException("The Excel session does not own this workbook binding.");
        object? windows = null; object? window = null;
        try
        {
            windows = ((dynamic)session.Workbook).Windows;
            window = ((dynamic)windows)[1];
            EnsureBoundWorkbookWindow(window, session.WindowHandle, session.ProcessId, session.Workbook, session.Worksheet);
            EnsureWorkbookPresentation(
                Convert.ToBoolean(((dynamic)session.Application).Visible, CultureInfo.InvariantCulture),
                Convert.ToBoolean(((dynamic)window).Visible, CultureInfo.InvariantCulture),
                Convert.ToString(((dynamic)session.Worksheet).Name, CultureInfo.InvariantCulture),
                binding.Definition.WorksheetName);
            foreach (var expected in binding.Definition.Cells)
            {
                ct.ThrowIfCancellationRequested();
                object? cell = null;
                try
                {
                    cell = ((dynamic)session.Worksheet).Range[expected.Address];
                    ValidateWorkbookCell(expected, ((dynamic)cell).Value2,
                        Convert.ToBoolean(((dynamic)cell).HasFormula, CultureInfo.InvariantCulture));
                }
                finally { Release(cell); }
            }
            foreach (var expected in binding.Definition.Formulas)
            {
                ct.ThrowIfCancellationRequested();
                object? cell = null;
                try
                {
                    cell = ((dynamic)session.Worksheet).Range[expected.Cell];
                    object? formulaValue = ((dynamic)cell).Formula;
                    var actualFormula = Convert.ToString(formulaValue, CultureInfo.InvariantCulture);
                    object? actualValue = ((dynamic)cell).Value2;
                    bool hasFormula = Convert.ToBoolean(((dynamic)cell).HasFormula, CultureInfo.InvariantCulture);
                    if (!hasFormula || actualFormula != expected.Formula ||
                        !TryWorkbookNumber(actualValue, out double actual) || !ExcelWorkbookDefinition.NumbersMatch(expected.ExpectedValue, actual))
                        throw new InvalidDataException($"Excel formula or calculated result differs at {expected.Cell}.");
                }
                finally { Release(cell); }
            }
            ct.ThrowIfCancellationRequested();
            VerifyWorkbookCharts(session.Worksheet, binding.Definition.Charts, ct);
            EnsureBoundWorkbookWindow(window, session.WindowHandle, session.ProcessId, session.Workbook, session.Worksheet);
            return new Dictionary<string, string>
            {
                ["bindingId"] = binding.BindingId, ["worksheetName"] = binding.Definition.WorksheetName,
                ["startCell"] = binding.Definition.StartCell,
                ["rowCount"] = binding.Definition.RowCount.ToString(CultureInfo.InvariantCulture),
                ["columnCount"] = binding.Definition.ColumnCount.ToString(CultureInfo.InvariantCulture),
                ["verifiedDataCells"] = binding.Definition.Cells.Count.ToString(CultureInfo.InvariantCulture),
                ["verifiedFormulas"] = binding.Definition.Formulas.Count.ToString(CultureInfo.InvariantCulture),
                ["verifiedCharts"] = binding.Definition.Charts.Count.ToString(CultureInfo.InvariantCulture),
                ["inputProvenance"] = "model-proposed-data",
                ["verification"] = "owned-window;exact-cell-types-and-values;literal-text;constructed-local-sum;independent-result",
                ["numericTolerance"] = "input-cells:exact-double;sum-results:abs(expected)*2e-15;zero:exact", ["numericInputPrecision"] = "15-significant-decimal-digits",
                ["saved"] = "false"
            };
        }
        finally { Release(window); Release(windows); }
    }

    private static void CreateWorkbookChart(object worksheet, ExcelWorkbookChart expected)
    {
        object? charts = null; object? chartObject = null; object? chart = null;
        object? seriesCollection = null; object? series = null; object? categories = null; object? values = null;
        try
        {
            charts = ((dynamic)worksheet).ChartObjects();
            chartObject = ((dynamic)charts).Add(300, 20, 520, 320);
            chart = ((dynamic)chartObject).Chart;
            ((dynamic)chart).ChartType = XlColumnClustered;
            seriesCollection = ((dynamic)chart).SeriesCollection();
            // A blank chart should have no inferred series. Remove any default
            // series before binding precisely the model-selected local ranges.
            var inferredCount = Convert.ToInt32(((dynamic)seriesCollection).Count, CultureInfo.InvariantCulture);
            if (inferredCount is < 0 or > 256) throw new InvalidDataException("Unexpected default Excel chart series count.");
            for (var remaining = inferredCount; remaining > 0; remaining--)
            {
                object? inferred = null;
                try { inferred = ((dynamic)seriesCollection).Item(1); ((dynamic)inferred).Delete(); }
                finally { Release(inferred); }
            }
            series = ((dynamic)seriesCollection).NewSeries();
            categories = ((dynamic)worksheet).Range[expected.CategoryRange];
            values = ((dynamic)worksheet).Range[expected.ValueRange];
            ((dynamic)series).XValues = categories;
            ((dynamic)series).Values = values;
            ((dynamic)series).Name = expected.SeriesName;
            ((dynamic)chart).HasTitle = true;
            object? title = null;
            try { title = ((dynamic)chart).ChartTitle; ((dynamic)title).Text = expected.Title; }
            finally { Release(title); }
            SetWorkbookChartAxis(chart, 1, expected.CategoryAxisTitle);
            SetWorkbookChartAxis(chart, 2, expected.ValueAxisTitle);
        }
        finally { Release(values); Release(categories); Release(series); Release(seriesCollection); Release(chart); Release(chartObject); Release(charts); }
    }

    private static void SetWorkbookChartAxis(object chart, int kind, string titleText)
    {
        object? axis = null; object? title = null;
        try
        {
            axis = ((dynamic)chart).Axes(kind, 1);
            ((dynamic)axis).HasTitle = true;
            title = ((dynamic)axis).AxisTitle;
            ((dynamic)title).Text = titleText;
        }
        finally { Release(title); Release(axis); }
    }

    private static void VerifyWorkbookCharts(object worksheet, IReadOnlyList<ExcelWorkbookChart> expected, CancellationToken ct)
    {
        object? charts = null;
        try
        {
            charts = ((dynamic)worksheet).ChartObjects();
            if (Convert.ToInt32(((dynamic)charts).Count, CultureInfo.InvariantCulture) != expected.Count)
                throw new InvalidDataException("Excel chart count differs from the proposed workbook.");
            for (var i = 0; i < expected.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                object? chartObject = null; object? chart = null; object? title = null; object? collection = null; object? series = null;
                try
                {
                    chartObject = ((dynamic)charts).Item(i + 1); chart = ((dynamic)chartObject).Chart;
                    if (!Convert.ToBoolean(((dynamic)chartObject).Visible, CultureInfo.InvariantCulture) ||
                        Convert.ToInt32(((dynamic)chart).ChartType, CultureInfo.InvariantCulture) != XlColumnClustered ||
                        !Convert.ToBoolean(((dynamic)chart).HasTitle, CultureInfo.InvariantCulture))
                        throw new InvalidDataException("The proposed visible column chart is missing.");
                    title = ((dynamic)chart).ChartTitle;
                    if (Convert.ToString(((dynamic)title).Text, CultureInfo.InvariantCulture) != expected[i].Title)
                        throw new InvalidDataException("Excel chart title differs from the proposed title.");
                    collection = ((dynamic)chart).SeriesCollection();
                    if (Convert.ToInt32(((dynamic)collection).Count, CultureInfo.InvariantCulture) != 1)
                        throw new InvalidDataException("Excel chart must contain exactly its proposed series.");
                    series = ((dynamic)collection).Item(1);
                    ValidateWorkbookChartSeries(expected[i], Convert.ToString(((dynamic)series).Name, CultureInfo.InvariantCulture),
                        ((dynamic)series).XValues, ((dynamic)series).Values);
                    VerifyWorkbookChartAxis(chart, 1, expected[i].CategoryAxisTitle);
                    VerifyWorkbookChartAxis(chart, 2, expected[i].ValueAxisTitle);
                }
                finally { Release(series); Release(collection); Release(title); Release(chart); Release(chartObject); }
            }
        }
        finally { Release(charts); }
    }

    internal static void ValidateWorkbookChartSeries(ExcelWorkbookChart expected, string? name, object? categories, object? values)
    {
        static object?[] Elements(object? value, int expectedCount)
        {
            if (value is not Array array) return [value];
            if (array.Length != expectedCount) throw new InvalidDataException("Excel chart point count differs from the proposed data.");
            return array.Cast<object?>().ToArray();
        }
        var actualCategories = Elements(categories, expected.Categories.Count); var actualValues = Elements(values, expected.Values.Count);
        if (name != expected.SeriesName || actualCategories.Length != expected.Categories.Count || actualValues.Length != expected.Values.Count)
            throw new InvalidDataException("Excel chart series name or point count differs from the proposed data.");
        for (var i = 0; i < actualCategories.Length; i++)
            if (actualCategories[i] is not string label || label != expected.Categories[i] ||
                !TryWorkbookNumber(actualValues[i], out var number) || number != expected.Values[i])
                throw new InvalidDataException("Excel chart categories or plotted values differ from the proposed data.");
    }

    private static void VerifyWorkbookChartAxis(object chart, int kind, string expected)
    {
        object? axis = null; object? title = null;
        try
        {
            axis = ((dynamic)chart).Axes(kind, 1);
            if (!Convert.ToBoolean(((dynamic)axis).HasTitle, CultureInfo.InvariantCulture))
                throw new InvalidDataException("Excel chart axis title is missing.");
            title = ((dynamic)axis).AxisTitle;
            if (Convert.ToString(((dynamic)title).Text, CultureInfo.InvariantCulture) != expected)
                throw new InvalidDataException("Excel chart axis title differs from the proposed label.");
        }
        finally { Release(title); Release(axis); }
    }

    internal static void EnsureWorkbookPresentation(bool applicationVisible, bool windowVisible,
        string? worksheetName, string expectedName)
    {
        if (!applicationVisible) throw new InvalidDataException("The owned Excel application is not visible.");
        if (!windowVisible) throw new InvalidDataException("The owned Excel workbook window is not visible.");
        if (worksheetName != expectedName)
            throw new InvalidDataException("The owned Excel worksheet name differs from the proposed worksheet name.");
    }

    internal static void ValidateWorkbookCell(ExcelWorkbookCell expected, object? actual, bool hasFormula)
    {
        var matches = !hasFormula && (expected.Kind switch
        {
            ExcelWorkbookCellKind.Blank => actual == null,
            ExcelWorkbookCellKind.Text => actual is string text && text == (string)expected.Value!,
            ExcelWorkbookCellKind.Boolean => actual is bool value && value == (bool)expected.Value!,
            ExcelWorkbookCellKind.Number => TryWorkbookNumber(actual, out var number) &&
                (double)expected.Value! == number,
            _ => false
        });
        if (!matches) throw new InvalidDataException($"Excel data type, value or formula state differs at {expected.Address}.");
    }

    private static bool TryWorkbookNumber(object? value, out double number)
    {
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(number);
        }
        number = 0;
        return false;
    }
    internal static ExcelFormattingCapture CaptureFormatting(long? requestedWindowHandle, CancellationToken ct)
    {
        var captured = CaptureActiveWorkbook(requestedWindowHandle, ct);
        try
        {
            var region = ReadFormattingRegion(captured.Worksheet, ct);
            return new(captured.ProcessId, captured.WindowHandle, captured.WorkbookName, captured.WorksheetName,
                captured.Fingerprint, region.FirstRow, region.FirstColumn, region.RowCount, region.ColumnCount);
        }
        finally { Release(captured.Worksheet); Release(captured.Workbook); Release(captured.Application); }
    }

    public static (object Application, object Workbook, object Worksheet, int ProcessId, long WindowHandle, string WorkbookName, string WorksheetName, string Fingerprint) CaptureActiveWorkbook(long? requestedWindowHandle = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Active Excel workbook binding requires Windows.");
        var selected = SelectWorkbookWindow(ListWorkbookWindows(), GetForegroundWindow().ToInt64(), requestedWindowHandle);
        object? nativeWindow = null; object? app = null; object? workbook = null; object? worksheet = null;
        try
        {
            nativeWindow = GetWorkbookWindow(selected.WindowHandle, selected.ProcessId);
            worksheet = ((dynamic)nativeWindow).ActiveSheet ?? throw new InvalidOperationException("Selected Excel window has no active worksheet to bind.");
            workbook = ((dynamic)worksheet).Parent ?? throw new InvalidOperationException("Selected Excel worksheet has no workbook.");
            app = ((dynamic)nativeWindow).Application ?? throw new InvalidOperationException("Selected Excel window has no application.");
            var fingerprint = Fingerprint(worksheet, ct);
            ct.ThrowIfCancellationRequested();
            EnsureBoundWorkbookWindow(nativeWindow, selected.WindowHandle, selected.ProcessId, workbook, worksheet);
            return (app, workbook, worksheet, selected.ProcessId, selected.WindowHandle,
                Convert.ToString(((dynamic)workbook).Name, CultureInfo.InvariantCulture)!,
                Convert.ToString(((dynamic)worksheet).Name, CultureInfo.InvariantCulture)!, fingerprint);
        }
        catch { Release(worksheet); Release(workbook); Release(app); throw; }
        finally { Release(nativeWindow); }
    }

    public static ExcelComSession OpenBoundWorkbook(ExcelFormattingBinding binding, CancellationToken ct = default)
    {
        var captured = CaptureActiveWorkbook(binding.WindowHandle, ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (captured.ProcessId != binding.ProcessId || captured.WindowHandle != binding.WindowHandle || captured.WorkbookName != binding.WorkbookName || captured.WorksheetName != binding.WorksheetName || captured.Fingerprint != binding.PreStateSha256)
                throw new InvalidDataException("Active Excel workbook identity or content changed after binding.");
            // Engram owns only these references, never the user's workbook or process.
            return new ExcelComSession(captured.Application, captured.Workbook, captured.Worksheet, binding.BindingId, captured.ProcessId, captured.WindowHandle, null, false);
        }
        catch { Release(captured.Worksheet); Release(captured.Workbook); Release(captured.Application); throw; }
    }

    public static void ApplyFormatting(ExcelFormattingBinding binding, ExcelComSession session, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        object? window = null;
        try
        {
            window = GetWorkbookWindow(session.WindowHandle, session.ProcessId);
            ApplyFormattingCore(binding, session.Worksheet, window,
                () => EnsureBoundWorkbookWindow(window, session.WindowHandle, session.ProcessId, session.Workbook, session.Worksheet),
                () => Fingerprint(session.Worksheet, ct), CultureInfo.CurrentCulture, ct);
        }
        finally { Release(window); }
    }

    public static IReadOnlyDictionary<string, string> VerifyFormatting(ExcelFormattingBinding binding, ExcelComSession session, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        object? window = null;
        try
        {
            window = GetWorkbookWindow(session.WindowHandle, session.ProcessId);
            return VerifyFormattingCore(binding, session.Worksheet, window,
                () => EnsureBoundWorkbookWindow(window, session.WindowHandle, session.ProcessId, session.Workbook, session.Worksheet),
                () => Fingerprint(session.Worksheet, ct), CultureInfo.CurrentCulture, ct);
        }
        finally { Release(window); }
    }

    // The production entry points always supply native identity and typed-content
    // checks. This narrow seam permits managed tests without allocating Excel.
    internal static void ApplyFormattingCore(ExcelFormattingBinding binding, object worksheet, object window,
        Action ensureIdentity, Func<string> fingerprint, CultureInfo culture, CancellationToken ct)
    {
        EnsureFormattingUnchanged(binding, ensureIdentity, fingerprint, ct);
        var plan = PreflightFormatting(binding, worksheet, culture, ct);
        void BeforeMutation() => EnsureFormattingUnchanged(binding, ensureIdentity, fingerprint, ct);
        if (plan.Definition.BoldHeader)
        {
            object? header = null; object? font = null;
            try
            {
                header = FormattingRange(worksheet, plan.Definition.HeaderRow, plan.Region.FirstColumn,
                    plan.Definition.HeaderRow, plan.Region.LastColumn);
                font = ((dynamic)header).Font;
                BeforeMutation(); ((dynamic)font).Bold = true;
            }
            finally { Release(font); Release(header); }
        }
        if (plan.Region.LastRow > plan.Definition.HeaderRow)
            foreach (var column in plan.CurrencyColumns)
            {
                object? range = null;
                try
                {
                    range = FormattingRange(worksheet, plan.Definition.HeaderRow + 1, column, plan.Region.LastRow, column);
                    BeforeMutation(); ((dynamic)range).NumberFormat = plan.NumberFormat;
                }
                finally { Release(range); }
            }
        if (plan.Definition.FreezeHeader)
        {
            BeforeMutation(); ((dynamic)window).FreezePanes = false;
            BeforeMutation(); ((dynamic)window).SplitColumn = 0;
            BeforeMutation(); ((dynamic)window).SplitRow = plan.Definition.HeaderRow;
            BeforeMutation(); ((dynamic)window).FreezePanes = true;
        }
        // Fit final rendered currency and header text only after other formatting.
        if (plan.Definition.AutoFitColumns) AutoFitFormatting(worksheet, BeforeMutation, ct);
        EnsureFormattingUnchanged(binding, ensureIdentity, fingerprint, ct);
    }

    internal static IReadOnlyDictionary<string, string> VerifyFormattingCore(ExcelFormattingBinding binding,
        object worksheet, object window, Action ensureIdentity, Func<string> fingerprint, CultureInfo culture, CancellationToken ct)
    {
        EnsureFormattingUnchanged(binding, ensureIdentity, fingerprint, ct);
        var plan = PreflightFormatting(binding, worksheet, culture, ct);
        var evidence = new Dictionary<string, string>
        {
            ["contentSha256"] = binding.PreStateSha256,
            ["verification"] = "bound-window;exact-requested-formatting;unchanged-typed-cell-content"
        };
        if (plan.Definition.BoldHeader)
        {
            object? header = null; object? font = null;
            try
            {
                header = FormattingRange(worksheet, plan.Definition.HeaderRow, plan.Region.FirstColumn,
                    plan.Definition.HeaderRow, plan.Region.LastColumn);
                font = ((dynamic)header).Font;
                object? bold = ((dynamic)font).Bold;
                if (bold is not bool actual || !actual) throw new InvalidDataException("The selected header span is not entirely bold.");
                evidence["headerBold"] = "true";
            }
            finally { Release(font); Release(header); }
        }
        if (plan.Definition.FreezeHeader)
        {
            ct.ThrowIfCancellationRequested(); ensureIdentity();
            if (!Convert.ToBoolean(((dynamic)window).FreezePanes, CultureInfo.InvariantCulture) ||
                Convert.ToInt32(((dynamic)window).SplitRow, CultureInfo.InvariantCulture) != plan.Definition.HeaderRow ||
                Convert.ToInt32(((dynamic)window).SplitColumn, CultureInfo.InvariantCulture) != 0)
                throw new InvalidDataException("The selected header row is not frozen with an unsplit column pane.");
            evidence["freezeHeader"] = "true";
        }
        if (plan.CurrencyColumns.Count > 0)
        {
            foreach (var column in plan.CurrencyColumns)
            {
                ct.ThrowIfCancellationRequested();
                if (plan.Region.LastRow <= plan.Definition.HeaderRow) continue;
                object? range = null;
                try
                {
                    range = FormattingRange(worksheet, plan.Definition.HeaderRow + 1, column, plan.Region.LastRow, column);
                    var format = Convert.ToString(((dynamic)range).NumberFormat, CultureInfo.InvariantCulture);
                    if (format != plan.NumberFormat) throw new InvalidDataException("A selected currency data range has a different number format.");
                }
                finally { Release(range); }
                VisitCurrencyCells(worksheet, plan, column, (value, cell) =>
                    ValidateCurrencyDisplay(value, Convert.ToString(((dynamic)cell).Text, CultureInfo.InvariantCulture), plan.CurrencySymbol), ct);
            }
            evidence["currencyNumberFormat"] = plan.NumberFormat;
            evidence["currencyDisplayVerified"] = "true";
            evidence["currencyColumns"] = string.Join(",", plan.CurrencyColumns);
        }
        if (plan.Definition.AutoFitColumns)
        {
            var before = FormattingWidths(worksheet, plan.Region, ct);
            EnsureFormattingUnchanged(binding, ensureIdentity, fingerprint, ct);
            AutoFitFormatting(worksheet, () => EnsureFormattingUnchanged(binding, ensureIdentity, fingerprint, ct), ct);
            var after = FormattingWidths(worksheet, plan.Region, ct);
            for (var index = 0; index < before.Length; index++)
                if (Math.Abs(after[index] - before[index]) > 0.01)
                    throw new InvalidDataException($"Used column {plan.Region.FirstColumn + index} was not at Excel's AutoFit width before verification.");
            evidence["autofitVerified"] = "true";
            evidence["autofitColumnWidths"] = string.Join(",", after.Select(width => width.ToString("0.####", CultureInfo.InvariantCulture)));
        }
        EnsureFormattingUnchanged(binding, ensureIdentity, fingerprint, ct);
        return evidence;
    }

    private static void EnsureFormattingUnchanged(ExcelFormattingBinding binding, Action ensureIdentity, Func<string> fingerprint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); ensureIdentity();
        if (fingerprint() != binding.PreStateSha256)
            throw new InvalidDataException("Non-format cell values or formulas changed after Excel formatting was bound.");
        ct.ThrowIfCancellationRequested(); ensureIdentity();
    }

    private sealed record FormattingRegion(int FirstRow, int FirstColumn, int RowCount, int ColumnCount)
    {
        public int LastRow => FirstRow + RowCount - 1;
        public int LastColumn => FirstColumn + ColumnCount - 1;
    }

    private sealed record FormattingPlan(ExcelWorksheetFormattingDefinition Definition, FormattingRegion Region,
        IReadOnlyList<int> CurrencyColumns, string NumberFormat, string CurrencySymbol);

    internal static void ValidateFormattingRegion(int firstRow, int firstColumn, int rows, int columns)
    {
        if (firstRow < 1 || firstColumn < 1 || rows < 1 || columns is < 1 or > 256 ||
            (long)rows * columns > 10_000 || firstRow + (long)rows - 1 > 1_048_576 || firstColumn + (long)columns - 1 > 16_384)
            throw new InvalidDataException("Selected worksheet formatting exceeds the supported used-region bounds (10,000 cells and 256 columns).");
    }

    internal static void ValidateFormattingHeader(ExcelWorksheetFormattingDefinition definition, int firstRow, int rows)
    {
        if (definition.HeaderRow < firstRow || definition.HeaderRow >= firstRow + (long)rows)
            throw new InvalidDataException("The selected header row must lie within the worksheet's used range.");
    }

    private static FormattingRegion ReadFormattingRegion(object worksheet, CancellationToken ct)
    {
        object? used = null; object? rows = null; object? columns = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            used = ((dynamic)worksheet).UsedRange; rows = ((dynamic)used).Rows; columns = ((dynamic)used).Columns;
            var region = new FormattingRegion(Convert.ToInt32(((dynamic)used).Row, CultureInfo.InvariantCulture),
                Convert.ToInt32(((dynamic)used).Column, CultureInfo.InvariantCulture),
                Convert.ToInt32(((dynamic)rows).Count, CultureInfo.InvariantCulture),
                Convert.ToInt32(((dynamic)columns).Count, CultureInfo.InvariantCulture));
            ValidateFormattingRegion(region.FirstRow, region.FirstColumn, region.RowCount, region.ColumnCount);
            return region;
        }
        finally { Release(columns); Release(rows); Release(used); }
    }

    private static FormattingPlan PreflightFormatting(ExcelFormattingBinding binding, object worksheet, CultureInfo culture, CancellationToken ct)
    {
        var region = ReadFormattingRegion(worksheet, ct);
        var definition = binding.Definition ?? ExcelWorksheetFormattingDefinition.LegacyDefault(region.FirstRow);
        ValidateFormattingHeader(definition, region.FirstRow, region.RowCount);
        var headers = new string?[region.ColumnCount];
        object? cells = null;
        try
        {
            cells = ((dynamic)worksheet).Cells;
            for (var index = 0; index < headers.Length; index++)
            {
                ct.ThrowIfCancellationRequested(); object? cell = null;
                try
                {
                    cell = ((dynamic)cells)[definition.HeaderRow, region.FirstColumn + index];
                    headers[index] = Convert.ToString(((dynamic)cell).Value2, CultureInfo.InvariantCulture);
                }
                finally { Release(cell); }
            }
        }
        finally { Release(cells); }
        var selected = definition.ResolveCurrencyColumns(headers, region.FirstColumn);
        var format = selected.Count == 0 ? "" : definition.CurrencyNumberFormat(culture);
        var symbol = selected.Count == 0 ? "" : format[1..format.IndexOf('"', 1)];
        var plan = new FormattingPlan(definition, region, selected, format, symbol);
        // Validate every chosen data column before changing even the header font.
        foreach (var column in selected)
            VisitCurrencyCells(worksheet, plan, column, (value, _) => ValidateAmountValue(value), ct);
        return plan;
    }

    private static void VisitCurrencyCells(object worksheet, FormattingPlan plan, int column,
        Action<object?, object> inspect, CancellationToken ct)
    {
        object? cells = null;
        try
        {
            cells = ((dynamic)worksheet).Cells;
            for (var row = plan.Definition.HeaderRow + 1; row <= plan.Region.LastRow; row++)
            {
                ct.ThrowIfCancellationRequested(); object? cell = null;
                try
                {
                    cell = ((dynamic)cells)[row, column];
                    object? value = ((dynamic)cell).Value2;
                    inspect(value, cell);
                }
                catch (InvalidDataException ex) { throw new InvalidDataException($"Currency data row {row}, column {column}: {ex.Message}", ex); }
                finally { Release(cell); }
            }
        }
        finally { Release(cells); }
    }

    private static object FormattingRange(object worksheet, int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        object? cells = null; object? first = null; object? last = null;
        try
        {
            cells = ((dynamic)worksheet).Cells;
            first = ((dynamic)cells)[firstRow, firstColumn]; last = ((dynamic)cells)[lastRow, lastColumn];
            return ((dynamic)worksheet).Range[first, last];
        }
        finally { Release(last); Release(first); Release(cells); }
    }

    private static void AutoFitFormatting(object worksheet, Action ensureIdentity, CancellationToken ct)
    {
        object? used = null; object? columns = null;
        try
        {
            used = ((dynamic)worksheet).UsedRange; columns = ((dynamic)used).Columns;
            ct.ThrowIfCancellationRequested(); ensureIdentity(); ((dynamic)columns).AutoFit();
        }
        finally { Release(columns); Release(used); }
    }

    private static double[] FormattingWidths(object worksheet, FormattingRegion region, CancellationToken ct)
    {
        object? columns = null;
        try
        {
            columns = ((dynamic)worksheet).Columns;
            var widths = new double[region.ColumnCount];
            for (var index = 0; index < widths.Length; index++)
            {
                ct.ThrowIfCancellationRequested(); object? column = null;
                try
                {
                    column = ((dynamic)columns)[region.FirstColumn + index];
                    widths[index] = Convert.ToDouble(((dynamic)column).ColumnWidth, CultureInfo.InvariantCulture);
                    if (!double.IsFinite(widths[index]) || widths[index] <= 0)
                        throw new InvalidDataException("An autofitted used column has invalid width.");
                }
                finally { Release(column); }
            }
            return widths;
        }
        finally { Release(columns); }
    }

    private static void ValidateCurrencyDisplay(object? value, string? display, string symbol)
    {
        ValidateAmountValue(value);
        if (value is null or "") return;
        if (string.IsNullOrEmpty(display) || !display.Contains(symbol, StringComparison.Ordinal) || display.Contains('#'))
            throw new InvalidDataException("A selected currency cell does not render the selected currency symbol or displays overflow markers.");
    }

    internal static void ValidateAmountValue(object? value)
    {
        if (value is null or "" || value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal ||
            value is double doubleValue && double.IsFinite(doubleValue) || value is float floatValue && float.IsFinite(floatValue)) return;
        throw new InvalidDataException("Amount data must be numeric. Convert the Amount cells to numbers in Excel, then bind the workbook again.");
    }

    internal static void ValidateAmountDisplay(object? value, string? display)
    {
        ValidateAmountValue(value);
        if (value is null or "") return;
        if (string.IsNullOrEmpty(display) || !display.Contains('$') || display.Contains('#'))
            throw new InvalidDataException("An Amount cell does not display currency or displays overflow markers; currency formatting was not verified.");
    }

    public static ExcelComSession CreateCleanedWorkbook(string bindingId, string path, CsvDocument cleaned)
    {
        cleaned.ValidateBounds();
        var app = CreateExcel(visible: false); dynamic excel = app; object? workbook = null; object? worksheet = null;
        object? target = null;
        try
        {
            workbook = excel.Workbooks.Add();
            worksheet = ((dynamic)workbook).Worksheets[1];
            dynamic sheet = worksheet;
            sheet.Name = "Cleaned Data";
            var rowCount = cleaned.Rows.Count;
            var columnCount = cleaned.Rows[0].Count;
            var values = new object[rowCount, columnCount];
            for (var row = 0; row < rowCount; row++)
                for (var column = 0; column < columnCount; column++)
                    values[row, column] = cleaned.Rows[row][column];
            target = sheet.Range[sheet.Cells[1, 1], sheet.Cells[rowCount, columnCount]];
            ((dynamic)target).NumberFormat = "@";
            ((dynamic)target).Value2 = values;
            ((dynamic)target).Columns.AutoFit();
            ((dynamic)workbook).SaveAs(path, 51);
            excel.Caption = $"Engram {bindingId}";
            return Session(app, workbook, worksheet, bindingId, path);
        }
        catch
        {
            ExcelComSession? failed = new(app, workbook!, worksheet!, bindingId, 0, 0, path, true);
            Close(ref failed);
            throw;
        }
        finally { Release(target); }
    }

    public static ExcelComSession OpenCleanedWorkbook(string bindingId, string path)
    {
        var app = CreateExcel();
        dynamic excel = app;
        object? workbooks = null, workbook = null, worksheet = null;
        try
        {
            workbooks = excel.Workbooks;
            var previousSecurity = excel.AutomationSecurity;
            try
            {
                excel.AutomationSecurity = 3; // msoAutomationSecurityForceDisable
                workbook = ((dynamic)workbooks).Open(path, UpdateLinks: 0);
            }
            finally { excel.AutomationSecurity = previousSecurity; }
            worksheet = ((dynamic)workbook).Worksheets[1];
            excel.Caption = $"Engram {bindingId}";
            return Session(app, workbook, worksheet, bindingId, path);
        }
        catch
        {
            ExcelComSession? failed = new(app, workbook!, worksheet!, bindingId, 0, 0, path, true);
            Close(ref failed);
            throw;
        }
        finally { Release(workbooks); }
    }

    public static IReadOnlyDictionary<string, string> VerifyCleanedWorkbook(
        ExcelCsvCleanupBinding binding,
        ExcelComSession session,
        CsvDocument expected)
    {
        if (session.PublishedSha256 != null && (session.ProcessId <= 0 || !IsWindowVisible(new IntPtr(session.WindowHandle))))
            throw new InvalidDataException("The published workbook's Excel window is not visible.");
        if (!Path.GetFullPath(session.Path ?? string.Empty).Equals(binding.OutputPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cleaned workbook session path does not match the bound output path.");
        dynamic workbook = session.Workbook;
        dynamic sheet = session.Worksheet;
        var workbookPath = Path.GetFullPath(Convert.ToString(workbook.FullName, CultureInfo.InvariantCulture) ?? string.Empty);
        if (!workbookPath.Equals(binding.OutputPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Visible cleaned workbook is not the bound output workbook.");
        object? used = null;
        try
        {
            used = sheet.UsedRange;
            var rowCount = Convert.ToInt32(((dynamic)used).Rows.Count, CultureInfo.InvariantCulture);
            var columnCount = Convert.ToInt32(((dynamic)used).Columns.Count, CultureInfo.InvariantCulture);
            if (rowCount != expected.Rows.Count || columnCount != expected.Rows[0].Count)
                throw new InvalidDataException("Cleaned workbook dimensions do not match the canonical cleaned CSV rows.");
            for (var row = 0; row < rowCount; row++)
                for (var column = 0; column < columnCount; column++)
                {
                    var actual = Convert.ToString(sheet.Cells[row + 1, column + 1].Value2, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!actual.Equals(expected.Rows[row][column], StringComparison.Ordinal))
                        throw new InvalidDataException($"Cleaned workbook cell mismatch at row {row + 1}, column {column + 1}.");
                }
            var semanticSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expected.Serialize()))).ToLowerInvariant();
            return new Dictionary<string, string>
            {
                ["workbookPath"] = workbookPath,
                ["workbookFormat"] = "xlsx",
                ["rowCount"] = Math.Max(0, rowCount - 1).ToString(CultureInfo.InvariantCulture),
                ["columnCount"] = columnCount.ToString(CultureInfo.InvariantCulture),
                ["cellMatrixSha256"] = semanticSha256
            };
        }
        finally { Release(used); }
    }

    private static object CreateExcel(bool visible = true, bool requireEmpty = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Excel desktop automation requires Windows.");
        var type = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("Microsoft Excel desktop automation is not installed.");
        dynamic app = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Microsoft Excel could not create an isolated instance.");
        if (requireEmpty)
        {
            object? workbooks = null;
            try
            {
                workbooks = app.Workbooks;
                if (Convert.ToInt32(((dynamic)workbooks).Count, CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException("Excel returned an instance with existing workbooks; no workbook was changed.");
            }
            catch { Release(app); throw; }
            finally { Release(workbooks); }
        }
        app.Visible = visible; app.DisplayAlerts = false; return app;
    }
    private static ExcelComSession Session(object app, object workbook, object worksheet, string bindingId, string? path)
    { dynamic excel = app; var hwnd = Convert.ToInt64(excel.Hwnd, CultureInfo.InvariantCulture); GetWindowThreadProcessId(new IntPtr(hwnd), out var pid); return new ExcelComSession(app, workbook, worksheet, bindingId, unchecked((int)pid), hwnd, path, true); }
    private static string Fingerprint(object worksheet, CancellationToken ct = default)
    {
        const int maximumCells = 10000;
        const int maximumRows = 1048576;
        const int maximumColumns = 16384;
        ArgumentNullException.ThrowIfNull(worksheet);
        ct.ThrowIfCancellationRequested();
        object? used = null; object? usedRows = null; object? usedColumns = null; object? cells = null;
        try
        {
            used = ((dynamic)worksheet).UsedRange;
            usedRows = ((dynamic)used).Rows;
            usedColumns = ((dynamic)used).Columns;
            var rows = FingerprintDimension(((dynamic)usedRows).Count, maximumRows);
            var columns = FingerprintDimension(((dynamic)usedColumns).Count, maximumColumns);
            var startRow = FingerprintDimension(((dynamic)used).Row, maximumRows);
            var startColumn = FingerprintDimension(((dynamic)used).Column, maximumColumns);
            if ((long)rows * columns > maximumCells || (long)startRow + rows - 1 > maximumRows ||
                (long)startColumn + columns - 1 > maximumColumns)
                throw new InvalidDataException("Excel worksheet fingerprint requires a valid used range of at most 10,000 cells.");

            ct.ThrowIfCancellationRequested();
            cells = ((dynamic)worksheet).Cells;
            using var data = new MemoryStream();
            using var writer = new BinaryWriter(data);
            writer.Write(startRow); writer.Write(startColumn); writer.Write(rows); writer.Write(columns);
            var textCharacters = 0;
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                ct.ThrowIfCancellationRequested();
                object? cell = null; object? value = null; object? hasFormula = null; object? formula = null;
                try
                {
                    cell = ((dynamic)cells)[startRow + row, startColumn + column];
                    value = ((dynamic)cell).Value2;
                    WriteFingerprintValue(writer, value, ref textCharacters);
                    hasFormula = ((dynamic)cell).HasFormula;
                    if (hasFormula is not bool formulaPresent)
                        throw new InvalidDataException("Excel worksheet fingerprint requires a scalar formula flag.");
                    writer.Write(formulaPresent);
                    if (formulaPresent)
                    {
                        formula = ((dynamic)cell).Formula;
                        if (formula is not string formulaText)
                            throw new InvalidDataException("Excel worksheet fingerprint requires a text formula.");
                        WriteFingerprintText(writer, formulaText, 8192, ref textCharacters);
                    }
                    ct.ThrowIfCancellationRequested();
                }
                finally { Release(formula); Release(hasFormula); Release(value); Release(cell); }
            }
            writer.Flush();
            ct.ThrowIfCancellationRequested();
            return Convert.ToHexString(SHA256.HashData(data.GetBuffer().AsSpan(0, checked((int)data.Length)))).ToLowerInvariant();
        }
        finally { Release(cells); Release(usedColumns); Release(usedRows); Release(used); }
    }

    private static int FingerprintDimension(object value, int maximum)
    {
        if (value is not int dimension || dimension < 1 || dimension > maximum)
            throw new InvalidDataException("Excel worksheet fingerprint received an invalid used-range dimension.");
        return dimension;
    }

    private static void WriteFingerprintText(BinaryWriter writer, string text, int maximum, ref int textCharacters)
    {
        const int maximumTextCharacters = 262144;
        if (text.Length > maximum || text.Length > maximumTextCharacters - textCharacters)
            throw new InvalidDataException("Excel worksheet fingerprint exceeds its per-cell or 262,144 total text-character limit.");
        textCharacters += text.Length;
        writer.Write(text.Length);
        // Preserve exact UTF-16 code units, including surrogate pairs, without encoding replacement collisions.
        foreach (var character in text) writer.Write((ushort)character);
    }

    private static void WriteFingerprintValue(BinaryWriter writer, object? value, ref int textCharacters)
    {
        if (value is null) { writer.Write((byte)TypeCode.Empty); return; }
        if (value is ErrorWrapper error) { writer.Write(byte.MaxValue); writer.Write(error.ErrorCode); return; }
        writer.Write((byte)Type.GetTypeCode(value.GetType()));
        switch (value)
        {
            case string text: WriteFingerprintText(writer, text, 32767, ref textCharacters); break;
            case bool boolean: writer.Write(boolean); break;
            case byte number: writer.Write(number); break;
            case sbyte number: writer.Write(number); break;
            case short number: writer.Write(number); break;
            case ushort number: writer.Write(number); break;
            case int number: writer.Write(number); break;
            case uint number: writer.Write(number); break;
            case long number: writer.Write(number); break;
            case ulong number: writer.Write(number); break;
            case float number when float.IsFinite(number): writer.Write(number); break;
            case double number when double.IsFinite(number): writer.Write(number); break;
            case decimal number: writer.Write(number); break;
            default: throw new InvalidDataException("Excel worksheet fingerprint encountered an unsupported or nonfinite scalar value.");
        }
    }
    internal static ExcelWorkbookWindow SelectWorkbookWindow(
        IReadOnlyList<ExcelWorkbookWindow> windows, long foregroundWindowHandle, long? requestedWindowHandle = null)
    {
        if (requestedWindowHandle.HasValue)
        {
            var selected = windows.SingleOrDefault(window => window.WindowHandle == requestedWindowHandle.Value);
            return selected ?? throw new InvalidOperationException("Selected Excel workbook window is no longer visible or available; select it again.");
        }
        var foreground = windows.SingleOrDefault(window => window.WindowHandle == foregroundWindowHandle);
        if (foreground != null) return foreground;
        return windows.Count switch
        {
            1 => windows[0],
            0 => throw new InvalidOperationException("No visible Excel workbook window is available to bind."),
            _ => throw new InvalidOperationException("Several Excel workbook windows are open; select the specific workbook window to bind.")
        };
    }

    public static IReadOnlyList<ExcelWorkbookWindow> ListWorkbookWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Excel workbook selection requires Windows.");
        var windows = new List<ExcelWorkbookWindow>();
        EnumWindows((window, _) =>
        {
            if (!TryGetWorkbookWindow(window, out var processId)) return true;
            var title = new StringBuilder(4096);
            _ = GetWindowText(window, title, title.Capacity);
            windows.Add(new ExcelWorkbookWindow(window.ToInt64(), processId, title.ToString()));
            return true;
        }, IntPtr.Zero);
        return windows.AsReadOnly();
    }

    private static bool TryGetWorkbookWindow(IntPtr window, out int processId)
    {
        processId = 0;
        if (!IsWindowVisible(window)) return false;
        var className = new StringBuilder(64);
        _ = GetClassName(window, className, className.Capacity);
        if (!className.ToString().Equals("XLMAIN", StringComparison.Ordinal)) return false;
        _ = GetWindowThreadProcessId(window, out var pid);
        if (pid == 0) return false;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)pid));
            if (!process.ProcessName.Equals("EXCEL", StringComparison.OrdinalIgnoreCase)) return false;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        if (FindExcelNativeModelWindow(window) == IntPtr.Zero) return false;
        processId = unchecked((int)pid);
        return true;
    }

    private static void ValidateWorkbookWindow(long windowHandle, int expectedProcessId)
    {
        if (!TryGetWorkbookWindow(new IntPtr(windowHandle), out var processId) || processId != expectedProcessId)
            throw new InvalidDataException("Selected Excel workbook window closed or changed process; bind the workbook again.");
    }

    internal static void EnsureWorkbookWindowIdentity(long expectedWindowHandle,
        Func<(long WindowHandle, bool WorkbookMatches, bool WorksheetMatches)> readIdentity)
    {
        var identity = readIdentity();
        if (identity.WindowHandle != expectedWindowHandle || !identity.WorkbookMatches || !identity.WorksheetMatches)
            throw new InvalidDataException("Selected Excel window no longer displays the bound workbook and worksheet; bind it again.");
    }

    internal static void EnsureBoundWorkbookWindow(object window, long expectedWindowHandle, int processId, object workbook, object worksheet)
    {
        EnsureWorkbookWindowIdentity(expectedWindowHandle, () =>
        {
            ValidateWorkbookWindow(expectedWindowHandle, processId);
            object? activeSheet = null; object? activeWorkbook = null;
            try
            {
                activeSheet = ((dynamic)window).ActiveSheet;
                if (activeSheet != null) activeWorkbook = ((dynamic)activeSheet).Parent;
                return (Convert.ToInt64(((dynamic)window).Hwnd, CultureInfo.InvariantCulture),
                    SameComObject(activeWorkbook, workbook), SameComObject(activeSheet, worksheet));
            }
            finally { Release(activeWorkbook); Release(activeSheet); }
        });
    }

    private static bool SameComObject(object? actual, object expected)
    {
        if (actual == null) return false;
        var actualPointer = Marshal.GetIUnknownForObject(actual);
        try
        {
            var expectedPointer = Marshal.GetIUnknownForObject(expected);
            try { return actualPointer == expectedPointer; }
            finally { Marshal.Release(expectedPointer); }
        }
        finally { Marshal.Release(actualPointer); }
    }

    internal static object GetWorkbookWindow(long handle, int processId)
    {
        ValidateWorkbookWindow(handle, processId);
        var windowHandle = new IntPtr(handle);
        var nativeModelWindow = FindExcelNativeModelWindow(windowHandle);
        if (nativeModelWindow == IntPtr.Zero)
            throw new InvalidOperationException("Selected Excel window did not expose an EXCEL7 native workbook surface.");
        var dispatch = IidDispatch;
        var result = AccessibleObjectFromWindow(nativeModelWindow, ObjIdNativeOfficeModel, ref dispatch, out var nativeWindow);
        try
        {
            if (result != 0) Marshal.ThrowExceptionForHR(result);
            if (nativeWindow == null) throw new InvalidOperationException("Selected Excel window did not expose its native object model.");
            if (Convert.ToInt64(((dynamic)nativeWindow).Hwnd, CultureInfo.InvariantCulture) != handle)
                throw new InvalidDataException("Excel's native object model does not match the selected workbook window.");
            ValidateWorkbookWindow(handle, processId);
            return nativeWindow;
        }
        catch { Release(nativeWindow); throw; }
    }

    private static IntPtr FindExcelNativeModelWindow(IntPtr topLevelWindow)
    {
        var match = IntPtr.Zero;
        EnumChildWindows(topLevelWindow, (child, _) =>
        {
            var className = new StringBuilder(64);
            _ = GetClassName(child, className, className.Capacity);
            if (className.ToString().Equals("EXCEL7", StringComparison.Ordinal))
            {
                match = child;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return match;
    }
    public static void Close(ref ExcelComSession? session)
    {
        if (session == null) return;
        var current = session;
        session = null;
        var processId = current.ProcessId;
        var ownsApplication = current.OwnsApplication;
        object? worksheet = current.Worksheet;
        object? workbook = current.Workbook;
        object? application = current.Application;
        current = null;
        if (ownsApplication)
        {
            try { ((dynamic)workbook!).Close(false); } catch { }
            // Another workbook may have been opened by the user after this
            // operation started. Close our workbook, but never quit over it.
            object? workbooks = null;
            try
            {
                workbooks = ((dynamic)application!).Workbooks;
                if (Convert.ToInt32(((dynamic)workbooks).Count, CultureInfo.InvariantCulture) == 0)
                    ((dynamic)application!).Quit();
            }
            catch { }
            finally { Release(workbooks); }
        }
        Release(worksheet);
        Release(workbook);
        Release(application);
        worksheet = null;
        workbook = null;
        application = null;
        // Dynamic Office calls can create short-lived RCWs for Cells, Ranges,
        // ChartObjects, and panes. Drain those proxies only after the owned
        // workbook/application have been closed so Excel can terminate without
        // leaving an invisible automation process behind.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    // Property getters can return RCWs retained by another binding. Release only
    // this acquired reference so a read-only bind cannot disconnect owned sessions.
    public static void Release(object? value) { try { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); } catch { } }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);
    private const uint ObjIdNativeOfficeModel = 0xFFFFFFF0;
    private static Guid IidDispatch = new("00020400-0000-0000-C000-000000000046");
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd,
        uint objectId,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object? value);
}

internal sealed class CsvDocument
{
    internal const int MaximumSourceBytes = 4 * 1024 * 1024;
    private const int MaximumCells = 10_000;
    private const int MaximumColumns = 256;
    private const int MaximumFieldCharacters = 32_767;
    private const int MaximumTextCharacters = 262_144;
    public List<List<string>> Rows { get; }
    private CsvDocument(List<List<string>> rows) => Rows = rows;
    public static CsvDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumSourceBytes)
            throw new InvalidDataException("CSV text exceeds the bounded source limit.");
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        var cells = 0; var textCharacters = 0; var fieldPresent = false;
        void Append(char character)
        {
            if (field.Length >= MaximumFieldCharacters || textCharacters >= MaximumTextCharacters)
                throw new InvalidDataException("CSV exceeds its 32,767-character field or 262,144-character total text limit.");
            field.Append(character);
            textCharacters++;
            fieldPresent = true;
        }
        void AddField()
        {
            if (row.Count >= MaximumColumns || cells >= MaximumCells)
                throw new InvalidDataException("CSV exceeds its 256-column or 10,000-cell limit.");
            row.Add(field.ToString()); field.Clear(); cells++; fieldPresent = false;
        }
        void AddRow()
        {
            if (rows.Count >= MaximumCells)
                throw new InvalidDataException("CSV exceeds its 10,000-row limit.");
            if (rows.Count > 0 && row.Count != rows[0].Count)
                throw new InvalidDataException("CSV rows have inconsistent column counts.");
            rows.Add(row); row = new List<string>();
        }
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted) { if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { Append('"'); i++; } else if (ch == '"') quoted = false; else Append(ch); }
            else if (ch == '"' && field.Length == 0) { quoted = true; fieldPresent = true; }
            else if (ch == ',') AddField();
            else if (ch is '\r' or '\n') { if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; AddField(); AddRow(); }
            else Append(ch);
        }
        if (quoted) throw new InvalidDataException("CSV contains an unterminated quoted field.");
        if (fieldPresent || row.Count > 0) { AddField(); AddRow(); }
        if (rows.Count == 0) throw new InvalidDataException("CSV is empty.");
        var columns = rows[0].Count; if (columns == 0 || rows.Any(candidate => candidate.Count != columns)) throw new InvalidDataException("CSV rows have inconsistent column counts.");
        return new CsvDocument(rows);
    }
    public CsvDocument CleanAndSortByDateDescending()
    {
        ValidateBounds();
        var header = Rows[0].Select(value => value.Trim()).ToList(); var date = header.FindIndex(value => value.Equals("Date", StringComparison.OrdinalIgnoreCase)); if (date < 0) throw new InvalidDataException("CSV has no Date column.");
        var seen = new HashSet<string>(StringComparer.Ordinal); var data = new List<(List<string> Row, DateTimeOffset Date)>();
        foreach (var raw in Rows.Skip(1)) { var row = raw.Select(value => value.Trim()).ToList(); var key = JsonSerializer.Serialize(row); if (!seen.Add(key)) continue; if (!DateTimeOffset.TryParse(row[date], CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var parsed)) throw new InvalidDataException($"CSV Date value '{row[date]}' is invalid."); data.Add((row, parsed)); }
        data.Sort((left, right) => right.Date.CompareTo(left.Date)); return new CsvDocument(new[] { header }.Concat(data.Select(item => item.Row)).ToList());
    }
    internal void ValidateBounds()
    {
        if (Rows.Count is < 1 or > MaximumCells || Rows[0] is null || Rows[0].Count is < 1 or > MaximumColumns)
            throw new InvalidDataException("CSV dimensions exceed the bounded Excel limits.");
        var columns = Rows[0].Count; var cells = 0; var textCharacters = 0;
        foreach (var row in Rows)
        {
            if (row is null || row.Count != columns || row.Count > MaximumCells - cells)
                throw new InvalidDataException("CSV rows are inconsistent or exceed the 10,000-cell limit.");
            cells += row.Count;
            foreach (var value in row)
            {
                if (value is null || value.Length > MaximumFieldCharacters || value.Length > MaximumTextCharacters - textCharacters)
                    throw new InvalidDataException("CSV exceeds its field or total text limit.");
                textCharacters += value.Length;
            }
        }
    }
    public void AssertCleanAndDateDescending()
    {
        var cleaned = CleanAndSortByDateDescending(); AssertEquivalent(cleaned);
    }
    public void AssertEquivalent(CsvDocument other) { if (Rows.Count != other.Rows.Count || Rows.Where((row, index) => !row.SequenceEqual(other.Rows[index], StringComparer.Ordinal)).Any()) throw new InvalidDataException("CSV semantic round-trip verification failed."); }
    public string Serialize() => string.Join("\r\n", Rows.Select(row => string.Join(",", row.Select(Escape)))) + "\r\n";
    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : '"' + value.Replace("\"", "\"\"") + '"';
}
