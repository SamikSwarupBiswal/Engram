using System.Text.Json;
using Engram.Store.Automation;

namespace Engram.Store.Tests;

public sealed class ExcelWorkbookDefinitionTests
{
    internal static ExcelWorkbookDefinition Parse(string rows = "[[1],[2],[3]]", string start = "D7",
        string formulas = "[{\"cell\":\"D10\",\"operation\":\"sum\",\"sourceRange\":\"D7:D9\"}]", string? charts = null) =>
        ExcelWorkbookDefinition.Parse(JsonDocument.Parse("{\"tool\":\"excel.workbook.create\",\"worksheetName\":\"Sample\",\"startCell\":\"" + start +
            "\",\"rows\":" + rows + ",\"formulas\":" + formulas + (charts == null ? "" : ",\"charts\":" + charts) + "}").RootElement);

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void ColumnChartUsesModelSelectedRangesAndAnyBoundedPointCount(int points)
    {
        var rows = JsonSerializer.Serialize(Enumerable.Range(0, points).Select(i => new object[] { "Label " + i, i + 10 }));
        var chart = JsonSerializer.Serialize(new[] { new { type = "column", title = "Illustrative totals", seriesName = "Units",
            categoryRange = $"F12:F{11 + points}", valueRange = $"G12:G{11 + points}", categoryAxisTitle = "Group", valueAxisTitle = "Units" } });

        var definition = Parse(rows, "F12", "[]", chart);
        var compiled = Assert.Single(definition.Charts);
        Assert.Equal(Enumerable.Range(0, points).Select(i => "Label " + i), compiled.Categories);
        Assert.Equal(Enumerable.Range(0, points).Select(i => (double)i + 10), compiled.Values);
        Assert.Equal($"F12:F{11 + points}", compiled.CategoryRange);
        Assert.Equal($"G12:G{11 + points}", compiled.ValueRange);
        Assert.Equal("Illustrative totals", compiled.Title);
        Assert.Equal("Group", compiled.CategoryAxisTitle);
        Assert.Equal("Units", compiled.ValueAxisTitle);
    }

    private static string ChartArguments(string categories = "A1:A2", string values = "B1:B2", string series = "Units") =>
        JsonSerializer.Serialize(new[] { new { type = "column", title = "Totals", seriesName = series,
            categoryRange = categories, valueRange = values, categoryAxisTitle = "Group", valueAxisTitle = "Units" } });

    [Theory]
    [InlineData("Other!A1:A2", "B1:B2")]
    [InlineData("[other.xlsx]A1:A2", "B1:B2")]
    [InlineData("A1,A2", "B1:B2")]
    [InlineData("A1:A3", "B1:B2")]
    [InlineData("A1:A1", "B1:B2")]
    [InlineData("A2:A1", "B1:B2")]
    [InlineData("A1:B2", "B1:B2")]
    [InlineData("B1:B2", "A1:A2")]
    public void ChartSourcesCannotEscapeTheGridOrMismatchLiteralCategoriesAndNumbers(string categories, string values)
    {
        Assert.Throws<InvalidDataException>(() => Parse("[[\"One\",1],[\"Two\",2]]", "A1", "[]", ChartArguments(categories, values)));
    }

    [Theory]
    [InlineData("=WEBSERVICE(\"https://example.test\")")]
    [InlineData(" =A1")]
    [InlineData("+1")]
    [InlineData("@name")]
    [InlineData("-1")]
    [InlineData("")]
    public void ChartSeriesNamesCannotCarryFormulas(string name)
    {
        Assert.Throws<InvalidDataException>(() => Parse("[[\"One\",1],[\"Two\",2]]", "A1", "[]", ChartArguments(series: name)));
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void ChartPointBudgetIsEnforcedIndependentlyOfGridSize(int points, bool accepted)
    {
        var rows = JsonSerializer.Serialize(Enumerable.Range(0, points).Select(i => new object[] { "Group " + i, i }));
        var error = Record.Exception(() => Parse(rows, "A1", "[]", ChartArguments($"A1:A{points}", $"B1:B{points}")));
        if (accepted) Assert.Null(error);
        else Assert.IsType<InvalidDataException>(error);
    }

    [Fact]
    public void ChartReadBackRequiresExactSeriesNameCategoryOrderValuesAndCount()
    {
        var expected = Assert.Single(Parse("[[\"One\",1],[\"Two\",2]]", "A1", "[]", ChartArguments()).Charts);
        ExcelCom.ValidateWorkbookChartSeries(expected, "Units", new object[] { "One", "Two" }, new double[] { 1, 2 });
        ExcelCom.ValidateWorkbookChartSeries(expected, "Units", new object[,] { { "One" }, { "Two" } }, new double[,] { { 1 }, { 2 } });
        Assert.Throws<InvalidDataException>(() => ExcelCom.ValidateWorkbookChartSeries(expected, "Other", new[] { "One", "Two" }, new[] { 1d, 2d }));
        Assert.Throws<InvalidDataException>(() => ExcelCom.ValidateWorkbookChartSeries(expected, "Units", new[] { "Two", "One" }, new[] { 1d, 2d }));
        Assert.Throws<InvalidDataException>(() => ExcelCom.ValidateWorkbookChartSeries(expected, "Units", new[] { "One", "Two" }, new[] { 1d, 3d }));
        Assert.Throws<InvalidDataException>(() => ExcelCom.ValidateWorkbookChartSeries(expected, "Units", new[] { "One", "Two" }, new[] { "1", "2" }));
        Assert.Throws<InvalidDataException>(() => ExcelCom.ValidateWorkbookChartSeries(expected, "Units", new[] { "One" }, new[] { 1d }));
    }

    [Fact]
    public void SinglePointChartReadBackAcceptsComScalarValues()
    {
        var expected = Assert.Single(Parse("[[\"One\",1]]", "A1", "[]", ChartArguments("A1", "B1")).Charts);
        ExcelCom.ValidateWorkbookChartSeries(expected, "Units", "One", 1d);
    }

    [Fact]
    public void ChartArgumentsRejectExtraAuthorityUnsupportedTypesAndMultipleCharts()
    {
        var chart = ChartArguments();
        foreach (var invalid in new[] { chart.Replace("\"column\"", "\"pie\""), chart.Replace("\"title\":", "\"path\":\"C:/private\",\"title\":"),
            "[" + chart[1..^1] + "," + chart[1..^1] + "]", "null" })
            Assert.Throws<InvalidDataException>(() => Parse("[[\"One\",1],[\"Two\",2]]", "A1", "[]", invalid));
    }

    [Theory]
    [InlineData("[[7]]", "A1", 1, 1)]
    [InlineData("[[1,2],[3,4]]", "F12", 2, 2)]
    [InlineData("[[1],[2],[3],[4],[5],[6],[7]]", "Z100", 7, 1)]
    public void GridShapeAndStartAreChosenByModel(string rows, string start, int height, int width)
    {
        var definition = Parse(rows, start, "[]");
        Assert.Equal(start, definition.StartCell);
        Assert.Equal(height, definition.RowCount);
        Assert.Equal(width, definition.ColumnCount);
        Assert.Equal(height * width, definition.Cells.Count);
    }

    [Fact]
    public void LocalSumIsConstructedAndExpectedValueIsCalculatedFromBoundNumbers()
    {
        var definition = Parse("[[1.25],[2.5],[-0.75]]");
        var formula = Assert.Single(definition.Formulas);
        Assert.Equal("D10", formula.Cell);
        Assert.Equal("=SUM(D7:D9)", formula.Formula);
        Assert.Equal(3d, formula.ExpectedValue);
    }

    [Fact]
    public void CellTypesAndFormulaLikeStringsRemainLiteralData()
    {
        var definition = Parse("[[\"=SUM(A1:A9)\",\"+cmd\",\"-1\",\"@name\",true,null,3.5]]", "C2", "[]");
        Assert.All(definition.Cells.Take(4), cell => Assert.Equal(ExcelWorkbookCellKind.Text, cell.Kind));
        Assert.Equal("=SUM(A1:A9)", definition.Cells[0].Value);
        Assert.Equal(ExcelWorkbookCellKind.Boolean, definition.Cells[4].Kind);
        Assert.Equal(ExcelWorkbookCellKind.Blank, definition.Cells[5].Kind);
        Assert.Equal(ExcelWorkbookCellKind.Number, definition.Cells[6].Kind);
    }

    [Theory]
    [InlineData("[]", "A1", "[]")]
    [InlineData("[[]]", "A1", "[]")]
    [InlineData("[[1],[2,3]]", "A1", "[]")]
    [InlineData("[[{}]]", "A1", "[]")]
    [InlineData("[[1e999]]", "A1", "[]")]
    [InlineData("[[1,2]]", "XFD1", "[]")]
    [InlineData("[[1],[2]]", "A1048576", "[]")]
    [InlineData("[[1]]", "Sheet1!A1", "[]")]
    [InlineData("[[1]]", "[other.xlsx]Sheet1!A1", "[]")]
    [InlineData("[[1]]", "A1", "[{\"cell\":\"A1\",\"operation\":\"sum\",\"sourceRange\":\"A1:A1\"}]")]
    [InlineData("[[1]]", "A1", "[{\"cell\":\"A2\",\"operation\":\"sum\",\"sourceRange\":\"A1:B1\"}]")]
    [InlineData("[[\"1\"]]", "A1", "[{\"cell\":\"A2\",\"operation\":\"sum\",\"sourceRange\":\"A1:A1\"}]")]
    [InlineData("[[1]]", "A1", "[{\"cell\":\"A2\",\"operation\":\"shell\",\"sourceRange\":\"A1:A1\"}]")]
    [InlineData("[[1]]", "A1", "[{\"cell\":\"A2\",\"operation\":\"sum\",\"sourceRange\":\"A1:A1\",\"formula\":\"=WEBSERVICE(\\\"x\\\")\"}]")]
    [InlineData("[[1]]", "A1", "[{\"cell\":\"A2\",\"operation\":\"sum\",\"sourceRange\":\"A1:A1\"},{\"cell\":\"A2\",\"operation\":\"sum\",\"sourceRange\":\"A1:A1\"}]")]
    public void InvalidOrUnboundedDefinitionsAreRejected(string rows, string start, string formulas)
    {
        Assert.Throws<InvalidDataException>(() => Parse(rows, start, formulas));
    }

    [Theory]
    [InlineData("permission", "\"Approved\"")]
    [InlineData("bindingId", "\"01234567890123456789012345678901\"")]
    [InlineData("capabilityId", "\"office.excel.workbook.create\"")]
    [InlineData("path", "\"C:\\\\private.xlsx\"")]
    public void OfficeArgumentsCannotSupplyAuthorityOrFileDestinations(string field, string value)
    {
        var json = "{\"tool\":\"excel.workbook.create\",\"worksheetName\":\"Sample\",\"startCell\":\"A1\",\"rows\":[[1]],\"formulas\":[],\"" + field + "\":" + value + "}";
        Assert.Throws<InvalidDataException>(() => ExcelWorkbookDefinition.Parse(JsonDocument.Parse(json).RootElement));
    }

    [Fact]
    public void ParsedDefinitionOutlivesSourceJsonWithoutMutableArrays()
    {
        ExcelWorkbookDefinition definition;
        using (var document = JsonDocument.Parse("{\"tool\":\"excel.workbook.create\",\"worksheetName\":\"Sample\",\"startCell\":\"A1\",\"rows\":[[8]],\"formulas\":[]}"))
            definition = ExcelWorkbookDefinition.Parse(document.RootElement);
        Assert.Equal(8d, Assert.Single(definition.Cells).Value);
        Assert.Throws<NotSupportedException>(() => ((IList<ExcelWorkbookCell>)definition.Cells)[0] = new("A1", ExcelWorkbookCellKind.Number, 99d));
    }

    [Theory]
    [InlineData("[[0.1234567890123456]]")]
    [InlineData("[[1e-324]]")]
    [InlineData("[[1000000000001]]")]
    public void NumbersWhichExcelCannotRetainUnderTheDeclaredContractAreRejected(string rows)
    {
        Assert.Throws<InvalidDataException>(() => Parse(rows, "A1", "[]"));
    }

    [Theory]
    [InlineData(1e-20, 0)]
    [InlineData(1e-100, 0)]
    [InlineData(0, 1e-20)]
    [InlineData(999999999999.99, 1000000000000)]
    [InlineData(1e12, 1000000000001)]
    public void NumericVerificationRejectsLostTinyValuesAndMaterialCurrencyErrors(double expected, double actual)
    {
        Assert.False(ExcelWorkbookDefinition.NumbersMatch(expected, actual));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1e-100)]
    [InlineData(0.1)]
    [InlineData(999999999999.99)]
    public void NumericVerificationAcceptsUnchangedRepresentableValues(double expected)
    {
        Assert.True(ExcelWorkbookDefinition.NumbersMatch(expected, expected));
    }

    [Fact]
    public void NumericCellReadbackCannotSilentlyChangeAnInputValue()
    {
        var cell = new ExcelWorkbookCell("A1", ExcelWorkbookCellKind.Number, 0.1d);
        Assert.Throws<InvalidDataException>(() => ExcelCom.ValidateWorkbookCell(cell, double.BitIncrement(0.1d), false));
        Assert.Throws<InvalidDataException>(() => ExcelCom.ValidateWorkbookCell(
            new("A1", ExcelWorkbookCellKind.Number, 1e-100), 0d, false));
        ExcelCom.ValidateWorkbookCell(cell, 0.1d, false);
    }
}
