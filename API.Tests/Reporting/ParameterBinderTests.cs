using System.Text.Json;
using API.Reporting.Model;
using API.Reporting.Runtime;

namespace API.Tests.Reporting;

public class ParameterBinderTests
{
    /// <summary>Fixed clock so the relative-date defaults are assertable.</summary>
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 14, 30, 0, TimeSpan.Zero);

    private readonly ParameterBinder _binder = new(new FixedTimeProvider(Now));

    private static ReportParameter Param(
        string name, ParameterType type, bool required = false, string? @default = null) =>
        new() { Name = name, Type = type, Required = required, DefaultValue = @default };

    private static Dictionary<string, object?> Supplied(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void MissingRequiredParameter_IsRejectedWithItsLabel()
    {
        var declared = new List<ReportParameter> { new() { Name = "fromDate", Label = "From date", Type = ParameterType.Date, Required = true } };

        var result = _binder.Bind(declared, Supplied());

        Assert.False(result.IsValid);
        Assert.Contains("From date", result.Errors.Single().Message);
        Assert.Equal("fromDate", result.Errors.Single().Parameter);
    }

    [Fact]
    public void EveryOffendingParameter_IsReportedInOneGo()
    {
        // Callers should not have to discover problems one round trip at a time.
        var declared = new List<ReportParameter>
        {
            Param("a", ParameterType.Int, required: true),
            Param("b", ParameterType.Date, required: true),
            Param("c", ParameterType.Decimal)
        };

        var result = _binder.Bind(declared, Supplied(("c", "not-a-number")));

        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void UnknownParameter_IsRejectedRatherThanIgnored()
    {
        // Silently dropping a value is how a report quietly returns the wrong rows.
        var result = _binder.Bind(new List<ReportParameter> { Param("known", ParameterType.Text) },
            Supplied(("known", "x"), ("typo", "y")));

        Assert.False(result.IsValid);
        Assert.Contains("no parameter called \"typo\"", result.Errors.Single().Message);
    }

    [Fact]
    public void OptionalMissingParameter_BindsToNull()
    {
        var result = _binder.Bind(new List<ReportParameter> { Param("x", ParameterType.Text) }, Supplied());

        Assert.True(result.IsValid);
        Assert.Null(result.Values["x"]);
    }

    [Theory]
    [InlineData("@Today", 2026, 7, 29)]
    [InlineData("@Yesterday", 2026, 7, 28)]
    [InlineData("@StartOfMonth", 2026, 7, 1)]
    [InlineData("@EndOfMonth", 2026, 7, 31)]
    [InlineData("@StartOfYear", 2026, 1, 1)]
    [InlineData("@EndOfYear", 2026, 12, 31)]
    public void RelativeDateDefaults_Resolve(string token, int year, int month, int day)
    {
        var declared = new List<ReportParameter> { Param("d", ParameterType.Date, @default: token) };

        var result = _binder.Bind(declared, Supplied());

        Assert.True(result.IsValid);
        Assert.Equal(new DateTime(year, month, day), result.Values["d"]);
    }

    [Fact]
    public void CurrentUserDefault_Resolves()
    {
        var declared = new List<ReportParameter> { Param("who", ParameterType.Text, @default: "@CurrentUser") };

        var result = _binder.Bind(declared, Supplied(), currentUser: "matan");

        Assert.Equal("matan", result.Values["who"]);
    }

    [Fact]
    public void DefaultApplies_WhenAnEmptyStringIsSupplied()
    {
        var declared = new List<ReportParameter> { Param("d", ParameterType.Date, @default: "@Today") };

        var result = _binder.Bind(declared, Supplied(("d", "")));

        Assert.Equal(new DateTime(2026, 7, 29), result.Values["d"]);
    }

    [Fact]
    public void JsonElements_AreUnwrappedToPlainClrScalars()
    {
        // Parameters arrive as JSON; letting JsonElement reach an expression would make every
        // comparison behave strangely.
        var json = JsonDocument.Parse("""{"n": 42, "d": "2026-03-01", "b": true, "s": "hi"}""").RootElement;

        var declared = new List<ReportParameter>
        {
            Param("n", ParameterType.Int),
            Param("d", ParameterType.Date),
            Param("b", ParameterType.Bool),
            Param("s", ParameterType.Text)
        };

        var result = _binder.Bind(declared, Supplied(
            ("n", json.GetProperty("n")),
            ("d", json.GetProperty("d")),
            ("b", json.GetProperty("b")),
            ("s", json.GetProperty("s"))));

        Assert.True(result.IsValid);
        Assert.Equal(42L, result.Values["n"]);
        Assert.Equal(new DateTime(2026, 3, 1), result.Values["d"]);
        Assert.Equal(true, result.Values["b"]);
        Assert.Equal("hi", result.Values["s"]);
    }

    [Fact]
    public void IntParameter_RejectsNonNumericText()
    {
        var result = _binder.Bind(new List<ReportParameter> { Param("n", ParameterType.Int) },
            Supplied(("n", "abc")));

        Assert.False(result.IsValid);
        Assert.Contains("not a valid Int", result.Errors.Single().Message);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    [InlineData("true", true)]
    public void BoolParameter_AcceptsTheFormsIntegrationsActuallySend(string input, bool expected)
    {
        var result = _binder.Bind(new List<ReportParameter> { Param("b", ParameterType.Bool) },
            Supplied(("b", input)));

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Values["b"]);
    }

    [Fact]
    public void DateParameter_UsesInvariantParsing_SoLocaleCannotChangeTheMeaning()
    {
        var result = _binder.Bind(new List<ReportParameter> { Param("d", ParameterType.Date) },
            Supplied(("d", "2026-01-31")));

        Assert.Equal(new DateTime(2026, 1, 31), result.Values["d"]);
    }

    [Fact]
    public void MultiSelect_BindsToAList()
    {
        var result = _binder.Bind(new List<ReportParameter> { Param("branches", ParameterType.MultiSelect) },
            Supplied(("branches", new object?[] { 3L, 7L })));

        Assert.True(result.IsValid);
        var list = Assert.IsAssignableFrom<IReadOnlyList<object?>>(result.Values["branches"]);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void MultiSelect_MissingAndOptional_BindsToAnEmptyListNotNull()
    {
        // An expression doing Params.branches.Count must not hit a null reference.
        var result = _binder.Bind(new List<ReportParameter> { Param("branches", ParameterType.MultiSelect) },
            Supplied());

        var list = Assert.IsAssignableFrom<IEnumerable<object?>>(result.Values["branches"]);
        Assert.Empty(list);
    }

    [Fact]
    public void DateRange_RequiresExactlyTwoDates()
    {
        var declared = new List<ReportParameter> { Param("range", ParameterType.DateRange) };

        var tooFew = _binder.Bind(declared, Supplied(("range", new object?[] { "2026-01-01" })));
        Assert.False(tooFew.IsValid);
        Assert.Contains("exactly two dates", tooFew.Errors.Single().Message);

        var justRight = _binder.Bind(declared,
            Supplied(("range", new object?[] { "2026-01-01", "2026-06-30" })));
        Assert.True(justRight.IsValid);
    }

    [Fact]
    public void ParameterNames_AreMatchedCaseInsensitively()
    {
        var result = _binder.Bind(new List<ReportParameter> { Param("fromDate", ParameterType.Date) },
            Supplied(("FROMDATE", "2026-02-02")));

        Assert.True(result.IsValid);
        Assert.Equal(new DateTime(2026, 2, 2), result.Values["fromDate"]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
