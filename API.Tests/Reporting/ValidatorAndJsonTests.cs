using API.Reporting.Model;
using API.Reporting.Model.Elements;
using API.Reporting.Runtime;

namespace API.Tests.Reporting;

public class ReportDefinitionValidatorTests
{
    private readonly ReportDefinitionValidator _validator = new(new ExpressionEvaluator());

    private static TableElement FirstTable(ReportDefinition definition) =>
        definition.AllTables().First();

    [Fact]
    public void AWellFormedDefinition_Passes()
    {
        var result = _validator.Validate(TestData.ValidDefinition());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
    }

    [Fact]
    public void MissingName_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        definition.Name = "";

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Path == "name");
    }

    [Fact]
    public void PageHeaderTallerThanTheTopMargin_IsAnError()
    {
        // Chromium draws the header inside the top margin, so this would silently clip.
        var definition = TestData.ValidDefinition();
        definition.Page.Margins.TopMm = 10;
        definition.Page.HeaderHeightMm = 25;

        var result = _validator.Validate(definition);

        var error = Assert.Single(result.Errors, e => e.Path == "page.headerHeightMm");
        Assert.Contains("cut off", error.Message);
    }

    [Fact]
    public void PageFooterTallerThanTheBottomMargin_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        definition.Page.Margins.BottomMm = 8;
        definition.Page.FooterHeightMm = 20;

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Path == "page.footerHeightMm");
    }

    [Fact]
    public void CustomPaperWithoutDimensions_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        definition.Page.PaperSize = PaperSize.Custom;

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Path == "page");
    }

    [Fact]
    public void DuplicateDataSetKeys_AreAnError()
    {
        var definition = TestData.ValidDefinition();
        definition.DataSets.Add(new DataSetDef { Key = "detail", SourceKind = DataSourceKind.Sample });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("Duplicate data set key"));
    }

    [Fact]
    public void DuplicateParameterNames_AreAnError()
    {
        var definition = TestData.ValidDefinition();
        definition.Parameters.Add(new ReportParameter { Name = "x", Type = ParameterType.Text });
        definition.Parameters.Add(new ReportParameter { Name = "X", Type = ParameterType.Text });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("Duplicate parameter name"));
    }

    [Fact]
    public void ParameterNameThatIsNotAnIdentifier_IsAnError()
    {
        // Parameter names become identifiers in expressions.
        var definition = TestData.ValidDefinition();
        definition.Parameters.Add(new ReportParameter { Name = "from date", Type = ParameterType.Text });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("not a usable parameter name"));
    }

    [Fact]
    public void SelectParameterWithoutALookupTable_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        definition.Parameters.Add(new ReportParameter { Name = "branch", Type = ParameterType.Select });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("lookup table id"));
    }

    [Fact]
    public void HiddenRequiredParameterWithNoDefault_IsAnError()
    {
        // The report could never run, so this is worth catching at design time.
        var definition = TestData.ValidDefinition();
        definition.Parameters.Add(new ReportParameter
        {
            Name = "secret", Type = ParameterType.Text, Required = true, Hidden = true
        });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("can never run"));
    }

    [Fact]
    public void DataSetInputBoundToAnUndeclaredParameter_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        definition.DataSets[0].Inputs.Add(new DataSetInput
        {
            Name = "p_from", Kind = InputKind.Parameter, Value = "ghost"
        });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("does not declare"));
    }

    [Fact]
    public void TableBoundToAMissingDataSet_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        FirstTable(definition).DataSetKey = "nope";

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("does not exist"));
    }

    [Fact]
    public void TableWithNoColumns_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        FirstTable(definition).Columns.Clear();

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("at least one column"));
    }

    [Fact]
    public void PageNumberOutsideAPageBand_IsAnError()
    {
        // Chromium only substitutes page numbers in its header and footer templates; anywhere else it
        // would print the literal token.
        var definition = TestData.ValidDefinition();
        definition.FindBand(BandKind.Detail)!.Elements.Add(new PageNumberElement { WidthMm = 30, HeightMm = 5 });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("Page Header or Page Footer"));
    }

    [Fact]
    public void PageNumberOnAPageFooter_IsFine()
    {
        var definition = TestData.ValidDefinition();
        definition.FindBand(BandKind.PageFooter)!.Elements.Add(new PageNumberElement { WidthMm = 30, HeightMm = 5 });

        Assert.True(_validator.Validate(definition).IsValid);
    }

    [Fact]
    public void PageTokenInBodyText_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        definition.FindBand(BandKind.ReportHeader)!.Elements.Add(new TextElement
        {
            Text = "Page {PageNumber}", WidthMm = 40, HeightMm = 6
        });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("only resolve"));
    }

    [Fact]
    public void SumOverATextField_IsAnError()
    {
        // Summing text yields nothing at render time; say so at save time instead.
        var definition = TestData.ValidDefinition();
        FirstTable(definition).GrandTotals.Add(new AggregateDef
        {
            Function = AggregateFunction.Sum, Field = "brand"
        });

        var result = _validator.Validate(definition);
        Assert.Contains(result.Errors, e => e.Message.Contains("needs a numeric field"));
    }

    [Fact]
    public void CountOverNoField_IsFine_ButSumOverNoFieldIsNot()
    {
        var definition = TestData.ValidDefinition();
        FirstTable(definition).GrandTotals.Add(new AggregateDef { Function = AggregateFunction.Count });
        Assert.True(_validator.Validate(definition).IsValid);

        FirstTable(definition).GrandTotals.Add(new AggregateDef { Function = AggregateFunction.Sum });
        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("needs a field"));
    }

    [Fact]
    public void GroupLevelWithoutAFieldOrExpression_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        FirstTable(definition).Groups.Add(new GroupDef());

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("needs either a field or an expression"));
    }

    [Fact]
    public void KeepTogetherOnSeveralLevels_IsAWarningNotAnError()
    {
        // Only the outermost level can be honoured, because the renderer uses a tbody and those do
        // not nest. Worth flagging, not worth blocking.
        var definition = TestData.ValidDefinition();
        var table = FirstTable(definition);
        table.Groups[0].KeepTogether = true;
        table.Groups.Add(new GroupDef { Field = "type", KeepTogether = true });

        var result = _validator.Validate(definition);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Message.Contains("outermost"));
    }

    [Fact]
    public void BandRuleReadingFields_IsAnError()
    {
        // A band is evaluated once per report and has no current row, so Fields would always be empty.
        var definition = TestData.ValidDefinition();
        definition.FindBand(BandKind.PageHeader)!.Rules.Add(new ConditionalRuleDef
        {
            Expression = "Fields.total > 5"
        });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("has no current"));
    }

    [Fact]
    public void ColumnRuleReadingFields_IsFine()
    {
        var definition = TestData.ValidDefinition();
        FirstTable(definition).Columns[2].Rules.Add(new ConditionalRuleDef
        {
            Expression = "Fields.total > 10000",
            Actions = new RuleActions { Bold = true, Color = "#B00020" }
        });

        Assert.True(_validator.Validate(definition).IsValid);
    }

    [Fact]
    public void RuleWithAnUncompilableExpression_IsAnError()
    {
        var definition = TestData.ValidDefinition();
        FirstTable(definition).RowRules.Add(new ConditionalRuleDef { Expression = "Fields.total >>> 1" });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("does not compile"));
    }

    [Fact]
    public void InvalidColour_IsAnError_WhichAlsoClosesOffCssInjection()
    {
        var definition = TestData.ValidDefinition();
        FirstTable(definition).HeaderStyle = new StyleDef { BackColor = "red; } body { display:none } .x{" };

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("not a valid colour"));
    }

    [Theory]
    [InlineData("#fff")]
    [InlineData("#B00020")]
    [InlineData("rgba(0,0,0,0.5)")]
    [InlineData("darkred")]
    public void ValidColourForms_AreAccepted(string color)
    {
        var definition = TestData.ValidDefinition();
        FirstTable(definition).HeaderStyle = new StyleDef { BackColor = color };

        Assert.True(_validator.Validate(definition).IsValid);
    }

    [Fact]
    public void DuplicateBandsOfTheSameKind_AreAnError()
    {
        var definition = TestData.ValidDefinition();
        definition.Bands.Add(new BandDef { Kind = BandKind.Detail });

        Assert.Contains(_validator.Validate(definition).Errors, e => e.Message.Contains("only be one Detail band"));
    }

    [Fact]
    public void UnknownFieldReference_IsAWarningNotAnError()
    {
        // The cached catalogue may simply be stale, so this must not block a save.
        var definition = TestData.ValidDefinition();
        FirstTable(definition).Columns.Add(new TableColumnDef { Field = "notAField", Caption = "X" });

        var result = _validator.Validate(definition);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Message.Contains("not a known field"));
    }

    [Fact]
    public void ColumnPercentagesOverOneHundred_AreAWarning()
    {
        var definition = TestData.ValidDefinition();
        FirstTable(definition).Columns[0].WidthPercent = 80;

        var result = _validator.Validate(definition);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Message.Contains("overflow"));
    }
}

public class ReportJsonTests
{
    [Fact]
    public void RoundTrip_PreservesTheDefinitionExactly()
    {
        var original = TestData.ValidDefinition();

        var json = ReportJson.Serialize(original);
        var restored = ReportJson.Deserialize(json);

        // Re-serialising must produce byte-identical JSON, otherwise a save-then-load cycle silently
        // changes the template.
        Assert.Equal(json, ReportJson.Serialize(restored));
    }

    [Fact]
    public void PolymorphicElements_SurviveARoundTripAsTheirRealTypes()
    {
        var definition = TestData.ValidDefinition();
        var header = definition.FindBand(BandKind.ReportHeader)!;
        header.Elements.Add(new TextElement { Text = "{ReportTitle}", WidthMm = 60, HeightMm = 8 });
        header.Elements.Add(new ImageElement { Source = "/images/logo.png", WidthMm = 30, HeightMm = 12 });
        header.Elements.Add(new LineElement { WidthMm = 180, HeightMm = 1 });
        definition.FindBand(BandKind.PageFooter)!.Elements.Add(new PageNumberElement { WidthMm = 40, HeightMm = 5 });

        var restored = ReportJson.Deserialize(ReportJson.Serialize(definition));

        Assert.IsType<TextElement>(restored.FindBand(BandKind.ReportHeader)!.Elements[0]);
        Assert.IsType<ImageElement>(restored.FindBand(BandKind.ReportHeader)!.Elements[1]);
        Assert.IsType<LineElement>(restored.FindBand(BandKind.ReportHeader)!.Elements[2]);
        Assert.IsType<PageNumberElement>(restored.FindBand(BandKind.PageFooter)!.Elements[0]);
        Assert.IsType<TableElement>(restored.FindBand(BandKind.Detail)!.Elements[0]);
    }

    [Fact]
    public void ElementTypeDiscriminator_IsTheShortStableName()
    {
        // The discriminator must not be a CLR type name, or stored templates would break the moment a
        // namespace or assembly is renamed.
        var definition = ReportDefinition.CreateEmpty("t");
        definition.FindBand(BandKind.Detail)!.Elements.Add(new TextElement { Text = "hi" });

        var json = ReportJson.Serialize(definition);

        Assert.Contains("\"type\":\"text\"", json);
        Assert.DoesNotContain("API.Reporting.Model.Elements", json);
    }

    [Fact]
    public void EnumsAreSerialisedByName_SoStoredJsonStaysReadableAndStable()
    {
        var json = ReportJson.Serialize(TestData.ValidDefinition());

        Assert.Contains("\"orientation\":\"Portrait\"", json);
        Assert.Contains("\"paperSize\":\"A4\"", json);
    }

    [Fact]
    public void PropertiesAreCamelCased()
    {
        var json = ReportJson.Serialize(ReportDefinition.CreateEmpty("t"));

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"heightMm\"", json);
    }

    [Fact]
    public void TryDeserialize_ReportsMalformedJsonInsteadOfThrowing()
    {
        Assert.False(ReportJson.TryDeserialize("{ not json", out var definition, out var error));
        Assert.Null(definition);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void UnknownElementTypeDiscriminator_IsReportedAsAnError()
    {
        var json = """
        {"name":"t","bands":[{"kind":"Detail","elements":[{"type":"holographicChart"}]}]}
        """;

        Assert.False(ReportJson.TryDeserialize(json, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void CommentsAndTrailingCommasAreToleratedOnInput()
    {
        var json = """
        {
          // definitions get hand-edited often enough that this is worth allowing
          "name": "t",
          "page": { "paperSize": "A4", },
        }
        """;

        Assert.True(ReportJson.TryDeserialize(json, out var definition, out var error), error);
        Assert.Equal("t", definition!.Name);
    }

    [Fact]
    public void ComputedPageDimensions_AreNotSerialisedBack()
    {
        // WidthMm is derived from paper size and orientation; persisting it would let it drift.
        var json = ReportJson.Serialize(ReportDefinition.CreateEmpty("t"));

        Assert.DoesNotContain("\"widthMm\"", json);
        Assert.DoesNotContain("\"contentWidthMm\"", json);
    }
}
