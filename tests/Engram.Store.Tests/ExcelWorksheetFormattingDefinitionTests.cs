using System.Globalization;
using System.Text.Json;
using Engram.Store.Automation;

namespace Engram.Store.Tests;

public sealed class ExcelWorksheetFormattingDefinitionTests
{
    private static string Arguments(bool bold = false, bool freeze = false, bool autoFit = true,
        string[]? columns = null, string currency = "local", int row = 1) => JsonSerializer.Serialize(new
        { tool = "excel.worksheet.format", headerRow = row, boldHeader = bold, freezeHeader = freeze,
            autoFitColumns = autoFit, currencyColumns = columns ?? [], currencyCode = currency });

    private static ExcelWorksheetFormattingDefinition Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ExcelWorksheetFormattingDefinition.Parse(document.RootElement);
    }

    [Fact]
    public void ModelSelectsOperationsHeaderAndNamedColumnsWithoutAnAmountTemplate()
    {
        var definition = Parse(Arguments(bold: true, autoFit: false, columns: ["Net total", "Tax"], currency: "INR", row: 7));
        Assert.Equal(7, definition.HeaderRow);
        Assert.True(definition.BoldHeader);
        Assert.False(definition.FreezeHeader);
        Assert.False(definition.AutoFitColumns);
        Assert.Equal(new[] { 6, 5 }, definition.ResolveCurrencyColumns(["Item", " Tax ", "NET TOTAL"], 4));
        Assert.Equal("\"₹\"#,##0.00", definition.CurrencyNumberFormat(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void UnrequestedOperationsRemainUnselectedAndNoCurrencyColumnIsInvented()
    {
        var definition = Parse(Arguments(freeze: true, autoFit: false));
        Assert.False(definition.BoldHeader);
        Assert.True(definition.FreezeHeader);
        Assert.False(definition.AutoFitColumns);
        Assert.Empty(definition.ResolveCurrencyColumns(["Amount", "Sales"], 1));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Sales")]
    public void MissingOrAmbiguousRequestedColumnsAreRejectedBeforeMutation(string requested)
    {
        var definition = Parse(Arguments(columns: [requested]));
        Assert.Throws<InvalidDataException>(() => definition.ResolveCurrencyColumns(["Sales", " sales "], 1));
    }

    [Fact]
    public void DuplicateUnselectedHeadersDoNotPreventResolvingAnUnambiguousColumn()
    {
        var definition = Parse(Arguments(columns: ["Total"]));
        Assert.Equal(new[] { 3 }, definition.ResolveCurrencyColumns(["Other", "Other", "Total"], 1));
    }

    [Theory]
    [InlineData("USD", "\"$\"#,##0.00")]
    [InlineData("EUR", "\"€\"#,##0.00")]
    [InlineData("GBP", "\"£\"#,##0.00")]
    [InlineData("JPY", "\"¥\"#,##0")]
    public void ExplicitCurrencyCodesDoNotDependOnTheMachinesLocale(string code, string expected)
    {
        Assert.Equal(expected, Parse(Arguments(currency: code)).CurrencyNumberFormat(CultureInfo.GetCultureInfo("en-IN")));
    }

    [Fact]
    public void LocalCurrencyUsesTheActualSelectedLocaleRatherThanAssumingDollars()
    {
        Assert.Equal("\"₹\"#,##0.00", Parse(Arguments()).CurrencyNumberFormat(CultureInfo.GetCultureInfo("en-IN")));
    }

    [Theory]
    [InlineData("\";[Red]0")]
    [InlineData("\\0")]
    [InlineData("[USD]")]
    [InlineData("\n$")]
    public void CustomizedCurrencySettingsCannotInjectExcelFormatOperators(string symbol)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.CurrencySymbol = symbol;
        Assert.Throws<InvalidDataException>(() => Parse(Arguments()).CurrencyNumberFormat(culture));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_048_577)]
    public void HeaderRowMustRemainInsideTheWorksheet(int row)
    {
        Assert.Throws<InvalidDataException>(() => Parse(Arguments(row: row)));
    }

    [Fact]
    public void ArgumentsCannotAddAuthorityDuplicatesOrUnsupportedValues()
    {
        var json = Arguments();
        foreach (var invalid in new[] { json.Replace("\"headerRow\":1", "\"headerRow\":1.5"),
            json.Replace("\"boldHeader\":false", "\"boldHeader\":\"false\""),
            json.Replace("\"headerRow\":1", "\"headerRow\":1,\"headerRow\":2"),
            json.Insert(1, "\"bindingId\":\"invented\","), json.Insert(1, "\"permission\":\"Approved\","),
            Arguments(currency: "=WEBSERVICE()"), Arguments(autoFit: false),
            Arguments(columns: ["Total", " total "]), Arguments(columns: [""]), Arguments(columns: ["Line\nbreak"]),
            Arguments(columns: Enumerable.Range(0, 33).Select(i => "Column " + i).ToArray()) })
            Assert.Throws<InvalidDataException>(() => Parse(invalid));
    }

    [Fact]
    public void DefinitionsRemainImmutableAfterJsonDisposal()
    {
        var definition = Parse(Arguments(columns: ["Total"]));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)definition.CurrencyColumns)[0] = "Other");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(16_384, 2)]
    [InlineData(1, 257)]
    public void HeaderObservationMustStayWithinBoundedColumns(int firstColumn, int count)
    {
        var definition = Parse(Arguments(columns: ["Total"]));
        Assert.Throws<InvalidDataException>(() => definition.ResolveCurrencyColumns(Enumerable.Repeat<string?>("Total", count).ToArray(), firstColumn));
    }

    [Fact]
    public void HeaderTextBudgetIsCheckedBeforeTrimmingOrMatching()
    {
        var definition = Parse(Arguments(columns: ["Total"]));
        var oversizedHeader = Assert.Throws<InvalidDataException>(() =>
            definition.ResolveCurrencyColumns(["Total", new string(' ', 2049)], 1));
        var oversizedRow = Assert.Throws<InvalidDataException>(() => definition.ResolveCurrencyColumns(
            new string?[] { "Total" }.Concat(Enumerable.Repeat<string?>(new string('x', 2048), 129)).ToArray(), 1));
        Assert.Contains("read budget", oversizedHeader.Message);
        Assert.Contains("read budget", oversizedRow.Message);
    }
}
