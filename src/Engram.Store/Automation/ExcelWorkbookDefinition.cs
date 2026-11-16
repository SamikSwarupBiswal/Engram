using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Engram.Store.Automation;

public enum ExcelWorkbookCellKind { Blank, Text, Number, Boolean }
public sealed record ExcelWorkbookCell(string Address, ExcelWorkbookCellKind Kind, object? Value);
public sealed record ExcelWorkbookFormula(string Cell, string SourceRange, string Formula, double ExpectedValue);
public sealed record ExcelWorkbookChart(string Title, string SeriesName, string CategoryRange, string ValueRange,
    string CategoryAxisTitle, string ValueAxisTitle, IReadOnlyList<string> Categories, IReadOnlyList<double> Values);

/// <summary>Immutable, bounded new-workbook data. It carries no file or execution authority.</summary>
public sealed class ExcelWorkbookDefinition
{
    public const int MaximumCells = 10_000;
    public const int MaximumFormulas = 32;
    private ExcelWorkbookDefinition(string name, string start, int rowCount, int columnCount,
        ExcelWorkbookCell[] cells, ExcelWorkbookFormula[] formulas, ExcelWorkbookChart[] charts)
    {
        WorksheetName = name;
        StartCell = start;
        RowCount = rowCount;
        ColumnCount = columnCount;
        Cells = Array.AsReadOnly(cells);
        Formulas = Array.AsReadOnly(formulas);
        Charts = Array.AsReadOnly(charts);
        EstimatedBytes = cells.Sum(cell => 128L + (cell.Value is string text ? Encoding.UTF8.GetByteCount(text) : 16)) +
            formulas.Length * 256L + charts.Sum(chart => 2048L + chart.Categories.Sum(label => 16L + Encoding.UTF8.GetByteCount(label)));
    }

    public string WorksheetName { get; }
    public string StartCell { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<ExcelWorkbookCell> Cells { get; }
    public IReadOnlyList<ExcelWorkbookFormula> Formulas { get; }
    public IReadOnlyList<ExcelWorkbookChart> Charts { get; }
    public long EstimatedBytes { get; }

    public static ExcelWorkbookDefinition Parse(JsonElement arguments)
    {
        RequireProperties(arguments, ["tool", "worksheetName", "startCell", "rows", "formulas"], "charts");
        if (String(arguments.GetProperty("tool")) != "excel.workbook.create")
            throw new InvalidDataException("Choose a supported Excel workbook tool.");
        var name = String(arguments.GetProperty("worksheetName"));
        if (name.Length is < 1 or > 31 || name.Trim().Length == 0 || name.Any(char.IsControl) ||
            name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0 || name.StartsWith('\'') || name.EndsWith('\'') ||
            name.Equals("History", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a valid Excel worksheet name of at most 31 characters.");
        var origin = Address(String(arguments.GetProperty("startCell")));
        var rows = arguments.GetProperty("rows");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() is < 1 or > MaximumCells)
            throw new InvalidDataException("Provide a nonempty rectangular grid with at most 10,000 cells.");
        var height = rows.GetArrayLength();
        var width = rows[0].ValueKind == JsonValueKind.Array ? rows[0].GetArrayLength() : 0;
        if (width < 1 || (long)height * width > MaximumCells || origin.Row + height - 1 > 1_048_576 || origin.Column + width - 1 > 16_384)
            throw new InvalidDataException("The supplied grid exceeds the bounded Excel worksheet range.");
        var cells = new List<ExcelWorkbookCell>(height * width);
        var textBytes = 0;
        for (var r = 0; r < height; r++)
        {
            if (rows[r].ValueKind != JsonValueKind.Array || rows[r].GetArrayLength() != width)
                throw new InvalidDataException("Excel data rows must form one rectangular grid.");
            for (var c = 0; c < width; c++)
            {
                var element = rows[r][c];
                var address = CellAddress(origin.Row + r, origin.Column + c);
                var cell = element.ValueKind switch
                {
                    JsonValueKind.Null => new ExcelWorkbookCell(address, ExcelWorkbookCellKind.Blank, null),
                    JsonValueKind.True or JsonValueKind.False => new(address, ExcelWorkbookCellKind.Boolean, element.GetBoolean()),
                    JsonValueKind.String => new(address, ExcelWorkbookCellKind.Text, element.GetString()!),
                    JsonValueKind.Number when TryExcelNumber(element, out var number) =>
                        new(address, ExcelWorkbookCellKind.Number, number),
                    _ => throw new InvalidDataException("Excel cells must be text, numbers with at most 15 significant digits within +/-1e12, booleans, or blank.")
                };
                if (cell.Value is string text)
                {
                    textBytes += Encoding.UTF8.GetByteCount(text);
                    if (text.Length is < 1 or > 2048 || text.Contains('\0') || textBytes > 65_536)
                        throw new InvalidDataException("Excel cell text exceeds the bounded workbook text limit.");
                }
                cells.Add(cell);
            }
        }
        var inputByAddress = cells.ToDictionary(cell => cell.Address, StringComparer.Ordinal);
        var formulas = arguments.GetProperty("formulas");
        if (formulas.ValueKind != JsonValueKind.Array || formulas.GetArrayLength() > MaximumFormulas)
            throw new InvalidDataException("Provide at most 32 bounded local SUM operations.");
        var outputs = new HashSet<string>(StringComparer.Ordinal);
        var compiled = new List<ExcelWorkbookFormula>();
        var minRow = origin.Row; var maxRow = origin.Row + height - 1;
        var minColumn = origin.Column; var maxColumn = origin.Column + width - 1;
        foreach (var formula in formulas.EnumerateArray())
        {
            RequireProperties(formula, ["cell", "operation", "sourceRange"]);
            if (String(formula.GetProperty("operation")) != "sum")
                throw new InvalidDataException("Only a bounded local SUM operation is supported.");
            var destination = Address(String(formula.GetProperty("cell")));
            var cell = CellAddress(destination.Row, destination.Column);
            if (inputByAddress.ContainsKey(cell) || !outputs.Add(cell))
                throw new InvalidDataException("Formula destinations must be distinct and outside the supplied data cells.");
            var rangeText = String(formula.GetProperty("sourceRange"));
            var parts = rangeText.Split(':');
            if (parts.Length is < 1 or > 2) throw new InvalidDataException("SUM requires an unqualified local A1 range.");
            var first = Address(parts[0]); var last = Address(parts[^1]);
            if (first.Row > last.Row || first.Column > last.Column ||
                (long)(last.Row - first.Row + 1) * (last.Column - first.Column + 1) > MaximumCells)
                throw new InvalidDataException("SUM source range is invalid or too large.");
            double sum = 0, compensation = 0;
            for (var r = first.Row; r <= last.Row; r++)
                for (var c = first.Column; c <= last.Column; c++)
                {
                    if (!inputByAddress.TryGetValue(CellAddress(r, c), out var source) || source.Kind != ExcelWorkbookCellKind.Number)
                        throw new InvalidDataException("SUM sources must contain only numeric cells from the supplied grid.");
                    var adjusted = (double)source.Value! - compensation;
                    var next = sum + adjusted;
                    compensation = next - sum - adjusted;
                    sum = next;
                }
            minRow = Math.Min(minRow, destination.Row); maxRow = Math.Max(maxRow, destination.Row);
            minColumn = Math.Min(minColumn, destination.Column); maxColumn = Math.Max(maxColumn, destination.Column);
            if ((long)(maxRow - minRow + 1) * (maxColumn - minColumn + 1) > MaximumCells + MaximumFormulas)
                throw new InvalidDataException("Formula destinations must stay within the bounded data/output region.");
            var sourceRange = CellAddress(first.Row, first.Column) + ":" + CellAddress(last.Row, last.Column);
            compiled.Add(new ExcelWorkbookFormula(cell, sourceRange, "=SUM(" + sourceRange + ")", sum));
        }
        var charts = new List<ExcelWorkbookChart>();
        if (arguments.TryGetProperty("charts", out var chartArguments))
        {
            if (chartArguments.ValueKind != JsonValueKind.Array || chartArguments.GetArrayLength() > 1)
                throw new InvalidDataException("Provide at most one bounded column chart per workbook.");
            foreach (var chart in chartArguments.EnumerateArray())
            {
                RequireProperties(chart, ["type", "title", "seriesName", "categoryRange", "valueRange", "categoryAxisTitle", "valueAxisTitle"]);
                if (String(chart.GetProperty("type")) != "column") throw new InvalidDataException("Choose a supported column chart.");
                var categoryCells = ChartCells(String(chart.GetProperty("categoryRange")), inputByAddress);
                var valueCells = ChartCells(String(chart.GetProperty("valueRange")), inputByAddress);
                if (categoryCells.Length != valueCells.Length ||
                    categoryCells.Any(cell => cell.Kind != ExcelWorkbookCellKind.Text) ||
                    valueCells.Any(cell => cell.Kind != ExcelWorkbookCellKind.Number))
                    throw new InvalidDataException("Chart categories must be literal text and values must be matching numeric cells from the supplied grid.");
                charts.Add(new(ChartLabel(chart, "title"), ChartLabel(chart, "seriesName"),
                    categoryCells[0].Address + ":" + categoryCells[^1].Address,
                    valueCells[0].Address + ":" + valueCells[^1].Address,
                    ChartLabel(chart, "categoryAxisTitle"), ChartLabel(chart, "valueAxisTitle"),
                    Array.AsReadOnly(categoryCells.Select(cell => (string)cell.Value!).ToArray()),
                    Array.AsReadOnly(valueCells.Select(cell => (double)cell.Value!).ToArray())));
            }
        }
        return new(name, CellAddress(origin.Row, origin.Column), height, width, cells.ToArray(), compiled.ToArray(), charts.ToArray());
    }

    private static ExcelWorkbookCell[] ChartCells(string range, IReadOnlyDictionary<string, ExcelWorkbookCell> cells)
    {
        var parts = range.Split(':');
        if (parts.Length is < 1 or > 2) throw new InvalidDataException("Chart ranges must be unqualified local A1 ranges.");
        var first = Address(parts[0]); var last = Address(parts[^1]);
        if (first.Row > last.Row || first.Column > last.Column || first.Row != last.Row && first.Column != last.Column ||
            (long)(last.Row - first.Row + 1) * (last.Column - first.Column + 1) > 1000)
            throw new InvalidDataException("Chart ranges must contain one row or column of at most 1000 cells.");
        var selected = new List<ExcelWorkbookCell>();
        for (var r = first.Row; r <= last.Row; r++)
            for (var c = first.Column; c <= last.Column; c++)
            {
                if (!cells.TryGetValue(CellAddress(r, c), out var cell))
                    throw new InvalidDataException("Chart sources must belong to the supplied data grid.");
                selected.Add(cell);
            }
        return selected.ToArray();
    }

    private static string ChartLabel(JsonElement chart, string property)
    {
        var label = String(chart.GetProperty(property));
        if (string.IsNullOrWhiteSpace(label) || label.Length > 120 || label.Any(char.IsControl) ||
            property == "seriesName" && label.TrimStart()[0] is '=' or '+' or '-' or '@')
            throw new InvalidDataException("Chart titles and series labels must be nonempty literal text of at most 120 characters.");
        return label;
    }

    internal static bool NumbersMatch(double expected, double actual) =>
        double.IsFinite(expected) && double.IsFinite(actual) &&
        (expected == actual || Math.Abs(expected - actual) <= Math.Abs(expected) * 2e-15);

    private static bool TryExcelNumber(JsonElement element, out double number)
    {
        if (!element.TryGetDouble(out number) || !double.IsFinite(number) || Math.Abs(number) > 1e12) return false;
        var mantissa = element.GetRawText().Split('e', 'E')[0];
        var significant = string.Concat(mantissa.Where(char.IsDigit)).TrimStart('0').TrimEnd('0');
        // Excel retains at most 15 significant decimal digits and does not
        // represent subnormal doubles. Never silently round a richer input.
        return significant.Length <= 15 && (number == 0 ? significant.Length == 0 : Math.Abs(number) >= 2.2250738585072014e-308);
    }

    private static string String(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new InvalidDataException("Excel tool names and addresses must be strings.");

    private static void RequireProperties(JsonElement value, string[] properties, params string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Excel tool arguments must be objects.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
            if ((!properties.Contains(property.Name, StringComparer.Ordinal) && !optional.Contains(property.Name, StringComparer.Ordinal)) || !seen.Add(property.Name))
                throw new InvalidDataException("Excel tool arguments contain an unknown or duplicate property.");
        if (properties.Any(property => !seen.Contains(property))) throw new InvalidDataException("Excel tool arguments are incomplete.");
    }

    private static (int Row, int Column) Address(string text)
    {
        if (text.Length > 10 || !Regex.IsMatch(text, "\\A[A-Za-z]{1,3}[1-9][0-9]{0,6}\\z", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Use an unqualified local A1 cell address.");
        var letters = text.TakeWhile(char.IsLetter).Count();
        var column = 0;
        foreach (var character in text[..letters].ToUpperInvariant()) column = column * 26 + character - 'A' + 1;
        if (!int.TryParse(text[letters..], NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row > 1_048_576 || column > 16_384)
            throw new InvalidDataException("Excel address is outside the worksheet.");
        return (row, column);
    }

    private static string CellAddress(int row, int column)
    {
        var letters = "";
        for (var value = column; value > 0; value = (value - 1) / 26)
            letters = (char)('A' + (value - 1) % 26) + letters;
        return letters + row.ToString(CultureInfo.InvariantCulture);
    }
}
