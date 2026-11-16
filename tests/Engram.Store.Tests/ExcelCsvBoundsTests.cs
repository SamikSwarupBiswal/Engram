using System.Text;
using Engram.Store.Automation;

namespace Engram.Store.Tests;

public sealed class ExcelCsvBoundsTests
{
    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"\"\n")]
    [InlineData("\"\"\r\n")]
    public void ParserPreservesQuotedEmptyDocumentField(string text)
    {
        Assert.Equal("", Assert.Single(Assert.Single(CsvDocument.Parse(text).Rows)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ParserPreservesTrailingQuotedEmptyRowAndCleanupRejectsItsEmptyDate(string ending)
    {
        var document = CsvDocument.Parse("Date\n\"\"" + ending);

        Assert.Equal(2, document.Rows.Count);
        Assert.Equal("", Assert.Single(document.Rows[1]));
        var error = Assert.Throws<InvalidDataException>(() => document.CleanAndSortByDateDescending());
        Assert.Contains("invalid", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ParserPreservesTrailingQuotedEmptyFieldAfterOtherColumns(string ending)
    {
        var document = CsvDocument.Parse("Name,Note\nAlice,\"\"" + ending);

        Assert.Equal(2, document.Rows.Count);
        Assert.Equal(new[] { "Alice", "" }, document.Rows[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    public void ParserKeepsQuotedCommaNextToEmptyQuotedField(string ending)
    {
        var document = CsvDocument.Parse("\"\",\"has,comma\"" + ending);
        Assert.Equal(new[] { "", "has,comma" }, Assert.Single(document.Rows));
    }

    [Fact]
    public void ParserStillRejectsPlainEmptyFileAndDoesNotInventARowAfterFinalNewline()
    {
        Assert.Throws<InvalidDataException>(() => CsvDocument.Parse(""));
        Assert.Equal("Date", Assert.Single(Assert.Single(CsvDocument.Parse("Date\n").Rows)));
    }

    [Fact]
    public void ParserRejectsTooManyColumnsIncludingEmptyFields()
    {
        Assert.Throws<InvalidDataException>(() => CsvDocument.Parse(new string(',', 256)));
    }

    [Fact]
    public void ParserRejectsTooManyCellsIncludingEmptyFields()
    {
        var text = string.Concat(Enumerable.Repeat(",,,,,,,,,\n", 1001));
        Assert.Throws<InvalidDataException>(() => CsvDocument.Parse(text));
    }

    [Fact]
    public void ParserAcceptsCellAndRowBudgetBoundary()
    {
        var csv = CsvDocument.Parse(string.Concat(Enumerable.Repeat("1\n", 10_000)));
        Assert.Equal(10_000, csv.Rows.Count);
        Assert.All(csv.Rows, row => Assert.Equal("1", Assert.Single(row)));
    }

    [Fact]
    public void ParserRejectsMoreThanTenThousandRows()
    {
        Assert.Throws<InvalidDataException>(() => CsvDocument.Parse(new string('\n', 10_001)));
    }

    [Fact]
    public void ParserAcceptsColumnBudgetBoundary()
    {
        var csv = CsvDocument.Parse(new string(',', 255));
        Assert.Equal(256, Assert.Single(csv.Rows).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParserBoundsDecodedFieldText(bool quoted)
    {
        var valid = new string('x', 32_767);
        string Wrap(string text) => quoted ? "\"" + text + "\"" : text;
        Assert.Equal(valid, Assert.Single(Assert.Single(CsvDocument.Parse(Wrap(valid)).Rows)));
        Assert.Throws<InvalidDataException>(() => CsvDocument.Parse(Wrap(valid + "x")));
    }

    [Fact]
    public void ParserBoundsTotalDecodedTextAcrossFields()
    {
        var fields = Enumerable.Repeat(new string('x', 32_767), 8).Append("12345678").ToArray();
        Assert.Equal(262_144, CsvDocument.Parse(string.Join(',', fields)).Rows[0].Sum(field => field.Length));
        fields[^1] += "9";
        Assert.Throws<InvalidDataException>(() => CsvDocument.Parse(string.Join(',', fields)));
    }

    [Fact]
    public void ParserCountsEscapedQuotesAsDecodedFieldCharacters()
    {
        var valid = "\"" + string.Concat(Enumerable.Repeat("\"\"", 32_767)) + "\"";
        Assert.Equal(new string('"', 32_767), Assert.Single(Assert.Single(CsvDocument.Parse(valid).Rows)));
        Assert.Throws<InvalidDataException>(() => CsvDocument.Parse("\"" + string.Concat(Enumerable.Repeat("\"\"", 32_768)) + "\""));
    }

    [Fact]
    public void BindingRejectsOversizedSourceBeforeRetainingContext()
    {
        var directory = Directory.CreateTempSubdirectory("engram-csv-bounds-").FullName;
        try
        {
            var source = Path.Combine(directory, "large.csv");
            using (var stream = File.Create(source)) stream.SetLength(4 * 1024 * 1024 + 1);
            var registry = new ExcelAdvancedContextBindingRegistry();

            Assert.Throws<InvalidDataException>(() => registry.BindCsvCleanup(new() { Path = source }));

            Assert.Null(registry.CurrentCsvCleanup);
            Assert.Equal(4 * 1024 * 1024 + 1, new FileInfo(source).Length);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void BindingRejectsExcessiveCellsBeforeRetainingContext()
    {
        var directory = Directory.CreateTempSubdirectory("engram-csv-bounds-").FullName;
        try
        {
            var source = Path.Combine(directory, "many-cells.csv");
            File.WriteAllText(source, string.Concat(Enumerable.Repeat(",,,,,,,,,\n", 1001)));
            var registry = new ExcelAdvancedContextBindingRegistry();

            Assert.Throws<InvalidDataException>(() => registry.BindCsvCleanup(new() { Path = source }));

            Assert.Null(registry.CurrentCsvCleanup);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupRejectsChangedOrNewlyOversizedSourceBeforeExcel(bool oversized)
    {
        var directory = Directory.CreateTempSubdirectory("engram-csv-bounds-").FullName;
        try
        {
            var source = Path.Combine(directory, "sales.csv");
            File.WriteAllText(source, "Date,Amount\n2026-01-01,10\n", new UTF8Encoding(true));
            var registry = new ExcelAdvancedContextBindingRegistry();
            var binding = registry.BindCsvCleanup(new() { Path = source });
            if (oversized)
            {
                using var stream = new FileStream(source, FileMode.Open, FileAccess.Write);
                stream.SetLength(4 * 1024 * 1024 + 1);
            }
            else File.WriteAllText(source, "Date,Amount\n2026-01-02,20\n");

            Assert.Throws<InvalidDataException>(() => registry.CleanCsv(binding.BindingId));

            Assert.False(File.Exists(binding.OutputPath));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void BoundedCsvKeepsQuotedUnicodeAndCleanupSemantics()
    {
        var document = CsvDocument.Parse("Date,Name,Note\r\n2026-01-01, Alice ,\"অভিনন্দন, hello\"\r\n" +
            "2026-01-03,Bob,\"said \"\"hello\"\"\"\r\n2026-01-01, Alice ,\"অভিনন্দন, hello\"\r\n");
        var cleaned = document.CleanAndSortByDateDescending();

        Assert.Equal(3, cleaned.Rows.Count);
        Assert.Equal("2026-01-03", cleaned.Rows[1][0]);
        Assert.Equal("Alice", cleaned.Rows[2][1]);
        Assert.Equal("অভিনন্দন, hello", cleaned.Rows[2][2]);
        cleaned.AssertEquivalent(CsvDocument.Parse(cleaned.Serialize()));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("rows")]
    [InlineData("columns")]
    [InlineData("cells")]
    [InlineData("field")]
    [InlineData("text")]
    public void MutatedDocumentMustPassBoundsAgainBeforeExcelAllocation(string change)
    {
        var document = CsvDocument.Parse("Date\n2026-01-01\n");
        switch (change)
        {
            case "empty": document.Rows.Clear(); break;
            case "rows": document.Rows.AddRange(Enumerable.Range(0, 10_000).Select(_ => new List<string> { "" })); break;
            case "columns": document.Rows[0].AddRange(Enumerable.Repeat("", 256)); break;
            case "cells":
                document.Rows.Clear();
                document.Rows.AddRange(Enumerable.Range(0, 101).Select(_ => Enumerable.Repeat("", 100).ToList()));
                break;
            case "field": document.Rows[1][0] = new string('x', 32_768); break;
            case "text":
                document.Rows.Clear();
                document.Rows.Add(Enumerable.Repeat(new string('x', 32_767), 9).ToList());
                break;
        }

        Assert.Throws<InvalidDataException>(document.ValidateBounds);
    }
}
