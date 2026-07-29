#nullable enable
using System.Text;
using API.Reporting.Model;
using API.Reporting.Runtime;

namespace API.Reporting.Rendering
{
    /// <summary>The HTML document plus the two Chromium templates that go with it.</summary>
    public sealed record RenderedHtml(
        string Document,
        string? HeaderTemplate,
        string? FooterTemplate);

    /// <summary>
    /// Assembles the printable HTML document from a definition and its resolved data.
    ///
    /// Band placement is what gives the once-per-report versus once-per-page distinction for free:
    /// report header and footer are ordinary blocks at the start and end of the flow, so they appear
    /// exactly once, while the page header and footer become Chromium templates and repeat.
    /// </summary>
    public sealed class HtmlReportRenderer
    {
        private readonly FontProvider _fonts;

        public HtmlReportRenderer(FontProvider fonts) => _fonts = fonts;

        public RenderedHtml Render(
            ReportDefinition definition,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            ExpressionScope scope,
            RenderDiagnostics diagnostics)
        {
            var formatter = new ValueFormatter(definition.Page.Culture);
            var interpolator = new TokenInterpolator(formatter);
            var evaluator = new ExpressionEvaluator();

            var bandWriter = new HtmlBandWriter(definition, evaluator, formatter, interpolator, diagnostics);
            var headerFooter = new HeaderFooterTemplateBuilder(definition, evaluator, formatter, interpolator, diagnostics);

            var body = new StringBuilder();

            // Report header, detail, then report footer — in flow, so pagination is Chromium's problem.
            WriteBand(body, definition, BandKind.ReportHeader, bandWriter, scope, data);
            WriteBand(body, definition, BandKind.Detail, bandWriter, scope, data);
            WriteBand(body, definition, BandKind.ReportFooter, bandWriter, scope, data);

            var font = _fonts.Resolve(definition);

            var document = new StringBuilder()
                .Append("<!DOCTYPE html><html dir=\"")
                .Append(CssBuilder.Direction(definition.Page.Direction))
                .Append("\" lang=\"").Append(Html.Attr(LanguageOf(definition.Page.Culture)))
                .Append("\"><head><meta charset=\"utf-8\"><title>")
                .Append(Html.Text(definition.EffectiveTitle))
                .Append("</title><style>")
                .Append(CssBuilder.BuildDocumentCss(definition, font.FamilyStack, font.FontFaceCss))
                .Append("</style></head><body>")
                .Append(body)
                .Append("</body></html>")
                .ToString();

            return new RenderedHtml(
                document,
                headerFooter.Build(definition.FindBand(BandKind.PageHeader), scope, data, "bands.pageHeader"),
                headerFooter.Build(definition.FindBand(BandKind.PageFooter), scope, data, "bands.pageFooter"));
        }

        private static void WriteBand(
            StringBuilder body,
            ReportDefinition definition,
            BandKind kind,
            HtmlBandWriter writer,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data)
        {
            var band = definition.FindBand(kind);
            if (band is null) return;

            writer.Write(body, band, scope, data, $"bands.{char.ToLowerInvariant(kind.ToString()[0])}{kind.ToString()[1..]}");
        }

        /// <summary>
        /// The lang attribute drives Chromium's line-breaking and font fallback, so it is worth setting
        /// properly from the report's culture rather than leaving the document language-neutral.
        /// </summary>
        private static string LanguageOf(string? culture)
        {
            if (string.IsNullOrWhiteSpace(culture)) return "en";
            var dash = culture.IndexOf('-');
            return dash > 0 ? culture[..dash] : culture;
        }
    }
}
