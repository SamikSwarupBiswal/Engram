using Engram.Store.Automation;

namespace Engram.Store.Tests;

public sealed class ExcelChartRuntimeTests
{
    private static ExcelChartBindingRequest Request() => new()
    {
        SalesData = [new("=1+1", 10m), new("+SUM(A1:A2)", 20m), new("@name", 30m), new("-12", 40m), new("May", 50m)]
    };

    private static ExcelAdvancedContextBindingRegistry Registry(
        Func<ExcelWorkbookBinding, CancellationToken, ExcelComSession> create) =>
        new((_, _) => throw new InvalidOperationException("Unexpected formatting capture."),
            (_, _) => throw new InvalidOperationException("Unexpected formatting session."), createWorkbook: create);

    private static ExcelAdvancedContextBindingRegistry Registry(Func<ExcelWorkbookBinding, ExcelComSession> create) =>
        Registry((binding, _) => create(binding));

    private static ExcelComSession Session(ExcelWorkbookBinding binding) =>
        new(new object(), new object(), new object(), binding.BindingId, 17, 202, null, false);

    [Fact]
    public void ChartCreationUsesTypedLiteralCellsAndTheGenericWorkbookRuntime()
    {
        var sheet = new ManagedSheet(); ExcelWorkbookBinding? prepared = null;
        var registry = Registry((binding, ct) =>
        {
            prepared = binding;
            ExcelCom.WriteWorkbookDataCells(sheet, binding.Definition.Cells, ct);
            return Session(binding);
        });
        var request = Request(); var chart = registry.BindChart(request);
        var result = registry.CreateChart(chart.BindingId);
        try
        {
            Assert.False(result.Verified);
            Assert.NotNull(prepared);
            Assert.Equal(chart.BindingId, prepared!.BindingId);
            Assert.Equal(request.SalesData.Select(point => point.Month), Assert.Single(prepared.Definition.Charts).Categories);
            Assert.Empty(prepared.Definition.Formulas);
            for (var index = 0; index < request.SalesData.Length; index++)
            {
                var cell = sheet.Range["A" + (index + 2)];
                Assert.Equal(request.SalesData[index].Month, cell.Value2);
                Assert.Equal("@", cell.NumberFormatAtWrite);
            }
        }
        finally { Assert.True(registry.Cleanup(chart.BindingId)); }
    }

    [Fact]
    public void ChartCreationRejectsDuplicateStartBeforeAnotherWorkbookIsAllocated()
    {
        var allocations = 0;
        var registry = Registry(binding => { allocations++; return Session(binding); });
        var chart = registry.BindChart(Request());
        registry.CreateChart(chart.BindingId);
        try
        {
            Assert.Throws<InvalidOperationException>(() => registry.CreateChart(chart.BindingId));
            Assert.Equal(1, allocations);
            Assert.False(registry.ReleaseWorkbookBinding(chart.BindingId));
        }
        finally { Assert.True(registry.Cleanup(chart.BindingId)); }
        Assert.Null(registry.CurrentChart);
    }

    [Fact]
    public async Task ConcurrentChartStartAndPreparationReleaseAreRejected()
    {
        using var entered = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
        var allocations = 0;
        var registry = Registry(binding =>
        {
            Interlocked.Increment(ref allocations); entered.Set();
            if (!resume.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Test did not release creation.");
            return Session(binding);
        });
        var chart = registry.BindChart(Request());
        var pending = Task.Run(() => registry.CreateChart(chart.BindingId));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            Assert.Throws<InvalidOperationException>(() => registry.CreateChart(chart.BindingId));
            Assert.False(registry.ReleaseWorkbookBinding(chart.BindingId));
            Assert.Equal(1, allocations);
        }
        finally { resume.Set(); }
        await pending;
        Assert.True(registry.Cleanup(chart.BindingId));
    }

    [Fact]
    public void FailedCreationPreservesItsErrorAndReleasesThePreparedChart()
    {
        var failure = new InvalidDataException("Creation failed before allocating Excel.");
        var registry = Registry(_ => throw failure);
        var chart = registry.BindChart(Request());
        Assert.Same(failure, Assert.Throws<InvalidDataException>(() => registry.CreateChart(chart.BindingId)));
        Assert.False(registry.ReleaseWorkbookBinding(chart.BindingId));
        Assert.Null(registry.CurrentChart);
    }

    [Fact]
    public void ChartPreparationsShareTheBoundedWorkbookBudgetAndCanBeReleased()
    {
        var registry = Registry(Session);
        var charts = Enumerable.Range(0, 32).Select(_ => registry.BindChart(Request())).ToArray();
        Assert.Throws<InvalidOperationException>(() => registry.BindChart(Request()));
        Assert.True(registry.ReleaseWorkbookBinding(charts[0].BindingId));
        Assert.False(registry.ReleaseWorkbookBinding(charts[0].BindingId));
        Assert.NotEqual(charts[0].BindingId, registry.BindChart(Request()).BindingId);
    }

    [Fact]
    public void ChartBindingRejectsValuesExcelCannotRetainBeforeAllocation()
    {
        var request = Request(); request.SalesData[0] = new("January", 0.1234567890123456m);
        var registry = Registry(_ => throw new InvalidOperationException("Must not allocate."));
        Assert.Throws<InvalidDataException>(() => registry.BindChart(request));
        Assert.Null(registry.CurrentChart);
    }

    public sealed class ManagedSheet { public ManagedRanges Range { get; } = new(); }
    public sealed class ManagedRanges
    {
        private readonly Dictionary<string, ManagedCell> _cells = new();
        public ManagedCell this[string address] => _cells.TryGetValue(address, out var cell) ? cell : _cells[address] = new();
    }
    public sealed class ManagedCell
    {
        private object? _value;
        public string? NumberFormat { get; set; }
        public string? NumberFormatAtWrite { get; private set; }
        public object? Value2
        {
            get => _value;
            set { NumberFormatAtWrite = NumberFormat; _value = value; }
        }
    }
}
