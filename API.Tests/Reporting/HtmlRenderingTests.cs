using API.Reporting;
using API.Reporting.Model;
using API.Reporting.Model.Elements;
using API.Reporting.Runtime;

namespace API.Tests.Reporting;

/// <summary>
/// Asserts on the generated HTML. These cover the structural decisions the PDF depends on — a repeating
/// thead, break rules, RTL attributes — which are far easier to pin down here than by inspecting a PDF.
/// </summary>
public class HtmlRenderingTests
{
    private static async Task<(string Document, string? Header, string? Footer, RenderDiagnostics Diagnostics)>
        RenderAsync(ReportDefinition definition, ReportRequest? request = null, int productCount = 40)
    {
        await using var harness = new RenderingHarness(productCount);
        using var scope = harness.CreateScope();

        var diagnostics = new RenderDiagnostics();
        var html = await harness.Runtime(scope).RenderHtmlAsync(
            definition, request ?? new ReportRequest(), diagnostics, CancellationToken.None);

        return (html.Document, html.HeaderTemplate, html.FooterTemplate, diagnostics);
    }

    [Fact]
    public async Task ColumnHeaders_GoInATheadSoChromiumRepeatsThemOnEveryPage()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand());

        Assert.Contains("<thead>", document);
        Assert.Contains("table-header-group", document);
    }

    [Fact]
    public async Task DisablingRepeatHeader_MovesHeadersOutOfTheThead()
    {
        var definition = SampleReports.StockByBrand();
        definition.AllTables().First().RepeatHeaderOnEachPage = false;

        var (document, _, _, _) = await RenderAsync(definition);

        Assert.DoesNotContain("<thead>", document);
        // The captions still print, just once.
        Assert.Contains("Stock value", document);
    }

    [Fact]
    public async Task GrandTotal_IsNotInATfoot_BecauseThatWouldRepeatOnEveryPage()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand());

        Assert.Contains("rpt-grand", document);
        Assert.DoesNotContain("<tfoot", document);
    }

    [Fact]
    public async Task BothGroupLevels_ProduceHeaderAndFooterRows()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand());

        Assert.Contains("rpt-grp-h-0", document);
        Assert.Contains("rpt-grp-h-1", document);
        Assert.Contains("rpt-grp-f-0", document);
        Assert.Contains("rpt-grp-f-1", document);
        Assert.Contains("Brand: Adidas", document);
        Assert.Contains("Total Adidas", document);
    }

    [Fact]
    public async Task GroupItemCount_IsAppendedWhenRequested()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand());

        // The brand level sets ShowItemCount, so its header carries a parenthesised row count.
        Assert.Matches(@"Brand: Adidas\s*\(\d+\)", document);
    }

    [Fact]
    public async Task KeepTogether_WrapsEachGroupInATbodyThatCannotSplit()
    {
        var definition = SampleReports.StockByBrand();
        definition.AllTables().First().Groups[0].KeepTogether = true;

        var (document, _, _, _) = await RenderAsync(definition);

        Assert.Contains("rpt-keep", document);
        Assert.Contains("break-inside:avoid", document);
    }

    [Fact]
    public async Task PageBreakBefore_PutsTheBreakClassOnTheGroupHeaderRow()
    {
        var definition = SampleReports.StockByBrand();
        definition.AllTables().First().Groups[0].PageBreakBefore = true;

        var (document, _, _, _) = await RenderAsync(definition);

        Assert.Contains("rpt-break-before", document);
    }

    [Fact]
    public async Task ConditionalRule_AppliesInlineStyleToMatchingCellsOnly()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand());

        // The high-value rule paints matching cells; it must not paint every cell.
        Assert.Contains("#B00020", document);
        var painted = System.Text.RegularExpressions.Regex.Matches(document, "#B00020").Count;
        var cells = System.Text.RegularExpressions.Regex.Matches(document, "<td").Count;
        Assert.True(painted < cells, $"Rule painted {painted} of {cells} cells, which suggests it matched everything.");
    }

    [Fact]
    public async Task RuleThatHidesAnElement_RemovesItEntirely()
    {
        var definition = SampleReports.StockByBrand();
        var header = definition.FindBand(BandKind.ReportHeader)!;
        ((TextElement)header.Elements[0]).Rules.Add(new ConditionalRuleDef
        {
            Expression = "true",
            Actions = new RuleActions { Hide = true }
        });

        var (document, _, _, _) = await RenderAsync(definition);

        Assert.DoesNotContain("Stock Valuation by Brand</span>", document);
    }

    [Fact]
    public async Task RoleBasedRule_HidesAColumnValueForUsersWithoutTheRole()
    {
        var definition = SampleReports.StockByBrand();
        definition.AllTables().First().Columns[3].Rules.Add(new ConditionalRuleDef
        {
            Name = "Managers only",
            Expression = "Report.User.IsInRole(\"Manager\") == false",
            Actions = new RuleActions { Hide = true }
        });

        var asViewer = await RenderAsync(definition, new ReportRequest
        {
            UserName = "v", UserRoles = new[] { "Viewer" }
        });
        var asManager = await RenderAsync(definition, new ReportRequest
        {
            UserName = "m", UserRoles = new[] { "Manager" }
        });

        // Price values are suppressed for the viewer but present for the manager.
        Assert.True(asManager.Document.Length > asViewer.Document.Length);
    }

    [Fact]
    public async Task PageSetup_BecomesAnAtPageRule()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand());

        Assert.Contains("@page{size:210mm 297mm", document);
        Assert.Contains("margin:24mm 12mm 18mm 12mm", document);
    }

    [Fact]
    public async Task Landscape_SwapsThePageDimensions()
    {
        var definition = SampleReports.StockByBrand();
        definition.Page.Orientation = PageOrientation.Landscape;

        var (document, _, _, _) = await RenderAsync(definition);

        Assert.Contains("@page{size:297mm 210mm", document);
    }

    [Theory]
    [InlineData(PaperSize.A3, "297mm 420mm")]
    [InlineData(PaperSize.A5, "148mm 210mm")]
    [InlineData(PaperSize.Letter, "215.9mm 279.4mm")]
    public async Task PaperSizes_MapToTheirRealDimensions(PaperSize size, string expected)
    {
        var definition = SampleReports.StockByBrand();
        definition.Page.PaperSize = size;

        var (document, _, _, _) = await RenderAsync(definition);

        Assert.Contains($"@page{{size:{expected}", document);
    }

    [Fact]
    public async Task PageNumberTokens_BecomeTheSpansChromiumSubstitutes()
    {
        var (_, header, footer, _) = await RenderAsync(SampleReports.StockByBrand());

        Assert.NotNull(header);
        Assert.Contains("class=\"pageNumber\"", header);
        Assert.Contains("class=\"totalPages\"", header);
        Assert.Contains("class=\"pageNumber\"", footer);
    }

    [Fact]
    public async Task HeaderTemplate_CarriesInlineStylesAndAnExplicitFontSize()
    {
        // Chromium ignores external CSS in header templates and defaults font-size to 0, so a header
        // without an explicit size renders invisible.
        var (_, header, _, _) = await RenderAsync(SampleReports.StockByBrand());

        Assert.NotNull(header);
        Assert.Contains("font-size:", header);
        Assert.DoesNotContain("<style", header);
        Assert.DoesNotContain("class=\"rpt-el\"", header);
    }

    [Fact]
    public async Task HeaderTemplate_ReappliesThePageMarginsSoItLinesUpWithTheBody()
    {
        var (_, header, _, _) = await RenderAsync(SampleReports.StockByBrand());

        Assert.Contains("padding-left:12mm", header);
        Assert.Contains("padding-right:12mm", header);
    }

    [Fact]
    public async Task BandsWithoutElements_ProduceNoHeaderTemplateAtAll()
    {
        // Handing Chromium an empty template makes it draw its own default header.
        var definition = SampleReports.StockByBrand();
        definition.FindBand(BandKind.PageHeader)!.Elements.Clear();

        var (_, header, _, _) = await RenderAsync(definition);

        Assert.Null(header);
    }

    [Fact]
    public async Task RtlReport_SetsDirectionOnTheDocument()
    {
        var (document, header, _, _) = await RenderAsync(SampleReports.StockByBrand(TextDirection.Rtl));

        Assert.Contains("<html dir=\"rtl\"", document);
        Assert.Contains("lang=\"he\"", document);
        Assert.Contains("direction:rtl", document);
        Assert.Contains("direction:rtl", header);
    }

    [Fact]
    public async Task RtlReport_KeepsHebrewTextIntact()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand(TextDirection.Rtl));

        // Hebrew must arrive as Hebrew, not as escapes or mangled bytes. Reversing text in the renderer
        // would be wrong: Chromium applies the bidi algorithm itself.
        Assert.Contains("דוח מלאי לפי מותג", document);
        Assert.Contains("שווי מלאי", document);
    }

    [Fact]
    public async Task ElementsArePlacedWithLogicalInlineStart_SoRtlMirrorsTheLayout()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand(TextDirection.Rtl));

        Assert.Contains("inset-inline-start:", document);
        // Physical left would pin elements to the wrong edge in an RTL report.
        Assert.DoesNotContain("position:absolute;left:", document);
    }

    [Fact]
    public async Task DirectionOverride_OnOneElementIsolatesIt()
    {
        var definition = SampleReports.StockByBrand(TextDirection.Rtl);
        definition.FindBand(BandKind.ReportHeader)!.Elements.Add(new TextElement
        {
            Text = "SKU-12345",
            Direction = TextDirection.Ltr,
            XMm = 130, YMm = 0, WidthMm = 50, HeightMm = 6
        });

        var (document, _, _, _) = await RenderAsync(definition);

        Assert.Contains("direction:ltr;unicode-bidi:isolate", document);
    }

    [Fact]
    public async Task PerCallDirectionOverride_FlipsTheReportWithoutTouchingTheStoredDefinition()
    {
        var definition = SampleReports.StockByBrand();

        var (document, _, _, _) = await RenderAsync(definition,
            new ReportRequest { Direction = TextDirection.Rtl, Culture = "he-IL" });

        Assert.Contains("<html dir=\"rtl\"", document);
        // The definition handed in must be untouched, since it may be a cached shared instance.
        Assert.Equal(TextDirection.Ltr, definition.Page.Direction);
        Assert.Equal("en-GB", definition.Page.Culture);
    }

    [Fact]
    public async Task DataValues_AreHtmlEscaped()
    {
        var definition = SampleReports.StockByBrand();
        definition.FindBand(BandKind.ReportHeader)!.Elements.Add(new TextElement
        {
            Text = "<script>alert(1)</script> & <b>bold</b>",
            XMm = 0, YMm = 30, WidthMm = 100, HeightMm = 6
        });

        var (document, _, _, _) = await RenderAsync(definition);

        Assert.Contains("&lt;script&gt;", document);
        Assert.DoesNotContain("<script>alert(1)</script>", document);
    }

    [Fact]
    public async Task EmptyResultSet_PrintsTheEmptyTextInsteadOfAnEmptyTable()
    {
        var definition = SampleReports.StockByBrand();
        // A price filter no product can satisfy.
        definition.Parameters.First(p => p.Name == "minPrice").DefaultValue = "999999";

        var (document, _, _, _) = await RenderAsync(definition);

        Assert.Contains("No stock matches these filters", document);
        Assert.DoesNotContain("<thead>", document);
    }

    [Fact]
    public async Task ReportHeaderAndFooter_AppearOnceInDocumentFlow()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand());

        Assert.Equal(1, CountOf(document, "rpt-band-reportheader"));
        Assert.Equal(1, CountOf(document, "rpt-band-reportfooter"));
    }

    [Fact]
    public async Task ParametersUsedForTheRun_AreShownInTheHeader()
    {
        var (document, _, _, _) = await RenderAsync(SampleReports.StockByBrand(),
            new ReportRequest
            {
                Parameters = new Dictionary<string, object?> { ["brand"] = "Nike", ["minPrice"] = 5m }
            });

        Assert.Contains("Brand: Nike", document);
        Assert.Contains("Minimum price: 5.00", document);
    }

    [Fact]
    public async Task DataSetInputsBoundToParameters_ActuallyFilterTheData()
    {
        var filtered = await RenderAsync(SampleReports.StockByBrand(),
            new ReportRequest { Parameters = new Dictionary<string, object?> { ["brand"] = "Nike" } });

        Assert.Contains("Brand: Nike", filtered.Document);
        Assert.DoesNotContain("Brand: Adidas", filtered.Document);
    }

    [Fact]
    public async Task BadParameterValue_IsRejectedBeforeAnyDataIsFetched()
    {
        var exception = await Assert.ThrowsAsync<ReportRequestException>(() => RenderAsync(
            SampleReports.StockByBrand(),
            new ReportRequest { Parameters = new Dictionary<string, object?> { ["minPrice"] = "not-a-number" } }));

        Assert.Contains("minPrice", exception.Errors.Single().Parameter);
    }

    [Fact]
    public async Task UnknownPushedDataKey_IsRejectedRatherThanIgnored()
    {
        var payload = new API.Reporting.Data.PushedDataProvider.Payload();
        payload.Add("nosuchset", new List<API.Reporting.Data.ReportRow>());

        var exception = await Assert.ThrowsAsync<ReportRequestException>(() => RenderAsync(
            SampleReports.StockByBrand(), new ReportRequest { PushedData = payload }));

        Assert.Contains("no data set called", exception.Errors.Single().Message);
    }

    [Fact]
    public async Task RenderingIsFreeOfDiagnostics_ForTheDemoReport()
    {
        // The shipped demo must not produce warnings; if it does, the template is wrong.
        var (_, _, _, diagnostics) = await RenderAsync(SampleReports.StockByBrand());

        Assert.False(diagnostics.HasAny, diagnostics.ToString());
    }

    [Fact]
    public async Task HebrewDemoReport_IsAlsoFreeOfDiagnostics()
    {
        var (_, _, _, diagnostics) = await RenderAsync(SampleReports.StockByBrand(TextDirection.Rtl));

        Assert.False(diagnostics.HasAny, diagnostics.ToString());
    }

    private static int CountOf(string haystack, string needle) =>
        System.Text.RegularExpressions.Regex.Matches(haystack, System.Text.RegularExpressions.Regex.Escape(needle)).Count;
}
