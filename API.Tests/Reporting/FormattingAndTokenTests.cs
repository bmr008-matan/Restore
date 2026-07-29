using API.Reporting.Runtime;

namespace API.Tests.Reporting;

public class ValueFormatterTests
{
    [Fact]
    public void NumericFormat_HonoursTheReportCulture()
    {
        var invariant = new ValueFormatter("en-US");
        var german = new ValueFormatter("de-DE");

        Assert.Equal("1,234.50", invariant.Format(1234.5m, "#,##0.00"));
        Assert.Equal("1.234,50", german.Format(1234.5m, "#,##0.00"));
    }

    [Fact]
    public void DateFormat_HonoursTheReportCulture()
    {
        Assert.Equal("31/01/2026", new ValueFormatter("en-GB").Format(new DateTime(2026, 1, 31), "dd/MM/yyyy"));
    }

    [Fact]
    public void HebrewCulture_IsResolvedAndUsed()
    {
        var hebrew = new ValueFormatter("he-IL");

        Assert.Equal("he-IL", hebrew.Culture.Name);
        Assert.Equal("1,234.50", hebrew.Format(1234.5m, "#,##0.00"));
    }

    [Fact]
    public void UnknownCulture_FallsBackToInvariantRatherThanThrowing()
    {
        // A template carrying a culture the server does not have installed should still render.
        var formatter = new ValueFormatter("xx-ZZ-not-a-culture");
        Assert.Equal("1,234.50", formatter.Format(1234.5m, "#,##0.00"));
    }

    [Fact]
    public void NullValue_RendersAsTheSuppliedNullTextOrEmpty()
    {
        var formatter = new ValueFormatter("en-US");

        Assert.Equal("", formatter.Format(null));
        Assert.Equal("-", formatter.Format(null, null, "-"));
    }

    [Fact]
    public void InvalidStandardFormatSpecifier_FallsBackToTheDefaultInsteadOfThrowing()
    {
        // "Q" is a single-character standard specifier that .NET does not define, so it throws
        // FormatException. A printed report showing an unformatted number beats a 500.
        var formatter = new ValueFormatter("en-US");
        Assert.Equal("42", formatter.Format(42, "Q"));
    }

    [Fact]
    public void LiteralTextInAFormatString_IsKept_BecauseThatIsWhatCustomFormatsMean()
    {
        // Pinning real .NET semantics: unrecognised characters in a *custom* format string are
        // literals, not errors. "#,##0.00 kg" is a legitimate format, so the formatter must not try to
        // second-guess which custom formats are "invalid".
        var formatter = new ValueFormatter("en-US");
        Assert.Equal("1,234.50 kg", formatter.Format(1234.5m, "#,##0.00 kg"));
    }

    [Fact]
    public void BooleansGetAReadableDefault()
    {
        var formatter = new ValueFormatter("en-US");
        Assert.Equal("Yes", formatter.Format(true));
        Assert.Equal("No", formatter.Format(false));
    }

    [Theory]
    [InlineData("100", "100")]
    [InlineData("1.5", "1.5")]
    [InlineData("-2.25", "-2.25")]
    [InlineData("abc", null)]
    [InlineData("", null)]
    public void ToDecimal_ParsesNumericTextAndRefusesTheRest(string input, string? expected)
    {
        var result = ValueFormatter.ToDecimal(input);
        Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result);
    }

    [Fact]
    public void ToDecimal_HandlesEveryNumericClrType()
    {
        Assert.Equal(1m, ValueFormatter.ToDecimal(1));
        Assert.Equal(1m, ValueFormatter.ToDecimal(1L));
        Assert.Equal(1m, ValueFormatter.ToDecimal((short)1));
        Assert.Equal(1m, ValueFormatter.ToDecimal(1.0));
        Assert.Equal(1m, ValueFormatter.ToDecimal(1.0f));
        Assert.Null(ValueFormatter.ToDecimal(null));
        Assert.Null(ValueFormatter.ToDecimal(DBNull.Value));
    }
}

public class TokenInterpolatorTests
{
    private readonly TokenInterpolator _interpolator = new(new ValueFormatter("en-US"));
    private readonly RenderDiagnostics _diagnostics = new();

    private ExpressionScope Scope() => TestData
        .Scope(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["fromDate"] = new DateTime(2026, 1, 1),
            ["branch"] = "Tel Aviv"
        })
        .WithRow(TestData.Row("N-1", "Nike", "Shoes", 3, 1234.5m));

    [Fact]
    public void ParameterToken_IsInterpolated()
    {
        var result = _interpolator.Interpolate("Branch: {@branch}", Scope(), _diagnostics, "t");
        Assert.Equal("Branch: Tel Aviv", result);
    }

    [Fact]
    public void FieldToken_IsInterpolatedFromTheCurrentRow()
    {
        Assert.Equal("Nike", _interpolator.Interpolate("{brand}", Scope(), _diagnostics, "t"));
    }

    [Fact]
    public void TokenFormat_IsApplied()
    {
        Assert.Equal("1,234.50", _interpolator.Interpolate("{total:#,##0.00}", Scope(), _diagnostics, "t"));
        Assert.Equal("01/01/2026", _interpolator.Interpolate("{@fromDate:dd/MM/yyyy}", Scope(), _diagnostics, "t"));
    }

    [Fact]
    public void ReportMetadataTokens_Resolve()
    {
        var result = _interpolator.Interpolate("{ReportTitle} on {Date:yyyy-MM-dd} by {CurrentUser}",
            Scope(), _diagnostics, "t");

        Assert.Equal("Sales by Brand on 2026-07-29 by tester", result);
    }

    [Fact]
    public void PageTokens_ArePassedThroughUntouchedForChromiumToSubstitute()
    {
        // Only Chromium can resolve these, and only inside its header and footer templates.
        var result = _interpolator.Interpolate("Page {PageNumber} of {TotalPages}", Scope(), _diagnostics, "t");

        Assert.Equal("Page {PageNumber} of {TotalPages}", result);
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void ScopedTokens_DisambiguateBetweenRowAndParameter()
    {
        var scope = TestData
            .Scope(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["brand"] = "FromParam" })
            .WithRow(TestData.Row("N-1", "FromRow", "Shoes", 1, 1m));

        Assert.Equal("FromRow", _interpolator.Interpolate("{Fields.brand}", scope, _diagnostics, "t"));
        Assert.Equal("FromParam", _interpolator.Interpolate("{Params.brand}", scope, _diagnostics, "t"));
        // A bare token prefers the current row, which is what dragging a field onto a band means.
        Assert.Equal("FromRow", _interpolator.Interpolate("{brand}", scope, _diagnostics, "t"));
    }

    [Fact]
    public void GroupAggregateToken_Resolves()
    {
        var scope = Scope().WithGroup(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SumOfTotal"] = 1300m,
            ["Count"] = 3L
        });

        Assert.Equal("3 rows totalling 1,300.00",
            _interpolator.Interpolate("{Count} rows totalling {SumOfTotal:#,##0.00}", scope, _diagnostics, "t"));
    }

    [Fact]
    public void UnknownToken_IsLeftInPlaceAndReported()
    {
        var result = _interpolator.Interpolate("Hello {nope}", Scope(), _diagnostics, "band.1");

        Assert.Equal("Hello {nope}", result);
        Assert.Contains("Unknown token", _diagnostics.ToString());
    }

    [Fact]
    public void TextWithoutTokens_IsReturnedUnchanged()
    {
        Assert.Equal("plain text", _interpolator.Interpolate("plain text", Scope(), _diagnostics, "t"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void NullFieldToken_RendersEmptyRatherThanTheWordNull()
    {
        var scope = TestData.Scope().WithRow(TestData.Row("N-1", "Nike", "Shoes", 1, null));
        Assert.Equal("Total: ", _interpolator.Interpolate("Total: {total}", scope, _diagnostics, "t"));
    }

    [Fact]
    public void ContainsPageToken_DetectsThemForTheValidator()
    {
        Assert.True(TokenInterpolator.ContainsPageToken("Page {PageNumber}"));
        Assert.True(TokenInterpolator.ContainsPageToken("{TotalPages}"));
        Assert.False(TokenInterpolator.ContainsPageToken("{brand}"));
        Assert.False(TokenInterpolator.ContainsPageToken(null));
    }
}
