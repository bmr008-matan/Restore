using API.Reporting.Data;
using API.Reporting.Model;
using API.Reporting.Runtime;

namespace API.Tests.Reporting;

public class AggregatorTests
{
    private static List<ReportRow> Rows(params object?[] totals) =>
        totals.Select((t, i) => new ReportRow(new Dictionary<string, object?>
        {
            ["total"] = t,
            ["name"] = $"row{i}"
        })).ToList();

    [Fact]
    public void Sum_SkipsNullsRatherThanTreatingThemAsZero()
    {
        var result = Aggregator.Compute(AggregateFunction.Sum, "total", Rows(100m, null, 200m));
        Assert.Equal(300m, result);
    }

    [Fact]
    public void Avg_DividesByTheCountOfPresentValuesOnly()
    {
        // Three rows but one null: the average is over two values, not three. Getting this wrong is
        // the classic reporting bug where a null silently drags an average down.
        var result = Aggregator.Compute(AggregateFunction.Avg, "total", Rows(100m, null, 200m));
        Assert.Equal(150m, result);
    }

    [Fact]
    public void Sum_OfAllNulls_IsNullNotZero()
    {
        Assert.Null(Aggregator.Compute(AggregateFunction.Sum, "total", Rows(null, null)));
    }

    [Fact]
    public void Sum_OfEmptyRowSet_IsNull()
    {
        Assert.Null(Aggregator.Compute(AggregateFunction.Sum, "total", Rows()));
    }

    [Fact]
    public void Count_CountsRowsIncludingNulls_AndNeedsNoField()
    {
        Assert.Equal(3L, Aggregator.Compute(AggregateFunction.Count, null, Rows(1m, null, 3m)));
    }

    [Fact]
    public void CountDistinct_IsCaseInsensitiveAndIgnoresNulls()
    {
        var rows = new List<ReportRow>
        {
            new(new Dictionary<string, object?> { ["b"] = "Nike" }),
            new(new Dictionary<string, object?> { ["b"] = "NIKE" }),
            new(new Dictionary<string, object?> { ["b"] = "Adidas" }),
            new(new Dictionary<string, object?> { ["b"] = null })
        };

        Assert.Equal(2L, Aggregator.Compute(AggregateFunction.CountDistinct, "b", rows));
    }

    [Fact]
    public void Sum_WidensIntegersToDecimalSoLargeTotalsDoNotOverflow()
    {
        var rows = Rows(long.MaxValue, long.MaxValue);
        var result = Aggregator.Compute(AggregateFunction.Sum, "total", rows);

        Assert.IsType<decimal>(result);
        Assert.Equal(2m * long.MaxValue, result);
    }

    [Fact]
    public void Sum_ParsesNumericStrings_BecauseProvidersReturnNumbersAsText()
    {
        Assert.Equal(300m, Aggregator.Compute(AggregateFunction.Sum, "total", Rows("100", "200")));
    }

    [Fact]
    public void Sum_OverNonNumericText_SkipsAndReportsADiagnostic()
    {
        var diagnostics = new RenderDiagnostics();
        var result = Aggregator.Compute(AggregateFunction.Sum, "total", Rows(100m, "abc"), diagnostics, "t");

        Assert.Equal(100m, result);
        Assert.Contains("non-numeric", diagnostics.ToString());
    }

    [Fact]
    public void MinAndMax_CompareNumerically_NotAsText()
    {
        // As strings "9" > "100", so a text comparison would give the wrong answer here.
        var rows = Rows(9m, 100m, 50m);
        Assert.Equal(9m, Aggregator.Compute(AggregateFunction.Min, "total", rows));
        Assert.Equal(100m, Aggregator.Compute(AggregateFunction.Max, "total", rows));
    }

    [Fact]
    public void MinAndMax_WorkOnDates()
    {
        var rows = new List<ReportRow>
        {
            new(new Dictionary<string, object?> { ["d"] = new DateTime(2026, 5, 1) }),
            new(new Dictionary<string, object?> { ["d"] = new DateTime(2026, 1, 1) }),
            new(new Dictionary<string, object?> { ["d"] = new DateTime(2026, 9, 1) })
        };

        Assert.Equal(new DateTime(2026, 1, 1), Aggregator.Compute(AggregateFunction.Min, "d", rows));
        Assert.Equal(new DateTime(2026, 9, 1), Aggregator.Compute(AggregateFunction.Max, "d", rows));
    }

    [Fact]
    public void MinAndMax_WorkOnText()
    {
        var rows = new List<ReportRow>
        {
            new(new Dictionary<string, object?> { ["s"] = "pear" }),
            new(new Dictionary<string, object?> { ["s"] = "apple" })
        };

        Assert.Equal("apple", Aggregator.Compute(AggregateFunction.Min, "s", rows));
        Assert.Equal("pear", Aggregator.Compute(AggregateFunction.Max, "s", rows));
    }

    [Fact]
    public void FirstAndLast_SkipLeadingAndTrailingNulls()
    {
        var rows = Rows(null, 10m, 20m, null);
        Assert.Equal(10m, Aggregator.Compute(AggregateFunction.First, "total", rows));
        Assert.Equal(20m, Aggregator.Compute(AggregateFunction.Last, "total", rows));
    }

    [Fact]
    public void SumWithoutAField_ReportsADiagnosticInsteadOfThrowing()
    {
        var diagnostics = new RenderDiagnostics();
        var result = Aggregator.Compute(AggregateFunction.Sum, null, Rows(1m), diagnostics, "t");

        Assert.Null(result);
        Assert.Contains("needs a field", diagnostics.ToString());
    }

    [Fact]
    public void ComputeAll_KeysResultsSoExpressionsCanReadThem()
    {
        var aggregates = new List<AggregateDef>
        {
            new() { Function = AggregateFunction.Sum, Field = "total" },
            new() { Function = AggregateFunction.Max, Field = "total" }
        };

        var result = Aggregator.ComputeAll(aggregates, Rows(10m, 30m));

        Assert.Equal(40m, result["SumOfTotal"]);
        Assert.Equal(30m, result["MaxOfTotal"]);
        Assert.Equal(2L, result["Count"]);
    }
}
