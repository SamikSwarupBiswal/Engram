using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Engram.Store.Automation;

namespace Engram.Store.Tests;

public sealed class ExcelWorksheetFingerprintTests
{
    [Fact]
    public void Fingerprint_DistinguishesNumericValueFromNumericLookingText()
    {
        Assert.NotEqual(Hash(Sheet(123d)), Hash(Sheet("123")));
    }

    [Fact]
    public void Fingerprint_DistinguishesFormulaFromLiteralWithSameFormulaText()
    {
        var formula = Sheet(2d);
        formula.CellSet.Cell = new FingerprintCell(2d, "=1+1", true);
        Assert.NotEqual(Hash(formula), Hash(Sheet("=1+1")));
    }

    [Fact]
    public void Fingerprint_TracksFormulaAndCalculatedValueIndependently()
    {
        var original = Sheet(2d);
        original.CellSet.Cell = new FingerprintCell(2d, "=1+1", true);
        var recalculated = Sheet(3d);
        recalculated.CellSet.Cell = new FingerprintCell(3d, "=1+1", true);
        var changedFormula = Sheet(2d);
        changedFormula.CellSet.Cell = new FingerprintCell(2d, "=2", true);
        Assert.NotEqual(Hash(original), Hash(recalculated));
        Assert.NotEqual(Hash(original), Hash(changedFormula));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    public void Fingerprint_IncludesUsedRangeOrigin(int row, int column)
    {
        Assert.NotEqual(Hash(Sheet("same")), Hash(Sheet("same", row: row, column: column)));
    }

    [Fact]
    public void Fingerprint_IncludesUsedRangeShape()
    {
        Assert.NotEqual(Hash(Sheet("same", rows: 1, columns: 2)), Hash(Sheet("same", rows: 2, columns: 1)));
    }

    [Fact]
    public void Fingerprint_IsStableAndDoesNotReadStyles()
    {
        var sheet = Sheet("Unicode \uD83D\uDE00 \u09AC\u09BE\u0982\u09B2\u09BE \"\\\n");
        var first = Hash(sheet);
        sheet.CellSet.Cell.Style = "currency";
        Assert.Equal(first, Hash(sheet));
        Assert.Matches("^[a-f0-9]{64}$", first);
    }

    [Fact]
    public void Fingerprint_DistinguishesEmptyTextNullAndBoolean()
    {
        var hashes = new[] { Hash(Sheet(null)), Hash(Sheet("")), Hash(Sheet(true)), Hash(Sheet("True")), Hash(Sheet(1d)) };
        Assert.Equal(hashes.Length, hashes.Distinct().Count());
    }

    [Theory]
    [InlineData(101, 100, 1, 1)]
    [InlineData(int.MaxValue, int.MaxValue, 1, 1)]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    [InlineData(2, 1, 1048576, 1)]
    [InlineData(1, 2, 1, 16384)]
    public void Fingerprint_RejectsUnsafeDimensionsBeforeAccessingCells(int rows, int columns, int row, int column)
    {
        var sheet = Sheet("value", rows, columns, row, column);
        sheet.CellSet.ThrowOnRead = true;
        Assert.Throws<InvalidDataException>(() => Hash(sheet));
        Assert.Equal(0, sheet.CellsAccesses);
        Assert.Equal(0, sheet.CellSet.Reads);
    }

    [Fact]
    public void Fingerprint_AllowsCellBudgetBoundary()
    {
        var sheet = Sheet(null, rows: 100, columns: 100);
        Assert.NotEmpty(Hash(sheet));
        Assert.Equal(10000, sheet.CellSet.Reads);
        Assert.Equal(1, sheet.CellsAccesses);
    }

    [Fact]
    public void Fingerprint_RejectsOversizedCellText()
    {
        Assert.Throws<InvalidDataException>(() => Hash(Sheet(new string('x', 32768))));
    }

    [Fact]
    public void Fingerprint_RejectsTotalTextBudgetAndStopsReading()
    {
        var sheet = Sheet(new string('x', 32767), rows: 10);
        Assert.Throws<InvalidDataException>(() => Hash(sheet));
        Assert.Equal(9, sheet.CellSet.Reads);
    }

    [Fact]
    public void Fingerprint_AcceptsTextBudgetBoundary()
    {
        var sheet = Sheet("", rows: 9);
        sheet.CellSet.ReadCell = (row, _) => new FingerprintCell(new string('x', row <= 8 ? 32767 : 8));
        Assert.NotEmpty(Hash(sheet));
    }

    [Fact]
    public void Fingerprint_RejectsOversizedFormula()
    {
        var sheet = Sheet(1d);
        sheet.CellSet.Cell = new FingerprintCell(1d, "=" + new string('x', 8192), true);
        Assert.Throws<InvalidDataException>(() => Hash(sheet));
    }

    [Theory]
    [MemberData(nameof(UnsupportedValues))]
    public void Fingerprint_RejectsUnsupportedAndNonfiniteValues(object value)
    {
        Assert.Throws<InvalidDataException>(() => Hash(Sheet(value)));
    }

    public static IEnumerable<object[]> UnsupportedValues()
    {
        yield return new object[] { double.NaN };
        yield return new object[] { double.PositiveInfinity };
        yield return new object[] { float.NegativeInfinity };
        yield return new object[] { new object() };
        yield return new object[] { new[] { "array" } };
    }

    [Fact]
    public void Fingerprint_PreservesExcelErrorCodeIdentity()
    {
        Assert.NotEqual(Hash(Sheet(new ErrorWrapper(2007))), Hash(Sheet(new ErrorWrapper(2042))));
        Assert.NotEqual(Hash(Sheet(new ErrorWrapper(2007))), Hash(Sheet(2007)));
    }

    [Fact]
    public void Fingerprint_CancellationStopsBeforeWorksheetRead()
    {
        var sheet = Sheet("value");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Hash(sheet, cancellation.Token));
        Assert.Equal(0, sheet.UsedRangeReads);
    }

    [Fact]
    public void Fingerprint_CancellationDuringReadStopsBeforeNextCell()
    {
        var sheet = Sheet("value", rows: 10);
        using var cancellation = new CancellationTokenSource();
        sheet.CellSet.ReadCell = (_, _) => { cancellation.Cancel(); return new FingerprintCell("value"); };
        Assert.Throws<OperationCanceledException>(() => Hash(sheet, cancellation.Token));
        Assert.Equal(1, sheet.CellSet.Reads);
    }

    private static FingerprintWorksheet Sheet(object? value, int rows = 1, int columns = 1, int row = 1, int column = 1)
        => new(new FingerprintRange(rows, columns, row, column), new FingerprintCells(new FingerprintCell(value)));

    private static string Hash(FingerprintWorksheet sheet, CancellationToken ct = default)
    {
        var method = typeof(ExcelCom).GetMethod("Fingerprint", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return (string)method.Invoke(null, method.GetParameters().Length == 1 ? new object[] { sheet } : new object[] { sheet, ct })!;
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}

// Public managed fakes allow ExcelCom's dynamic binder to inspect them without Excel or COM.
public sealed class FingerprintWorksheet(FingerprintRange usedRange, FingerprintCells cells)
{
    public int UsedRangeReads { get; private set; }
    public int CellsAccesses { get; private set; }
    public FingerprintRange UsedRange { get { UsedRangeReads++; return usedRange; } }
    public FingerprintCells CellSet => cells;
    public FingerprintCells Cells { get { CellsAccesses++; return cells; } }
}

public sealed class FingerprintRange(int rows, int columns, int row, int column)
{
    public FingerprintDimension Rows { get; } = new(rows);
    public FingerprintDimension Columns { get; } = new(columns);
    public int Row => row;
    public int Column => column;
}

public sealed class FingerprintDimension(int count)
{
    public int Count => count;
}

public sealed class FingerprintCells(FingerprintCell cell)
{
    public FingerprintCell Cell { get; set; } = cell;
    public bool ThrowOnRead { get; set; }
    public int Reads { get; private set; }
    public Func<int, int, FingerprintCell>? ReadCell { get; set; }
    public FingerprintCell this[int row, int column]
    {
        get
        {
            Reads++;
            if (ThrowOnRead) throw new InvalidOperationException("Cell access must not occur for unsafe dimensions.");
            return ReadCell?.Invoke(row, column) ?? Cell;
        }
    }
}

public sealed class FingerprintCell(object? value, object? formula = null, bool hasFormula = false)
{
    public object? Value2 => value;
    public object? Formula => hasFormula ? formula : value;
    public bool HasFormula => hasFormula;
    public string Style { get; set; } = "plain";
}
