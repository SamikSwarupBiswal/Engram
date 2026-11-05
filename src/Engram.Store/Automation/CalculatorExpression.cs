using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Engram.Store.Automation;

internal readonly record struct CalculatorExpression(string Left, char Operator, string Right, decimal Expected)
{
    private static readonly Regex ExpressionPattern = new(
        @"\A[ \t]*([0-9]{1,6}(?:\.[0-9]{1,3})?)[ \t]*([+*/-])[ \t]*([0-9]{1,6}(?:\.[0-9]{1,3})?)[ \t]*\z",
        RegexOptions.CultureInvariant);

    public static CalculatorExpression Parse(string? value)
    {
        if (value == null || value.Length > 64 || ExpressionPattern.Match(value) is not { Success: true } match)
            throw new InvalidOperationException("Calculator requires two nonnegative numbers (up to six whole and three decimal digits each) with one of +, -, *, or / between them.");

        var leftText = match.Groups[1].Value;
        var operation = match.Groups[2].Value[0];
        var rightText = match.Groups[3].Value;
        var left = decimal.Parse(leftText, CultureInfo.InvariantCulture);
        var right = decimal.Parse(rightText, CultureInfo.InvariantCulture);
        if (operation == '/' && right == 0)
            throw new InvalidOperationException("Calculator cannot divide by zero.");

        var expected = operation switch
        {
            '+' => left + right,
            '-' => left - right,
            '*' => left * right,
            '/' => left / right,
            _ => throw new InvalidOperationException("Calculator operator is unavailable.")
        };
        return new CalculatorExpression(leftText, operation, rightText, expected);
    }

    public bool Matches(decimal displayed)
    {
        if (displayed == Expected) return true;

        // Standard mode uses 16 digits. A fixed-point fraction can spend some
        // of that display space on leading zeros, unlike exponential notation.
        var magnitude = Math.Abs(Expected);
        var wholeDigits = magnitude < 1 ? 0 : decimal.Truncate(magnitude).ToString(CultureInfo.InvariantCulture).Length;
        if (displayed == decimal.Round(Expected, 16 - wholeDigits, MidpointRounding.AwayFromZero)) return true;
        return magnitude < 1 && displayed == decimal.Parse(Expected.ToString("G16", CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public static bool TryReadDisplay(string name, CultureInfo culture, out string number, out decimal value)
    {
        var normalized = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Format) continue;
            if (category == UnicodeCategory.DecimalDigitNumber)
                normalized.Append((char)('0' + (int)Rune.GetNumericValue(rune)));
            else if (rune.Value == '\u2212')
                normalized.Append('-');
            else
                normalized.Append(rune.ToString());
        }

        var display = normalized.ToString();
        var start = display.AsSpan().IndexOfAnyInRange('0', '9');
        if (start > 0 && display[start - 1] is '+' or '-') start--;
        number = start >= 0 ? display[start..].Trim() : string.Empty;
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.NegativeSign = "-";
        format.PositiveSign = "+";
        return decimal.TryParse(number, NumberStyles.Float | NumberStyles.AllowThousands, format, out value);
    }
}
