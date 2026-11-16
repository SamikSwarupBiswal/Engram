using System.Globalization;
using System.Text.Json;
using Engram.Store.Automation;

namespace Engram.Store.Tests;

public sealed class ExcelFormattingRuntimeTests
{
    private static ExcelWorksheetFormattingDefinition Definition(bool bold = false, bool freeze = false,
        bool autoFit = true, string[]? columns = null, string currency = "local", int row = 3)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { tool = "excel.worksheet.format", headerRow = row,
            boldHeader = bold, freezeHeader = freeze, autoFitColumns = autoFit, currencyColumns = columns ?? [], currencyCode = currency }));
        return ExcelWorksheetFormattingDefinition.Parse(json.RootElement);
    }

    private static ExcelFormattingCapture Capture() => new(17, 202, "User.xlsx", "Data", new string('a', 64), 3, 2, 3, 3);

    private static ExcelAdvancedContextBindingRegistry Registry(FakeSession session, Action? capture = null,
        Action<long?, CancellationToken>? observeCapture = null) => new((handle, ct) =>
        { capture?.Invoke(); observeCapture?.Invoke(handle, ct); return Capture(); }, (binding, ct) => { session.OpenToken = ct; return session; });

    [Fact]
    public void BindPreservesTypedChoicesAndForwardsSelectionAndCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var definition = Definition(bold: true, autoFit: false, columns: ["Tax"], currency: "INR");
        long? selected = null; CancellationToken observed = default;
        var registry = Registry(new(), observeCapture: (handle, ct) => { selected = handle; observed = ct; });
        var binding = registry.BindActiveFormatting(definition, 202, cancellation.Token);
        Assert.Same(definition, binding.Definition);
        Assert.Equal(202L, selected); Assert.Equal(cancellation.Token, observed);
        Assert.Equal(Capture().PreStateSha256, binding.PreStateSha256);
        Assert.Equal(binding, registry.CurrentFormatting);
    }

    [Fact]
    public void NullDefinitionAndPreCanceledCallsNeverCaptureOrOpen()
    {
        var captures = 0; var session = new FakeSession(); var registry = Registry(session, () => captures++);
        Assert.Throws<ArgumentNullException>(() => registry.BindActiveFormatting((ExcelWorksheetFormattingDefinition)null!));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => registry.BindActiveFormatting(Definition(), ct: cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => registry.ApplyFormatting(new string('b', 32), cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => registry.VerifyFormatting(new string('b', 32), cancellation.Token));
        Assert.Equal(0, captures); Assert.Equal(0, session.Applies);
    }

    [Fact]
    public void LegacyBindingKeepsFirstUsedHeaderAndHistoricalChoices()
    {
        var binding = Registry(new()).BindActiveFormatting(202);
        Assert.NotNull(binding.Definition);
        Assert.Equal(3, binding.Definition!.HeaderRow);
        Assert.True(binding.Definition.BoldHeader); Assert.True(binding.Definition.FreezeHeader); Assert.True(binding.Definition.AutoFitColumns);
        Assert.Equal(new[] { "Amount" }, binding.Definition.CurrencyColumns);
        Assert.Equal("USD", binding.Definition.CurrencyCode);
    }

    [Theory]
    [InlineData(0, 2, 3, 3)]
    [InlineData(3, 0, 3, 3)]
    [InlineData(3, 2, 0, 3)]
    [InlineData(3, 2, 3, 257)]
    [InlineData(3, 2, 10001, 1)]
    [InlineData(1048576, 2, 2, 3)]
    [InlineData(3, 16384, 3, 2)]
    public void InvalidCapturedRegionIsNotRetained(int firstRow, int firstColumn, int rows, int columns)
    {
        var registry = new ExcelAdvancedContextBindingRegistry((_, _) => Capture() with
            { FirstRow = firstRow, FirstColumn = firstColumn, RowCount = rows, ColumnCount = columns }, (_, _) => new FakeSession());
        Assert.Throws<InvalidDataException>(() => registry.BindActiveFormatting(Definition()));
        Assert.Null(registry.CurrentFormatting);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void HeaderMustBeInsideCapturedRegion(int row)
    {
        Assert.Throws<InvalidDataException>(() => Registry(new()).BindActiveFormatting(Definition(row: row)));
    }

    [Fact]
    public void MetadataReleaseIsIdempotentAndRestoresBoundedCapacity()
    {
        var captures = 0; var registry = Registry(new(), () => captures++);
        var bindings = Enumerable.Range(0, 32).Select(_ => registry.BindActiveFormatting(Definition())).ToArray();
        Assert.Throws<InvalidOperationException>(() => registry.BindActiveFormatting(Definition()));
        Assert.Equal(32, captures);
        Assert.True(registry.ReleaseFormattingBinding(bindings[0].BindingId));
        Assert.False(registry.ReleaseFormattingBinding(bindings[0].BindingId));
        Assert.NotNull(registry.BindActiveFormatting(Definition()));
    }

    [Fact]
    public void ApplyRequiresSeparateReadbackAndCannotRunTwice()
    {
        var session = new FakeSession(); var registry = Registry(session); var binding = registry.BindActiveFormatting(Definition());
        Assert.Throws<InvalidOperationException>(() => registry.VerifyFormatting(binding.BindingId));
        var applied = registry.ApplyFormatting(binding.BindingId);
        Assert.False(applied.Verified); Assert.Equal("excel-workbook", applied.Ownership!.Kind);
        Assert.Equal(binding.BindingId, applied.Ownership.BindingId);
        Assert.Same(binding, session.AppliedBinding); Assert.False(registry.ReleaseFormattingBinding(binding.BindingId));
        Assert.Throws<InvalidOperationException>(() => registry.ApplyFormatting(binding.BindingId));
        Assert.Equal(1, session.Applies);
        using var cancellation = new CancellationTokenSource();
        var verified = registry.VerifyFormatting(binding.BindingId, cancellation.Token);
        Assert.True(verified.Verified); Assert.Equal("fake-readback", verified.Evidence["verification"]);
        Assert.Equal(cancellation.Token, session.VerifyToken);
    }

    [Fact]
    public void CleanupDisposesOnlyFormattingReferencesAndRemovesMetadata()
    {
        var session = new FakeSession(); var registry = Registry(session); var binding = registry.BindActiveFormatting(Definition());
        registry.ApplyFormatting(binding.BindingId);
        Assert.True(registry.Cleanup(binding.BindingId)); Assert.Equal(1, session.Disposals);
        Assert.False(registry.Cleanup(binding.BindingId)); Assert.False(registry.ReleaseFormattingBinding(binding.BindingId));
        Assert.Throws<InvalidOperationException>(() => registry.VerifyFormatting(binding.BindingId));
        Assert.Null(registry.CurrentFormatting);
    }

    [Fact]
    public void MetadataOnlyCleanupDoesNotOpenOrDisposeAWorkbook()
    {
        var session = new FakeSession(); var registry = Registry(session); var binding = registry.BindActiveFormatting(Definition());
        Assert.True(registry.Cleanup(binding.BindingId)); Assert.Equal(0, session.Disposals);
        Assert.False(registry.ReleaseFormattingBinding(binding.BindingId));
    }

    [Fact]
    public async Task ActiveApplyRefusesConcurrentApplyVerifyReleaseAndCleanup()
    {
        using var entered = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
        var session = new FakeSession { DuringApply = () => { entered.Set(); Assert.True(resume.Wait(TimeSpan.FromSeconds(15))); } };
        var registry = Registry(session); var binding = registry.BindActiveFormatting(Definition());
        var task = Task.Run(() => registry.ApplyFormatting(binding.BindingId));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            Assert.Throws<InvalidOperationException>(() => registry.ApplyFormatting(binding.BindingId));
            Assert.Throws<InvalidOperationException>(() => registry.VerifyFormatting(binding.BindingId));
            Assert.False(registry.ReleaseFormattingBinding(binding.BindingId)); Assert.False(registry.Cleanup(binding.BindingId));
        }
        finally { resume.Set(); }
        await task;
        Assert.True(registry.Cleanup(binding.BindingId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyFailureOrCancellationDisposesAcquiredSession(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var session = new FakeSession { DuringApply = () => { if (cancel) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); } throw new InvalidDataException("preflight failed"); } };
        var registry = Registry(session); var binding = registry.BindActiveFormatting(Definition());
        var error = Record.Exception(() => registry.ApplyFormatting(binding.BindingId, cancellation.Token));
        Assert.IsAssignableFrom<Exception>(error);
        Assert.Equal(cancel, error is OperationCanceledException); Assert.Equal(1, session.Disposals);
        Assert.Throws<InvalidOperationException>(() => registry.VerifyFormatting(binding.BindingId));
        Assert.True(registry.ReleaseFormattingBinding(binding.BindingId));
    }

    [Fact]
    public void OpenFailureLeavesReleasableMetadata()
    {
        var registry = new ExcelAdvancedContextBindingRegistry((_, _) => Capture(), (_, _) => throw new InvalidDataException("identity changed"));
        var binding = registry.BindActiveFormatting(Definition());
        Assert.Equal("identity changed", Assert.Throws<InvalidDataException>(() => registry.ApplyFormatting(binding.BindingId)).Message);
        Assert.True(registry.ReleaseFormattingBinding(binding.BindingId));
    }

    [Theory]
    [InlineData(18, 202)]
    [InlineData(17, 203)]
    public void SessionIdentityMismatchStopsBeforeApplyAndDisposes(int pid, long handle)
    {
        var session = new FakeSession { ProcessId = pid, WindowHandle = handle }; var registry = Registry(session);
        var binding = registry.BindActiveFormatting(Definition());
        Assert.Throws<InvalidDataException>(() => registry.ApplyFormatting(binding.BindingId));
        Assert.Equal(0, session.Applies); Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public void VerifyFailureDisposesRetainedSessionAndKeepsTruthfulFailure()
    {
        var session = new FakeSession { DuringVerify = () => throw new InvalidDataException("content changed") };
        var registry = Registry(session); var binding = registry.BindActiveFormatting(Definition()); registry.ApplyFormatting(binding.BindingId);
        Assert.Equal("content changed", Assert.Throws<InvalidDataException>(() => registry.VerifyFormatting(binding.BindingId)).Message);
        Assert.Equal(1, session.Disposals); Assert.True(registry.ReleaseFormattingBinding(binding.BindingId));
    }

    private static ExcelFormattingBinding Binding(ExcelWorksheetFormattingDefinition definition) =>
        new(new string('b', 32), 17, 202, "User.xlsx", "Data", "unchanged", DateTimeOffset.UtcNow) { Definition = definition };

    [Fact]
    public void CoreAppliesOnlySelectedCurrencyToBoundedDataAndAutoFitsLast()
    {
        var sheet = new FakeSheet(); var window = new FakeWindow(sheet.Events);
        var binding = Binding(Definition(columns: ["Tax"], currency: "INR"));
        ExcelCom.ApplyFormattingCore(binding, sheet, window, () => { }, () => "unchanged", CultureInfo.InvariantCulture, default);
        Assert.Equal(new[] { "format:C4:C5:\"₹\"#,##0.00", "autofit" }, sheet.Events);
        Assert.Equal("General", sheet.Range["B4:B5"].NumberFormat);
        Assert.False(sheet.Range["B3:D3"].Font.Bold); Assert.False(window.FreezePanes);
        var evidence = ExcelCom.VerifyFormattingCore(binding, sheet, window, () => { }, () => "unchanged", CultureInfo.InvariantCulture, default);
        Assert.Equal("true", evidence["currencyDisplayVerified"]);
        Assert.DoesNotContain("headerBold", evidence.Keys); Assert.DoesNotContain("freezeHeader", evidence.Keys);
    }

    [Fact]
    public void CoreFreezesThroughSelectedHeaderWithoutInventingOtherOperations()
    {
        var sheet = new FakeSheet(); var window = new FakeWindow(sheet.Events) { SplitColumn = 5 };
        var binding = Binding(Definition(freeze: true, autoFit: false));
        ExcelCom.ApplyFormattingCore(binding, sheet, window, () => { }, () => "unchanged", CultureInfo.InvariantCulture, default);
        Assert.True(window.FreezePanes); Assert.Equal(3, window.SplitRow); Assert.Equal(0, window.SplitColumn);
        Assert.DoesNotContain("autofit", sheet.Events); Assert.DoesNotContain(sheet.Events, item => item.StartsWith("format:"));
        ExcelCom.VerifyFormattingCore(binding, sheet, window, () => { }, () => "unchanged", CultureInfo.InvariantCulture, default);
        Assert.DoesNotContain("autofit", sheet.Events);
    }

    [Fact]
    public void CoreBoldsOnlyTheSelectedHeaderSpan()
    {
        var sheet = new FakeSheet(); var window = new FakeWindow(sheet.Events); var binding = Binding(Definition(bold: true, autoFit: false));
        ExcelCom.ApplyFormattingCore(binding, sheet, window, () => { }, () => "unchanged", CultureInfo.InvariantCulture, default);
        Assert.True(sheet.Range["B3:D3"].Font.Bold); Assert.Equal(new[] { "bold:B3:D3" }, sheet.Events);
        ExcelCom.VerifyFormattingCore(binding, sheet, window, () => { }, () => "unchanged", CultureInfo.InvariantCulture, default);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("last-column-nonnumeric")]
    [InlineData("outside-header")]
    [InlineData("oversize")]
    public void CorePreflightsAllHeadersAndSelectedValuesBeforeAnyMutation(string failure)
    {
        var sheet = new FakeSheet(); var row = 3;
        if (failure == "missing") sheet.Values[(3, 4)] = "Other";
        if (failure == "duplicate") sheet.Values[(3, 2)] = "Tax";
        if (failure == "last-column-nonnumeric") sheet.Values[(5, 4)] = "100";
        if (failure == "outside-header") row = 2;
        if (failure == "oversize") sheet.UsedRange.Rows.Count = 10001;
        var binding = Binding(Definition(bold: true, freeze: true, columns: ["Tax", "Total"], currency: "EUR", row: row));
        Assert.Throws<InvalidDataException>(() => ExcelCom.ApplyFormattingCore(binding, sheet, new FakeWindow(sheet.Events), () => { }, () => "unchanged", CultureInfo.InvariantCulture, default));
        Assert.Empty(sheet.Events);
    }

    [Fact]
    public void CoreChecksFingerprintAndIdentityImmediatelyBeforeMutationAndAfter()
    {
        var sheet = new FakeSheet(); var window = new FakeWindow(sheet.Events); var reads = 0;
        var binding = Binding(Definition(bold: true, autoFit: false));
        Assert.Throws<InvalidDataException>(() => ExcelCom.ApplyFormattingCore(binding, sheet, window, () => { }, () => ++reads == 1 ? "unchanged" : "changed", CultureInfo.InvariantCulture, default));
        Assert.Empty(sheet.Events);
        Assert.Throws<InvalidDataException>(() => ExcelCom.ApplyFormattingCore(binding, sheet, window, () => throw new InvalidDataException("identity changed"), () => "unchanged", CultureInfo.InvariantCulture, default));
        Assert.Empty(sheet.Events);
    }

    [Fact]
    public void ChangedContentBetweenFormattingStepsStopsFurtherMutationsWithoutInventingRollback()
    {
        var sheet = new FakeSheet(); var window = new FakeWindow(sheet.Events);
        var binding = Binding(Definition(bold: true, autoFit: false, columns: ["Tax"], currency: "EUR"));
        Assert.Throws<InvalidDataException>(() => ExcelCom.ApplyFormattingCore(binding, sheet, window, () => { },
            () => sheet.Events.Count == 0 ? "unchanged" : "changed", CultureInfo.InvariantCulture, default));
        Assert.Equal(new[] { "bold:B3:D3" }, sheet.Events);
    }

    [Fact]
    public void AutoFitVerificationRequiresExcelFixedPointRatherThanPositiveWidths()
    {
        var sheet = new FakeSheet(); var window = new FakeWindow(sheet.Events);
        sheet.Columns.AutoFitChangesWidth = true;
        Assert.Throws<InvalidDataException>(() => ExcelCom.VerifyFormattingCore(Binding(Definition()), sheet, window,
            () => { }, () => "unchanged", CultureInfo.InvariantCulture, default));
    }

    [Fact]
    public void VerificationDetectsContentChangesDuringRangeAcquisitionBeforeAutoFit()
    {
        var sheet = new FakeSheet(); var changed = false; var reads = 0;
        sheet.BeforeUsedRangeRead = () => { if (++reads == 2) changed = true; };
        Assert.Throws<InvalidDataException>(() => ExcelCom.VerifyFormattingCore(Binding(Definition()), sheet,
            new FakeWindow(sheet.Events), () => { }, () => changed ? "changed" : "unchanged", CultureInfo.InvariantCulture, default));
        Assert.Empty(sheet.Events);
    }

    [Fact]
    public void FormattingAdapterCannotOwnOrCloseAUserWorkbook()
    {
        var workbook = new UserWorkbook(); var application = new UserApplication();
        var session = new ExcelComSession(application, workbook, new object(), new string('b', 32), 17, 202, null, false);
        var adapter = new ExcelFormattingSession(session);
        adapter.Dispose(); adapter.Dispose();
        Assert.Equal(0, workbook.Closes); Assert.Equal(0, application.Quits);
        Assert.Throws<InvalidOperationException>(() => new ExcelFormattingSession(session with { OwnsApplication = true }));
        Assert.Throws<ObjectDisposedException>(() => adapter.Apply(Binding(Definition()), default));
    }

    [Fact]
    public void CancellationAfterCaptureDoesNotRetainMetadata()
    {
        using var cancellation = new CancellationTokenSource();
        var registry = new ExcelAdvancedContextBindingRegistry((_, _) => { cancellation.Cancel(); return Capture(); }, (_, _) => new FakeSession());
        Assert.Throws<OperationCanceledException>(() => registry.BindActiveFormatting(Definition(), ct: cancellation.Token));
        Assert.Null(registry.CurrentFormatting);
    }

    [Fact]
    public void VerifyRechecksSessionWindowBeforeReadback()
    {
        var session = new FakeSession(); var registry = Registry(session); var binding = registry.BindActiveFormatting(Definition());
        registry.ApplyFormatting(binding.BindingId); session.WindowHandle = 999;
        Assert.Throws<InvalidDataException>(() => registry.VerifyFormatting(binding.BindingId));
        Assert.Equal(1, session.Disposals);
    }

    [Fact]
    public async Task PendingCaptureCountsTowardThePreparationLimit()
    {
        using var entered = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim(); var captures = 0;
        var registry = new ExcelAdvancedContextBindingRegistry((_, _) =>
        {
            if (Interlocked.Increment(ref captures) == 1) { entered.Set(); Assert.True(resume.Wait(TimeSpan.FromSeconds(15))); }
            return Capture();
        }, (_, _) => new FakeSession());
        var pending = Task.Run(() => registry.BindActiveFormatting(Definition()));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            for (var index = 0; index < 31; index++) registry.BindActiveFormatting(Definition());
            Assert.Throws<InvalidOperationException>(() => registry.BindActiveFormatting(Definition()));
            Assert.Equal(32, captures);
        }
        finally { resume.Set(); }
        Assert.NotNull(await pending);
    }

    [Theory]
    [InlineData("wrong-format")]
    [InlineData("wrong-symbol")]
    [InlineData("overflow")]
    [InlineData("content")]
    public void CoreVerificationRejectsIncorrectReadback(string failure)
    {
        var sheet = new FakeSheet(); var window = new FakeWindow(sheet.Events);
        var binding = Binding(Definition(autoFit: false, columns: ["Tax"], currency: "EUR"));
        ExcelCom.ApplyFormattingCore(binding, sheet, window, () => { }, () => "unchanged", CultureInfo.InvariantCulture, default);
        if (failure == "wrong-format") sheet.Range["C4:C5"].NumberFormat = "\"$\"#,##0.00";
        if (failure == "wrong-symbol") sheet.DisplayOverride = "$10.00";
        if (failure == "overflow") sheet.DisplayOverride = "€####";
        Assert.Throws<InvalidDataException>(() => ExcelCom.VerifyFormattingCore(binding, sheet, window, () => { }, () => failure == "content" ? "changed" : "unchanged", CultureInfo.InvariantCulture, default));
    }

    [Fact]
    public void CoreCancellationDoesNotMutate()
    {
        var sheet = new FakeSheet(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ExcelCom.ApplyFormattingCore(Binding(Definition()), sheet,
            new FakeWindow(sheet.Events), () => { }, () => "unchanged", CultureInfo.InvariantCulture, cancellation.Token));
        Assert.Empty(sheet.Events);
    }

    private sealed class FakeSession : IExcelFormattingSession
    {
        public int ProcessId { get; set; } = 17;
        public long WindowHandle { get; set; } = 202;
        public int Applies; public int Disposals;
        public CancellationToken OpenToken; public CancellationToken VerifyToken;
        public ExcelFormattingBinding? AppliedBinding;
        public Action? DuringApply; public Action? DuringVerify;
        public void Apply(ExcelFormattingBinding binding, CancellationToken ct) { Applies++; AppliedBinding = binding; DuringApply?.Invoke(); ct.ThrowIfCancellationRequested(); }
        public IReadOnlyDictionary<string, string> Verify(ExcelFormattingBinding binding, CancellationToken ct)
        { VerifyToken = ct; DuringVerify?.Invoke(); ct.ThrowIfCancellationRequested(); return new Dictionary<string, string> { ["verification"] = "fake-readback" }; }
        public void Dispose() => Disposals++;
    }

    public sealed class FakeSheet
    {
        public List<string> Events { get; } = [];
        public Dictionary<(int, int), object?> Values { get; } = new() { [(3, 2)] = "Item", [(3, 3)] = "Tax", [(3, 4)] = "Total", [(4, 2)] = "A", [(4, 3)] = 10d, [(4, 4)] = 100d, [(5, 2)] = "B", [(5, 3)] = 20d, [(5, 4)] = 200d };
        private readonly FakeUsedRange _usedRange;
        public Action? BeforeUsedRangeRead { get; set; }
        public FakeUsedRange UsedRange { get { BeforeUsedRangeRead?.Invoke(); return _usedRange; } }
        public FakeCells Cells { get; }
        public FakeRanges Range { get; }
        public FakeColumns Columns { get; }
        public string? DisplayOverride;
        public FakeSheet() { Cells = new(this); Range = new(this); Columns = new(this); _usedRange = new(this); }
    }
    public sealed class FakeUsedRange
    {
        public int Row => 3; public int Column => 2;
        public FakeRows Rows { get; } = new(); public FakeColumns Columns { get; }
        public FakeUsedRange(FakeSheet sheet) => Columns = sheet.Columns;
    }
    public sealed class FakeRows { public int Count { get; set; } = 3; }
    public sealed class FakeCells(FakeSheet sheet) { public FakeCell this[int row, int column] => new(sheet, row, column); }
    public sealed class FakeCell(FakeSheet sheet, int row, int column)
    {
        public int Row => row; public int Column => column;
        public object? Value2 => sheet.Values.GetValueOrDefault((row, column));
        public string Text => sheet.DisplayOverride ?? (column == 3 ? "€₹$10.00" : "€₹$100.00");
    }
    public sealed class FakeRanges(FakeSheet sheet)
    {
        private readonly Dictionary<string, FakeRange> _ranges = [];
        public FakeRange this[string address] => _ranges.TryGetValue(address, out var range) ? range : _ranges[address] = new(sheet, address);
        public FakeRange this[object first, object last] { get { var start = (FakeCell)first; var end = (FakeCell)last; return this[$"{(char)('A' + start.Column - 1)}{start.Row}:{(char)('A' + end.Column - 1)}{end.Row}"]; } }
    }
    public sealed class FakeRange(FakeSheet sheet, string address)
    {
        public FakeFont Font { get; } = new(sheet, address);
        private string _numberFormat = "General";
        public string NumberFormat { get => _numberFormat; set { _numberFormat = value; sheet.Events.Add($"format:{address}:{value}"); } }
    }
    public sealed class FakeFont(FakeSheet sheet, string address)
    {
        private bool _bold;
        public bool Bold { get => _bold; set { _bold = value; sheet.Events.Add($"bold:{address}"); } }
    }
    public sealed class FakeColumns(FakeSheet sheet)
    {
        public int Count => 3;
        private readonly Dictionary<int, FakeColumn> _columns = [];
        public FakeColumn this[int index] => _columns.TryGetValue(index, out var column) ? column : _columns[index] = new();
        public bool AutoFitChangesWidth;
        public void AutoFit()
        {
            sheet.Events.Add("autofit");
            if (AutoFitChangesWidth) for (var index = 2; index <= 4; index++) this[index].ColumnWidth += 2;
        }
    }
    public sealed class FakeColumn { public double ColumnWidth { get; set; } = 12; }
    public sealed class FakeWindow(List<string> events)
    {
        public bool FreezePanes { get; set; }
        public int SplitRow { get; set; }
        public int SplitColumn { get; set; }
    }
    public sealed class UserWorkbook { public int Closes; public void Close(bool saveChanges) => Closes++; }
    public sealed class UserApplication { public int Quits; public void Quit() => Quits++; }
}
