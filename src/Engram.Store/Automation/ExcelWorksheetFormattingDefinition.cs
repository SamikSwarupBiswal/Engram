using System.Globalization;
using System.Text.Json;

namespace Engram.Store.Automation;

/// <summary>Model-proposed formatting options; contains no workbook identity or execution authority.</summary>
public sealed class ExcelWorksheetFormattingDefinition
{
    private static readonly string[] Properties =
        ["tool", "headerRow", "boldHeader", "freezeHeader", "autoFitColumns", "currencyColumns", "currencyCode"];

    private ExcelWorksheetFormattingDefinition(int headerRow, bool boldHeader, bool freezeHeader,
        bool autoFitColumns, string[] currencyColumns, string currencyCode)
    {
        HeaderRow = headerRow;
        BoldHeader = boldHeader;
        FreezeHeader = freezeHeader;
        AutoFitColumns = autoFitColumns;
        CurrencyColumns = Array.AsReadOnly(currencyColumns);
        CurrencyCode = currencyCode;
    }

    public int HeaderRow { get; }
    public bool BoldHeader { get; }
    public bool FreezeHeader { get; }
    public bool AutoFitColumns { get; }
    public IReadOnlyList<string> CurrencyColumns { get; }
    public string CurrencyCode { get; }

    public static ExcelWorksheetFormattingDefinition Parse(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Worksheet formatting arguments must be an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
            if (!Properties.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new InvalidDataException("Worksheet formatting arguments contain an unknown or duplicate property.");
        if (seen.Count != Properties.Length)
            throw new InvalidDataException("Worksheet formatting arguments are incomplete.");
        if (Text(arguments.GetProperty("tool")) != "excel.worksheet.format")
            throw new InvalidDataException("Choose a supported worksheet formatting tool.");
        var row = arguments.GetProperty("headerRow");
        if (row.ValueKind != JsonValueKind.Number || !row.TryGetInt32(out var headerRow) || headerRow is < 1 or > 1_048_576)
            throw new InvalidDataException("Choose a header row within the Excel worksheet.");
        var bold = Flag(arguments, "boldHeader");
        var freeze = Flag(arguments, "freezeHeader");
        var autoFit = Flag(arguments, "autoFitColumns");
        var columns = arguments.GetProperty("currencyColumns");
        if (columns.ValueKind != JsonValueKind.Array || columns.GetArrayLength() > 32)
            throw new InvalidDataException("Choose up to 32 currency columns by their existing header names.");
        var selected = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns.EnumerateArray())
        {
            var name = Text(column);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl) || !names.Add(name.Trim()))
                throw new InvalidDataException("Currency column names must be nonempty, distinct text of at most 128 characters.");
            selected.Add(name.Trim());
        }
        var currency = Text(arguments.GetProperty("currencyCode"));
        if (currency is not ("local" or "USD" or "EUR" or "GBP" or "INR" or "JPY"))
            throw new InvalidDataException("Choose local currency or a supported currency code: USD, EUR, GBP, INR, JPY.");
        if (!bold && !freeze && !autoFit && selected.Count == 0)
            throw new InvalidDataException("Choose at least one worksheet formatting operation.");
        return new(headerRow, bold, freeze, autoFit, selected.ToArray(), currency);
    }

    /// <summary>Resolves names against a freshly observed, bounded header row; ambiguous names are never guessed.</summary>
    public IReadOnlyList<int> ResolveCurrencyColumns(IReadOnlyList<string?> headers, int firstColumn)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.Count > 256 || firstColumn < 1 || firstColumn > 16_384 || firstColumn + (long)headers.Count - 1 > 16_384)
            throw new InvalidDataException("The observed header row exceeds the supported worksheet column bounds.");
        var characters = 0;
        foreach (var header in headers)
        {
            var length = header?.Length ?? 0;
            if (length > 2048 || length > 262_144 - characters)
                throw new InvalidDataException("The observed header text exceeds the supported read budget.");
            characters += length;
        }
        var resolved = new List<int>();
        foreach (var requested in CurrencyColumns)
        {
            var matches = Enumerable.Range(0, headers.Count).Where(index =>
                string.Equals(headers[index]?.Trim(), requested, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Currency column '{requested}' is missing or ambiguous in the selected header row.");
            resolved.Add(firstColumn + matches[0]);
        }
        return resolved.AsReadOnly();
    }

    public string CurrencyNumberFormat(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var symbol = CurrencyCode switch
        {
            "USD" => "$", "EUR" => "€", "GBP" => "£", "INR" => "₹", "JPY" => "¥",
            _ => culture.NumberFormat.CurrencySymbol
        };
        var digits = CurrencyCode == "local" ? culture.NumberFormat.CurrencyDecimalDigits : CurrencyCode == "JPY" ? 0 : 2;
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 8 || digits is < 0 or > 6 ||
            symbol.Any(character => !char.IsLetter(character) && character != '.' && character != ' ' &&
                char.GetUnicodeCategory(character) != UnicodeCategory.CurrencySymbol))
            throw new InvalidDataException("The selected currency display settings cannot be safely represented in Excel.");
        return "\"" + symbol + "\"#,##0" + (digits == 0 ? "" : "." + new string('0', digits));
    }

    private static bool Flag(JsonElement arguments, string property) => arguments.GetProperty(property).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new InvalidDataException("Worksheet formatting choices must be boolean values.")
    };

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new InvalidDataException("Worksheet formatting names must be literal text.");
}
