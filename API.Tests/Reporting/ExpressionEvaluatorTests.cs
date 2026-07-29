using API.Reporting.Model;
using API.Reporting.Runtime;

namespace API.Tests.Reporting;

public class ExpressionEvaluatorTests
{
    private readonly ExpressionEvaluator _evaluator = new();
    private readonly RenderDiagnostics _diagnostics = new();

    private ExpressionScope RowScope(decimal? total = 500m, string brand = "Nike", long? qty = 3)
    {
        var row = TestData.Row("N-1", brand, "Shoes", qty, total);
        return TestData.Scope().WithRow(row);
    }

    [Fact]
    public void NumericComparison_UsesRealOperatorsNotStringCompare()
    {
        Assert.True(_evaluator.EvaluateBool("Fields.total > 100", RowScope(500m), _diagnostics, "r"));
        Assert.False(_evaluator.EvaluateBool("Fields.total > 100", RowScope(50m), _diagnostics, "r"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void NullField_ComparesWithLiftedOperatorSemantics_AndDoesNotThrow()
    {
        // C# says (decimal?)null > 100 is false, and null-propagating comparisons must not blow up a
        // whole report just because one cell is empty.
        Assert.False(_evaluator.EvaluateBool("Fields.total > 100", RowScope(null), _diagnostics, "r"));
        Assert.False(_evaluator.EvaluateBool("Fields.total <= 100", RowScope(null), _diagnostics, "r"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void SameExpression_BehavesIdenticallyWhetherOrNotTheFirstRowWasNull()
    {
        // The declared-type map exists precisely so caching cannot make row order matter. Evaluating a
        // null row first must not poison the compiled expression for later rows.
        Assert.False(_evaluator.EvaluateBool("Fields.total > 100", RowScope(null), _diagnostics, "r"));
        Assert.True(_evaluator.EvaluateBool("Fields.total > 100", RowScope(500m), _diagnostics, "r"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void BracketSyntax_IsEquivalentToDotSyntax()
    {
        Assert.True(_evaluator.EvaluateBool("Fields[\"total\"] > 100", RowScope(500m), _diagnostics, "r"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void FieldLookup_IsCaseInsensitive()
    {
        Assert.True(_evaluator.EvaluateBool("Fields.TOTAL > 100", RowScope(500m), _diagnostics, "r"));
    }

    [Fact]
    public void StringComparison_AndLogicalOperatorsWork()
    {
        var scope = RowScope(500m, "Nike");
        Assert.True(_evaluator.EvaluateBool("Fields.brand == \"Nike\" && Fields.total > 100", scope, _diagnostics, "r"));
        Assert.False(_evaluator.EvaluateBool("Fields.brand == \"Adidas\" || Fields.total > 1000", scope, _diagnostics, "r"));
    }

    [Fact]
    public void Arithmetic_AcrossFieldsOfDifferentDeclaredTypes()
    {
        // qty is a long, total a decimal: the expression has to mix them without complaint.
        var value = _evaluator.EvaluateValue("Fields.total / Fields.qty", RowScope(600m, qty: 3), _diagnostics, "r");
        Assert.Equal(200m, value);
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void Parameters_AreReadableAndTyped()
    {
        var scope = new ExpressionScope
        {
            Params = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["minTotal"] = 250m },
            DeclaredTypes = TypeMap.Create()
                .AddFields(TestData.SalesFields)
                .AddParameters(new[] { new ReportParameter { Name = "minTotal", Type = ParameterType.Decimal } })
        }.WithRow(TestData.Row("N-1", "Nike", "Shoes", 1, 500m));

        Assert.True(_evaluator.EvaluateBool("Fields.total > Params.minTotal", scope, _diagnostics, "r"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void ReportContext_ResolvesTypedMembers_IncludingRoleChecks()
    {
        var manager = TestData.Scope(user: new UserContext { Name = "m", Roles = new[] { "Manager" } });
        var viewer = TestData.Scope(user: new UserContext { Name = "v", Roles = new[] { "Viewer" } });

        Assert.True(_evaluator.EvaluateBool("Report.User.IsInRole(\"Manager\")", manager, _diagnostics, "r"));
        Assert.False(_evaluator.EvaluateBool("Report.User.IsInRole(\"Manager\")", viewer, _diagnostics, "r"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void RoleCheck_IsCaseInsensitive()
    {
        var scope = TestData.Scope(user: new UserContext { Roles = new[] { "manager" } });
        Assert.True(_evaluator.EvaluateBool("Report.User.IsInRole(\"MANAGER\")", scope, _diagnostics, "r"));
    }

    [Fact]
    public void GroupAggregates_AreReadableInRules()
    {
        var scope = TestData.Scope().WithGroup(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SumOfTotal"] = 1300m,
            ["Count"] = 3L
        });

        Assert.True(_evaluator.EvaluateBool("Group.SumOfTotal > 1000", scope, _diagnostics, "r"));
        Assert.True(_evaluator.EvaluateBool("Group.Count == 3", scope, _diagnostics, "r"));
    }

    [Fact]
    public void HelperFunctions_AreAvailable()
    {
        Assert.True(_evaluator.EvaluateBool("IsNull(Fields.total)", RowScope(null), _diagnostics, "r"));
        Assert.False(_evaluator.EvaluateBool("IsNull(Fields.total)", RowScope(1m), _diagnostics, "r"));
        Assert.True(_evaluator.EvaluateBool("IsBlank(Fields.brand)", RowScope(brand: "   "), _diagnostics, "r"));
    }

    [Fact]
    public void NullLiteral_IsUsableInExpressions()
    {
        // Regression: without DynamicExpresso's SystemKeywords option the literals true, false and
        // null are unknown identifiers, so this ordinary comparison failed at render time.
        Assert.True(_evaluator.EvaluateBool("Fields.total == null", RowScope(null), _diagnostics, "r"));
        Assert.False(_evaluator.EvaluateBool("Fields.total == null", RowScope(5m), _diagnostics, "r"));
        Assert.True(_evaluator.EvaluateBool("Fields.total != null", RowScope(5m), _diagnostics, "r"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void TrueAndFalseLiterals_AreUsableInExpressions()
    {
        var scope = TestData.Scope(user: new UserContext { Roles = new[] { "Viewer" } });

        Assert.True(_evaluator.EvaluateBool("Report.User.IsInRole(\"Manager\") == false", scope, _diagnostics, "r"));
        Assert.True(_evaluator.EvaluateBool("true", scope, _diagnostics, "r"));
        Assert.False(_evaluator.EvaluateBool("false", scope, _diagnostics, "r"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void NonBooleanResult_IsReportedAndTreatedAsFalse()
    {
        var result = _evaluator.EvaluateBool("Fields.total", RowScope(500m), _diagnostics, "rule.1");

        Assert.False(result);
        Assert.Contains("true/false", _diagnostics.ToString());
    }

    [Fact]
    public void UnparseableExpression_IsReportedAndTreatedAsFalse()
    {
        var result = _evaluator.EvaluateBool("Fields.total >>> 5", RowScope(), _diagnostics, "rule.2");

        Assert.False(result);
        Assert.Contains("Could not evaluate", _diagnostics.ToString());
    }

    [Fact]
    public void EmptyExpression_IsFalseAndSilent()
    {
        Assert.False(_evaluator.EvaluateBool("  ", RowScope(), _diagnostics, "r"));
        Assert.False(_diagnostics.HasAny);
    }

    [Fact]
    public void RepeatedFailures_AreCappedSoOneBadRuleCannotFloodTheLog()
    {
        for (var i = 0; i < 50; i++)
            _evaluator.EvaluateBool("this is not valid", RowScope(), _diagnostics, "rule.3");

        Assert.True(_diagnostics.Items.Count <= 3);
        Assert.Equal(50, _diagnostics.OccurrenceCount("rule.3",
            _diagnostics.Items.First(i => i.Path == "rule.3").Message));
    }

    [Fact]
    public void Assignment_IsRejected_SoAnExpressionCannotMutateState()
    {
        var result = _evaluator.EvaluateBool("Fields.total = 5", RowScope(), _diagnostics, "rule.4");

        Assert.False(result);
        Assert.True(_diagnostics.HasAny);
    }

    [Theory]
    [InlineData("Fields.total > 100")]
    [InlineData("Params.x == null")]
    [InlineData("Report.User.IsInRole(\"Manager\") == false")]
    [InlineData("Group.SumOfTotal >= 0")]
    public void TryValidateSyntax_AcceptsWellFormedExpressions(string expression)
    {
        Assert.True(_evaluator.TryValidateSyntax(expression, out var error), error);
    }

    [Theory]
    [InlineData("Fields.total >>> 100")]
    [InlineData("System.IO.File.Delete(\"x\")")]
    [InlineData("(")]
    public void TryValidateSyntax_RejectsBadOrOutOfSandboxExpressions(string expression)
    {
        Assert.False(_evaluator.TryValidateSyntax(expression, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
