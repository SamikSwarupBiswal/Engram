using System.Globalization;
using Engram.Store.Automation;

namespace Engram.Store.Tests;

public sealed class CalculatorExpressionTests
{
    [Theory]
    [InlineData(" 42 * 37 ", "1554")]
    [InlineData("0.5 + 1.25", "1.75")]
    [InlineData("4-11", "-7")]
    [InlineData("5/2", "2.5")]
    public void ParseEvaluatesSupportedBinaryOperations(string input, string expected)
    {
        var expression = CalculatorExpression.Parse(input);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), expression.Expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2 * 3 =")]
    [InlineData("1 + 2 + 3")]
    [InlineData("1e4 * 3")]
    [InlineData("1000000 * 3")]
    [InlineData("1 / 0.000")]
    [InlineData("7 * 8\n")]
    [InlineData("1+2; Remove-Item *")]
    public void ParseRejectsUnsupportedOrUnsafeExpressions(string? input)
    {
        Assert.Throws<InvalidOperationException>(() => CalculatorExpression.Parse(input));
    }

    [Theory]
    [InlineData("en-US", "Display is 12,345.67", "12345.67")]
    [InlineData("de-DE", "Anzeige ist −12.345,67", "-12345.67")]
    [InlineData("ar-EG", "العرض \u061C-١٢٬٣٤٥٫٦٧", "-12345.67")]
    public void ReadsNumericResultFromLocalizedDisplay(string locale, string display, string expected)
    {
        Assert.True(CalculatorExpression.TryReadDisplay(display, CultureInfo.GetCultureInfo(locale), out _, out var value));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), value);
    }

    [Fact]
    public void ComparesDisplayedPrecisionWithoutAcceptingAnIncorrectResult()
    {
        Assert.True(CalculatorExpression.Parse("1/3").Matches(0.3333333333333333m));
        Assert.False(CalculatorExpression.Parse("1/3").Matches(0.3333m));
        Assert.False(CalculatorExpression.Parse("1/3").Matches(decimal.MinValue));
        Assert.False(CalculatorExpression.Parse("2*3").Matches(6.000000000000001m));
    }

    [Fact]
    public void VerifiesLargeProductRoundedToStandardDisplayPrecision()
    {
        var expression = CalculatorExpression.Parse("999999.999*999999.999");

        Assert.True(expression.Matches(999999998000m));
        Assert.False(expression.Matches(999999998000.0001m));
    }

    [Fact]
    public void VerifiesFixedPointAndExponentialPrecisionForSmallFractions()
    {
        var expression = CalculatorExpression.Parse("1/300");

        Assert.True(expression.Matches(0.0033333333333333m));
        Assert.True(expression.Matches(0.003333333333333333m));
        Assert.False(expression.Matches(0.0033333333333334m));
    }
}
