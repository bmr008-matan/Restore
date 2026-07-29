using API.Reporting.Data;
using API.Reporting.Model;
using API.Reporting.Model.Elements;
using API.Reporting.Runtime;

namespace API.Tests.Reporting;

/// <summary>
/// Shared fixtures. The sales shape used here — brand, then type, with a quantity and a total —
/// mirrors the demo report, so a bug caught in a unit test is a bug visible in the real PDF.
/// </summary>
internal static class TestData
{
    public static readonly List<FieldMeta> SalesFields = new()
    {
        new FieldMeta { Name = "sku", DataType = FieldDataType.String },
        new FieldMeta { Name = "brand", DataType = FieldDataType.String },
        new FieldMeta { Name = "type", DataType = FieldDataType.String },
        new FieldMeta { Name = "qty", DataType = FieldDataType.Int },
        new FieldMeta { Name = "total", DataType = FieldDataType.Decimal },
        new FieldMeta { Name = "soldAt", DataType = FieldDataType.Date }
    };

    public static ReportRow Row(string sku, string brand, string type, long? qty, decimal? total, DateTime? soldAt = null) =>
        new(new Dictionary<string, object?>
        {
            ["sku"] = sku,
            ["brand"] = brand,
            ["type"] = type,
            ["qty"] = qty,
            ["total"] = total,
            ["soldAt"] = soldAt
        });

    /// <summary>
    /// Deliberately unsorted and with a null total, so grouping has real work to do and aggregation
    /// has a null to skip.
    /// </summary>
    public static List<ReportRow> SalesRows() => new()
    {
        Row("N-2", "Nike", "Shoes", 2, 200m, new DateTime(2026, 3, 2)),
        Row("A-1", "Adidas", "Shirts", 5, 500m, new DateTime(2026, 1, 5)),
        Row("N-1", "Nike", "Shoes", 3, 300m, new DateTime(2026, 2, 1)),
        Row("A-2", "Adidas", "Shoes", 1, 100m, new DateTime(2026, 1, 9)),
        Row("N-3", "Nike", "Shirts", 4, null, new DateTime(2026, 4, 4)),
        Row("A-3", "Adidas", "Shirts", 7, 700m, new DateTime(2026, 2, 20))
    };

    public static ExpressionScope Scope(
        IReadOnlyDictionary<string, object?>? parameters = null,
        UserContext? user = null) => new()
        {
            Params = parameters ?? ExpressionScope.NoValues,
            Report = new ReportContext
            {
                Name = "Sales",
                Title = "Sales by Brand",
                RunAt = new DateTime(2026, 7, 29, 14, 30, 0),
                User = user ?? new UserContext { Name = "tester", Roles = new[] { "Viewer" } }
            },
            DeclaredTypes = TypeMap.Create().AddFields(SalesFields)
        };

    public static (ExpressionEvaluator Evaluator, ValueFormatter Formatter, GroupTreeBuilder Builder) Engine(
        string culture = "en-US")
    {
        var evaluator = new ExpressionEvaluator();
        var formatter = new ValueFormatter(culture);
        return (evaluator, formatter, new GroupTreeBuilder(evaluator, formatter));
    }

    public static GroupDef Group(string field, params AggregateDef[] aggregates) => new()
    {
        Field = field,
        Aggregates = aggregates.ToList()
    };

    public static AggregateDef Sum(string field, string? target = null) =>
        new() { Function = AggregateFunction.Sum, Field = field, TargetColumn = target };

    /// <summary>A minimal but valid definition, for validator tests to mutate one thing at a time.</summary>
    public static ReportDefinition ValidDefinition()
    {
        var definition = ReportDefinition.CreateEmpty("Sales by Brand");
        definition.Page.HeaderHeightMm = 10;
        definition.Page.FooterHeightMm = 8;
        definition.DataSets.Add(new DataSetDef
        {
            Key = "detail",
            SourceKind = DataSourceKind.Sample,
            TableId = 1042,
            Fields = SalesFields.ToList()
        });

        definition.FindBand(BandKind.Detail)!.Elements.Add(new TableElement
        {
            DataSetKey = "detail",
            WidthMm = 180,
            HeightMm = 40,
            Columns = new List<TableColumnDef>
            {
                new() { Field = "sku", Caption = "SKU", WidthPercent = 30 },
                new() { Field = "qty", Caption = "Qty", WidthPercent = 30 },
                new() { Field = "total", Caption = "Total", WidthPercent = 40, Format = "#,##0.00" }
            },
            Groups = new List<GroupDef> { Group("brand", Sum("total", "total")) },
            GrandTotals = new List<AggregateDef> { Sum("total", "total") }
        });

        return definition;
    }
}
