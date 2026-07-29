using API.Reporting.Model;
using API.Reporting.Runtime;

namespace API.Tests.Reporting;

/// <summary>
/// Grouping and aggregation is where reporting bugs hide, so these tests cover the boundary walk, the
/// nesting, and the invariant that an outer subtotal equals the sum of its inner ones.
/// </summary>
public class GroupTreeBuilderTests
{
    private static readonly List<SortDef> NoSort = new();
    private static readonly List<AggregateDef> NoTotals = new();

    [Fact]
    public void NoGroups_ReturnsFlatRowsAndNoNodes()
    {
        var (_, _, builder) = TestData.Engine();
        var diagnostics = new RenderDiagnostics();

        var result = builder.Build(TestData.SalesRows(), new List<GroupDef>(), NoSort, NoTotals,
            TestData.Scope(), diagnostics, "table");

        Assert.False(result.HasGroups);
        Assert.Equal(6, result.AllRows.Count);
        Assert.False(diagnostics.HasAny);
    }

    [Fact]
    public void SingleLevel_CutsOneNodePerDistinctKey_Sorted()
    {
        var (_, _, builder) = TestData.Engine();

        var result = builder.Build(TestData.SalesRows(), new List<GroupDef> { TestData.Group("brand") },
            NoSort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        Assert.Equal(new[] { "Adidas", "Nike" }, result.Roots.Select(r => r.Key));
        Assert.Equal(3, result.Roots[0].ItemCount);
        Assert.Equal(3, result.Roots[1].ItemCount);
    }

    [Fact]
    public void SingleLevel_DescendingReversesGroupOrderButKeepsMembership()
    {
        var (_, _, builder) = TestData.Engine();
        var group = TestData.Group("brand");
        group.SortDirection = SortDirection.Desc;

        var result = builder.Build(TestData.SalesRows(), new List<GroupDef> { group },
            NoSort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        Assert.Equal(new[] { "Nike", "Adidas" }, result.Roots.Select(r => r.Key));
        Assert.All(result.Roots, node => Assert.Equal(3, node.ItemCount));
    }

    [Fact]
    public void TwoLevels_NestByDeclarationOrder()
    {
        var (_, _, builder) = TestData.Engine();

        var result = builder.Build(TestData.SalesRows(),
            new List<GroupDef> { TestData.Group("brand"), TestData.Group("type") },
            NoSort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        Assert.Equal(2, result.Roots.Count);

        var adidas = result.Roots[0];
        Assert.Equal("Adidas", adidas.Key);
        Assert.Equal(0, adidas.Level);
        Assert.Equal(new[] { "Shirts", "Shoes" }, adidas.Children.Select(c => c.Key));
        Assert.All(adidas.Children, child => Assert.Equal(1, child.Level));

        // Adidas has two Shirts rows (A-1, A-3) and one Shoes row (A-2).
        Assert.Equal(2, adidas.Children[0].ItemCount);
        Assert.Equal(1, adidas.Children[1].ItemCount);
        Assert.True(adidas.Children.All(c => c.IsLeaf));
    }

    [Fact]
    public void SwappingGroupOrder_ChangesNestingAndNothingElse()
    {
        var (_, _, builder) = TestData.Engine();

        var result = builder.Build(TestData.SalesRows(),
            new List<GroupDef> { TestData.Group("type"), TestData.Group("brand") },
            NoSort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        Assert.Equal(new[] { "Shirts", "Shoes" }, result.Roots.Select(r => r.Key));
        Assert.Equal(new[] { "Adidas", "Nike" }, result.Roots[0].Children.Select(c => c.Key));
        Assert.Equal(6, result.AllRows.Count);
    }

    [Fact]
    public void OuterSubtotal_EqualsSumOfInnerSubtotals()
    {
        var (_, _, builder) = TestData.Engine();
        var total = TestData.Sum("total");

        var result = builder.Build(TestData.SalesRows(),
            new List<GroupDef> { TestData.Group("brand", total), TestData.Group("type", total) },
            NoSort, new List<AggregateDef> { total }, TestData.Scope(), new RenderDiagnostics(), "table");

        foreach (var outer in result.Roots)
        {
            var innerSum = outer.Children.Sum(c => (decimal)(c.Aggregates[total.Key] ?? 0m));
            Assert.Equal((decimal)(outer.Aggregates[total.Key] ?? 0m), innerSum);
        }

        // Adidas 500 + 100 + 700, Nike 200 + 300, plus one null that must not count as zero.
        Assert.Equal(1300m, result.Roots[0].Aggregates[total.Key]);
        Assert.Equal(500m, result.Roots[1].Aggregates[total.Key]);
        Assert.Equal(1800m, result.GrandTotals[total.Key]);
    }

    [Fact]
    public void GroupCount_IsAlwaysAvailableEvenWithoutADeclaredAggregate()
    {
        var (_, _, builder) = TestData.Engine();

        var result = builder.Build(TestData.SalesRows(), new List<GroupDef> { TestData.Group("brand") },
            NoSort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        Assert.Equal(3L, result.Roots[0].Aggregates["Count"]);
        Assert.Equal(6L, result.GrandTotals["Count"]);
    }

    [Fact]
    public void WithinGroupSort_OrdersDetailRowsInsideTheInnermostGroup()
    {
        var (_, _, builder) = TestData.Engine();
        var sort = new List<SortDef> { new() { Field = "total", Direction = SortDirection.Desc } };

        var result = builder.Build(TestData.SalesRows(), new List<GroupDef> { TestData.Group("brand") },
            sort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        var adidasTotals = result.Roots[0].Rows.Select(r => r["total"]).ToList();
        Assert.Equal(new object?[] { 700m, 500m, 100m }, adidasTotals);
    }

    [Fact]
    public void NullKeys_FormTheirOwnGroupAndSortFirst()
    {
        var (_, _, builder) = TestData.Engine();
        var rows = TestData.SalesRows();
        rows.Add(TestData.Row("X-1", null!, "Shoes", 1, 50m));

        var result = builder.Build(rows, new List<GroupDef> { TestData.Group("brand") },
            NoSort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        Assert.Equal(3, result.Roots.Count);
        Assert.Null(result.Roots[0].Key);
        Assert.Equal(1, result.Roots[0].ItemCount);
    }

    [Fact]
    public void KeysDifferingOnlyByCase_CollapseIntoOneGroup()
    {
        var (_, _, builder) = TestData.Engine();
        var rows = TestData.SalesRows();
        rows.Add(TestData.Row("N-9", "NIKE", "Shoes", 1, 10m));

        var result = builder.Build(rows, new List<GroupDef> { TestData.Group("brand") },
            NoSort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        Assert.Equal(2, result.Roots.Count);
        Assert.Equal(4, result.Roots.Single(r => r.Key?.ToString()!.Equals("Nike", StringComparison.OrdinalIgnoreCase) == true).ItemCount);
    }

    [Fact]
    public void ExpressionGroupKey_IsEvaluatedPerRow()
    {
        var (_, _, builder) = TestData.Engine();
        var group = new GroupDef { Expression = "Fields.soldAt.Value.Month" };

        var result = builder.Build(TestData.SalesRows(), new List<GroupDef> { group },
            NoSort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        // Months 1, 2, 3 and 4 appear in the fixture.
        Assert.Equal(4, result.Roots.Count);
        Assert.Equal(new object?[] { 1, 2, 3, 4 }, result.Roots.Select(r => r.Key));
    }

    [Fact]
    public void ThreeLevels_BuildTheFullDepth()
    {
        var (_, _, builder) = TestData.Engine();

        var result = builder.Build(TestData.SalesRows(),
            new List<GroupDef>
            {
                TestData.Group("brand"), TestData.Group("type"), TestData.Group("sku")
            },
            NoSort, NoTotals, TestData.Scope(), new RenderDiagnostics(), "table");

        var leaves = result.Roots
            .SelectMany(l0 => l0.Children)
            .SelectMany(l1 => l1.Children)
            .ToList();

        Assert.Equal(6, leaves.Count);
        Assert.All(leaves, leaf => Assert.Equal(2, leaf.Level));
        Assert.All(leaves, leaf => Assert.Equal(1, leaf.ItemCount));
    }
}
