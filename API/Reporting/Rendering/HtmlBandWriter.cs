#nullable enable
using System.Text;
using API.Reporting.Data;
using API.Reporting.Model;
using API.Reporting.Model.Elements;
using API.Reporting.Runtime;

namespace API.Reporting.Rendering
{
    /// <summary>
    /// Renders a band and the elements placed on it.
    ///
    /// Elements sit at absolute positions inside the band, except for tables on the detail band: a table
    /// has to grow and paginate, and an absolutely positioned box cannot do either. So the detail band
    /// lets tables flow normally and keeps everything else absolute.
    /// </summary>
    public sealed class HtmlBandWriter
    {
        private readonly ReportDefinition _definition;
        private readonly ExpressionEvaluator _evaluator;
        private readonly ValueFormatter _formatter;
        private readonly TokenInterpolator _interpolator;
        private readonly RenderDiagnostics _diagnostics;
        private readonly HtmlTableWriter _tableWriter;

        public HtmlBandWriter(
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
            _tableWriter = new HtmlTableWriter(definition, evaluator, formatter, interpolator, diagnostics);
        }

        /// <summary>
        /// Writes a band in document flow. Returns false when the band is hidden, either outright or by
        /// a conditional rule.
        /// </summary>
        public bool Write(
            StringBuilder html,
            BandDef band,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string path)
        {
            if (!band.Visible) return false;

            var style = StyleDef.Merge(_definition.DefaultStyle, band.Style);

            foreach (var rule in band.Rules.Where(r => r.Enabled))
            {
                if (!_evaluator.EvaluateBool(rule.Expression, scope, _diagnostics, $"{path}.rules")) continue;
                if (rule.Actions.Hide == true) return false;
                style = StyleDef.Merge(style, rule.Actions.ToStyle());
            }

            var tables = band.Elements.OfType<TableElement>().ToList();
            var positioned = band.Elements.Where(e => e is not TableElement).ToList();

            var css = new StringBuilder(CssBuilder.InlineStyle(style));

            // A band holding a table must be free to grow past its designed height, or the table would
            // be clipped at whatever height the designer happened to drag it to.
            css.Append(tables.Count > 0 ? "min-height:" : "height:")
               .Append(CssBuilder.Mm(band.HeightMm)).Append(';');

            html.Append("<div class=\"rpt-band rpt-band-").Append(band.Kind.ToString().ToLowerInvariant())
                .Append('"').Append(Html.StyleAttr(css.ToString())).Append('>');

            foreach (var element in positioned)
                WriteElement(html, element, scope, data, $"{path}.{element.Id}");

            foreach (var table in tables)
                WriteTable(html, table, scope, data, $"{path}.{table.Id}");

            html.Append("</div>");
            return true;
        }

        private void WriteTable(
            StringBuilder html,
            TableElement table,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string path)
        {
            if (!table.Visible) return;

            if (!data.TryGetValue(table.DataSetKey, out var rendered))
            {
                _diagnostics.Add(path, $"Table is bound to data set \"{table.DataSetKey}\", which produced no data.");
                return;
            }

            _tableWriter.Write(html, table, rendered.Data, rendered.Grouped, scope, path);
        }

        /// <summary>
        /// Writes one absolutely positioned element. Position uses the logical inline-start edge so that
        /// an RTL report mirrors its layout — a logo designed at the left in an LTR report lands at the
        /// right in Hebrew, which is what mirroring means.
        /// </summary>
        public void WriteElement(
            StringBuilder html,
            ReportElement element,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string path,
            bool absolute = true)
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
            if (absolute)
            {
                css.Append("position:absolute;")
                   .Append("inset-inline-start:").Append(CssBuilder.Mm(element.XMm)).Append(';')
                   .Append("top:").Append(CssBuilder.Mm(element.YMm)).Append(';')
                   .Append("width:").Append(CssBuilder.Mm(element.WidthMm)).Append(';')
                   .Append("height:").Append(CssBuilder.Mm(element.HeightMm)).Append(';');
            }
            css.Append(CssBuilder.InlineStyle(style, element.Direction));

            html.Append("<div class=\"rpt-el\"").Append(Html.StyleAttr(css.ToString())).Append('>');
            WriteElementContent(html, element, scope, data, path, textOverride, style);
            html.Append("</div>");
        }

        private void WriteElementContent(
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
                case TextElement text:
                    WriteText(html, textOverride ?? text.Text, scope, data, text.DataSetKey, path);
                    break;

                case FieldElement field:
                    WriteField(html, field, scope, data, path, textOverride, style);
                    break;

                case PageNumberElement pageNumber:
                    // Left as literal tokens on purpose: only Chromium can substitute them, and it does
                    // that when this band is turned into a header or footer template.
                    html.Append(Html.Text(pageNumber.Template));
                    break;

                case ImageElement image:
                    WriteImage(html, image, scope, data, path);
                    break;

                case LineElement line:
                    WriteLine(html, line);
                    break;

                case BoxElement box:
                    if (box.CornerRadiusMm > 0)
                        html.Append("<div style=\"width:100%;height:100%;border-radius:")
                            .Append(CssBuilder.Mm(box.CornerRadiusMm)).Append(";\"></div>");
                    break;
            }
        }

        private void WriteText(
            StringBuilder html,
            string? template,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string? dataSetKey,
            string path)
        {
            var rowScope = FirstRowScope(scope, data, dataSetKey);
            var text = _interpolator.Interpolate(template, rowScope, _diagnostics, path);
            html.Append("<span class=\"rpt-text\">").Append(Html.Text(text)).Append("</span>");
        }

        private void WriteField(
            StringBuilder html,
            FieldElement field,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string path,
            string? textOverride,
            StyleDef? style)
        {
            var rowScope = FirstRowScope(scope, data, field.DataSetKey);

            if (textOverride is { Length: > 0 })
            {
                html.Append(Html.Text(_interpolator.Interpolate(textOverride, rowScope, _diagnostics, path)));
                return;
            }

            var value = field.Field is { Length: > 0 }
                ? rowScope.Fields.TryGetValue(field.Field, out var raw) ? raw : null
                : _evaluator.EvaluateValue(field.Expression, rowScope, _diagnostics, path);

            html.Append(Html.Text(_formatter.Format(value, field.Format ?? style?.Format, field.NullText)));
        }

        /// <summary>
        /// A field or text element sitting on a band is outside any row, so it resolves against the
        /// first row of its data set. That is what makes a header showing a company name or a document
        /// number work without wrapping it in a one-row table.
        /// </summary>
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

        private void WriteImage(
            StringBuilder html,
            ImageElement image,
            ExpressionScope scope,
            IReadOnlyDictionary<string, RenderedDataSet> data,
            string path)
        {
            var source = image.Source;

            if (image.Field is { Length: > 0 })
            {
                var rowScope = FirstRowScope(scope, data, image.DataSetKey);
                rowScope.Fields.TryGetValue(image.Field, out var raw);
                source = raw?.ToString();
            }

            if (string.IsNullOrWhiteSpace(source)) return;

            var fit = image.Fit switch
            {
                ImageFit.Cover => "cover",
                ImageFit.Fill => "fill",
                ImageFit.None => "none",
                _ => "contain"
            };

            html.Append("<img src=\"").Append(Html.Attr(source))
                .Append("\" style=\"width:100%;height:100%;object-fit:").Append(fit).Append(";\" alt=\"\">");
        }

        private static void WriteLine(StringBuilder html, LineElement line)
        {
            var css = new StringBuilder();
            if (line.Orientation == LineOrientation.Horizontal)
                css.Append("width:100%;height:0;border-top:")
                   .Append(CssBuilder.Pt(line.ThicknessPt)).Append(" solid ")
                   .Append(CssBuilder.CssSafe(line.Color)).Append(';');
            else
                css.Append("height:100%;width:0;border-left:")
                   .Append(CssBuilder.Pt(line.ThicknessPt)).Append(" solid ")
                   .Append(CssBuilder.CssSafe(line.Color)).Append(';');

            html.Append("<div").Append(Html.StyleAttr(css.ToString())).Append("></div>");
        }
    }

    /// <summary>A data set resolved and grouped, ready for rendering.</summary>
    public sealed record RenderedDataSet(ReportDataSet Data, GroupedRows Grouped);
}
