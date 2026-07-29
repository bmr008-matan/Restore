#nullable enable
using System.Text;
using API.Reporting.Model;
using API.Reporting.Model.Elements;
using API.Reporting.Runtime;

namespace API.Reporting.Rendering
{
    /// <summary>
    /// Turns a PageHeader or PageFooter band into the HTML fragment Chromium wants for its
    /// <c>headerTemplate</c> / <c>footerTemplate</c> PDF options.
    ///
    /// This is the only place in the report where a page number can resolve, because Chromium performs
    /// that substitution itself and only inside these templates. That constraint brings a set of quirks
    /// which are all handled here rather than leaking into the rest of the renderer:
    ///
    /// <list type="bullet">
    /// <item>External CSS is ignored, so every declaration has to be inline.</item>
    /// <item>The default font-size is 0, so an explicit size is required or the header renders invisible.</item>
    /// <item>The fragment spans the full paper width, not the content width, so the page margins have to
    /// be re-applied as padding for the header to line up with the body.</item>
    /// <item>Images must be data URIs; Chromium will not fetch anything from here.</item>
    /// <item>Page numbers appear via elements carrying the magic classes pageNumber and totalPages.</item>
    /// </list>
    /// </summary>
    public sealed class HeaderFooterTemplateBuilder
    {
        /// <summary>
        /// Chromium's default header font-size is 0. Anything that does not set its own size gets this,
        /// so a band with unstyled text is still visible.
        /// </summary>
        private const double FallbackFontSizePt = 8;

        private readonly ReportDefinition _definition;
        private readonly ExpressionEvaluator _evaluator;
        private readonly ValueFormatter _formatter;
        private readonly TokenInterpolator _interpolator;
        private readonly RenderDiagnostics _diagnostics;

        public HeaderFooterTemplateBuilder(
            ReportDefinition definition,
            ExpressionEvaluator evaluator,
            ValueFormatter formatter,
            TokenInterpolator interpolator,
            RenderDiagnostics diagnostics)
        {
            _definition = definition;
            _evaluator = evaluator;
            _formatter = formatter;
            _interpolator = interpolator;
            _diagnostics = diagnostics;
        }

        /// <summary>
        /// Builds the fragment, or returns null when there is nothing to render — Chromium draws an
        /// ugly default header when handed an empty template, so the caller must disable the feature
        /// entirely rather than pass a blank string.
        /// </summary>
        public string? Build(
            BandDef? band,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string path)
        {
            if (band is null || !band.Visible || band.Elements.Count == 0) return null;

            foreach (var rule in band.Rules.Where(r => r.Enabled))
            {
                if (!_evaluator.EvaluateBool(rule.Expression, scope, _diagnostics, $"{path}.rules")) continue;
                if (rule.Actions.Hide == true) return null;
            }

            var page = _definition.Page;
            var height = band.Kind == BandKind.PageHeader ? page.HeaderHeightMm : page.FooterHeightMm;

            var html = new StringBuilder();

            // The outer box re-establishes what Chromium does not inherit: a font, a size, a direction,
            // and the page's own left/right margins so the header aligns with the body content.
            html.Append("<div style=\"")
                .Append("width:100%;")
                .Append("height:").Append(CssBuilder.Mm(height)).Append(';')
                .Append("box-sizing:border-box;")
                .Append("padding-left:").Append(CssBuilder.Mm(page.Margins.LeftMm)).Append(';')
                .Append("padding-right:").Append(CssBuilder.Mm(page.Margins.RightMm)).Append(';')
                .Append("font-size:").Append(CssBuilder.Pt(_definition.DefaultStyle?.FontSizePt ?? FallbackFontSizePt)).Append(';')
                .Append("direction:").Append(CssBuilder.Direction(page.Direction)).Append(';')
                .Append("position:relative;")
                .Append("-webkit-print-color-adjust:exact;")
                .Append("\">");

            foreach (var element in band.Elements)
                WriteElement(html, element, scope, data, $"{path}.{element.Id}");

            html.Append("</div>");
            return html.ToString();
        }

        private void WriteElement(
            StringBuilder html,
            ReportElement element,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string path)
        {
            if (!element.Visible) return;

            var style = StyleDef.Merge(_definition.DefaultStyle, element.Style);

            string? textOverride = null;
            foreach (var rule in element.Rules.Where(r => r.Enabled))
            {
                if (!_evaluator.EvaluateBool(rule.Expression, scope, _diagnostics, $"{path}.rules")) continue;
                if (rule.Actions.Hide == true) return;
                if (rule.Actions.TextOverride is { Length: > 0 }) textOverride = rule.Actions.TextOverride;
                style = StyleDef.Merge(style, rule.Actions.ToStyle());
            }

            var css = new StringBuilder();
            css.Append("position:absolute;")
               .Append("inset-inline-start:").Append(CssBuilder.Mm(element.XMm)).Append(';')
               .Append("top:").Append(CssBuilder.Mm(element.YMm)).Append(';')
               .Append("width:").Append(CssBuilder.Mm(element.WidthMm)).Append(';')
               .Append("height:").Append(CssBuilder.Mm(element.HeightMm)).Append(';')
               .Append("overflow:hidden;");

            // Every node needs its own explicit size: Chromium's zero default is not inherited away by
            // the wrapper for all cases, and a missing size here is the single most common reason a
            // header appears blank.
            if (style?.FontSizePt is null or <= 0)
                css.Append("font-size:").Append(CssBuilder.Pt(FallbackFontSizePt)).Append(';');

            css.Append(CssBuilder.InlineStyle(style, element.Direction));

            html.Append("<div").Append(Html.StyleAttr(css.ToString())).Append('>');
            WriteContent(html, element, scope, data, path, textOverride, style);
            html.Append("</div>");
        }

        private void WriteContent(
            StringBuilder html,
            ReportElement element,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string path,
            string? textOverride,
            StyleDef? style)
        {
            switch (element)
            {
                case PageNumberElement pageNumber:
                    html.Append(WithPageTokens(pageNumber.Template, scope, path));
                    break;

                case TextElement text:
                    html.Append(WithPageTokens(textOverride ?? text.Text, scope, path));
                    break;

                case FieldElement field:
                {
                    var rowScope = FirstRowScope(scope, data, field.DataSetKey);
                    var value = field.Field is { Length: > 0 }
                        ? rowScope.Fields.TryGetValue(field.Field, out var raw) ? raw : null
                        : _evaluator.EvaluateValue(field.Expression, rowScope, _diagnostics, path);

                    html.Append(Html.Text(_formatter.Format(value, field.Format ?? style?.Format, field.NullText)));
                    break;
                }

                case ImageElement image:
                    WriteImage(html, image, path);
                    break;

                case LineElement line:
                    html.Append("<div style=\"width:100%;height:0;border-top:")
                        .Append(CssBuilder.Pt(line.ThicknessPt)).Append(" solid ")
                        .Append(CssBuilder.CssSafe(line.Color)).Append(";\"></div>");
                    break;
            }
        }

        /// <summary>
        /// Interpolates the ordinary tokens, then swaps the two page tokens for the span elements
        /// Chromium recognises. Order matters: interpolation runs first and deliberately leaves the page
        /// tokens alone, so this replacement operates on already-escaped text and cannot be spoofed by a
        /// data value that happens to contain "{PageNumber}".
        /// </summary>
        private string WithPageTokens(string? template, ExpressionScope scope, string path)
        {
            var interpolated = _interpolator.Interpolate(template, scope, _diagnostics, path);
            var escaped = Html.Text(interpolated);

            return escaped
                .Replace("{PageNumber}", "<span class=\"pageNumber\"></span>", StringComparison.OrdinalIgnoreCase)
                .Replace("{TotalPages}", "<span class=\"totalPages\"></span>", StringComparison.OrdinalIgnoreCase);
        }

        private void WriteImage(StringBuilder html, ImageElement image, string path)
        {
            if (string.IsNullOrWhiteSpace(image.Source)) return;

            // Chromium will not fetch anything from inside a header template, so a non-inlined image
            // would render as a broken-image box. Say so rather than shipping a broken header.
            if (!image.Source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                _diagnostics.Add(path,
                    "Images on a page header or footer must be embedded as a data URI; Chromium cannot " +
                    "load external images there. This image was skipped.");
                return;
            }

            html.Append("<img src=\"").Append(Html.Attr(image.Source))
                .Append("\" style=\"width:100%;height:100%;object-fit:contain;\" alt=\"\">");
        }

        private static ExpressionScope FirstRowScope(
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string? dataSetKey)
        {
            if (string.IsNullOrWhiteSpace(dataSetKey)) return scope;
            if (!data.TryGetValue(dataSetKey, out var rendered)) return scope;

            var first = rendered.Data.Rows.FirstOrDefault();
            return first is null ? scope : scope.WithRow(first);
        }
    }
}
