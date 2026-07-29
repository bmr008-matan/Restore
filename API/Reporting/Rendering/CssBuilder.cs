#nullable enable
using System.Globalization;
using System.Text;
using API.Reporting.Model;

namespace API.Reporting.Rendering
{
    /// <summary>
    /// Turns page setup and style definitions into CSS.
    ///
    /// All measurements stay in millimetres rather than being converted to pixels: the definition is
    /// authored in mm, CSS understands mm natively, and Chromium does the physical conversion at print
    /// time. Converting to px in between would introduce rounding for no benefit.
    /// </summary>
    public static class CssBuilder
    {
        private static readonly CultureInfo Css = CultureInfo.InvariantCulture;

        /// <summary>Formats a number for CSS, always with a dot separator regardless of server locale.</summary>
        public static string Mm(double value) => value.ToString("0.###", Css) + "mm";

        public static string Pt(double value) => value.ToString("0.###", Css) + "pt";

        /// <summary>
        /// The document stylesheet. The @page rule is what actually sets paper size, orientation and
        /// margins — the renderer asks Chromium to prefer it over its own PDF options, so page setup
        /// lives in one place only.
        /// </summary>
        public static string BuildDocumentCss(ReportDefinition definition, string fontFamily, string? fontFaceCss)
        {
            var page = definition.Page;
            var css = new StringBuilder();

            if (!string.IsNullOrEmpty(fontFaceCss)) css.Append(fontFaceCss).Append('\n');

            css.Append("@page{size:")
               .Append(Mm(page.WidthMm)).Append(' ').Append(Mm(page.HeightMm))
               .Append(";margin:")
               .Append(Mm(page.Margins.TopMm)).Append(' ')
               .Append(Mm(page.Margins.RightMm)).Append(' ')
               .Append(Mm(page.Margins.BottomMm)).Append(' ')
               .Append(Mm(page.Margins.LeftMm))
               .Append(";}\n");

            css.Append("*{box-sizing:border-box;}\n");

            css.Append("html,body{margin:0;padding:0;}\n");
            css.Append("body{font-family:").Append(fontFamily)
               .Append(";font-size:10pt;line-height:1.25;")
               .Append("direction:").Append(Direction(page.Direction)).Append(';')
               // Chromium prints backgrounds only when asked; without this every BackColor is dropped.
               .Append("-webkit-print-color-adjust:exact;print-color-adjust:exact;")
               .Append("}\n");

            // Bands are ordinary blocks in document flow. Report header and footer therefore appear
            // once, at the start and end, which is exactly the first-page/last-page behaviour wanted.
            css.Append(".rpt-band{position:relative;width:100%;}\n");
            css.Append(".rpt-el{position:absolute;overflow:hidden;}\n");
            // Absolutely positioned elements are placed with physical left/right so that an RTL report
            // mirrors its layout instead of merely flipping text.
            css.Append(".rpt-text{white-space:pre-wrap;word-wrap:break-word;}\n");

            AppendTableCss(css);

            return css.ToString();
        }

        private static void AppendTableCss(StringBuilder css)
        {
            css.Append("table.rpt-table{width:100%;border-collapse:collapse;table-layout:fixed;}\n");

            // thead is what makes Chromium reprint column headers after every page break. This single
            // line is the whole implementation of "repeat header on each page".
            //
            // There is deliberately no tfoot rule to match: a table-footer-group repeats on *every*
            // page, which is wrong for a grand total. Grand totals are emitted as the last body row.
            css.Append("table.rpt-table thead{display:table-header-group;}\n");

            // Rows must never be split across a page boundary.
            css.Append("table.rpt-table tr{break-inside:avoid;page-break-inside:avoid;}\n");

            css.Append("table.rpt-table th,table.rpt-table td{")
               .Append("padding:1mm;vertical-align:top;overflow-wrap:break-word;")
               .Append("}\n");

            // Keep-together is implemented on tbody, which is why only the outermost group level that
            // asks for it can be honoured — tbody elements cannot nest.
            css.Append("tbody.rpt-keep{break-inside:avoid;page-break-inside:avoid;}\n");

            css.Append(".rpt-break-before{break-before:page;page-break-before:always;}\n");
            css.Append(".rpt-break-after{break-after:page;page-break-after:always;}\n");

            css.Append(".rpt-empty{padding:4mm;text-align:center;font-style:italic;color:#666;}\n");
        }

        public static string Direction(TextDirection direction) =>
            direction == TextDirection.Rtl ? "rtl" : "ltr";

        /// <summary>
        /// Renders a style as inline declarations. Inline rather than a class because the page header
        /// and footer are handed to Chromium as standalone HTML fragments where external CSS is ignored
        /// outright — so the renderer produces inline styles everywhere and stays consistent.
        /// </summary>
        public static string InlineStyle(StyleDef? style, TextDirection? direction = null)
        {
            if (style is null && direction is null) return string.Empty;

            var css = new StringBuilder();

            if (style is not null)
            {
                if (style.FontFamily is { Length: > 0 }) css.Append("font-family:").Append(CssSafe(style.FontFamily)).Append(';');
                if (style.FontSizePt is > 0) css.Append("font-size:").Append(Pt(style.FontSizePt.Value)).Append(';');
                if (style.Bold == true) css.Append("font-weight:bold;");
                if (style.Bold == false) css.Append("font-weight:normal;");
                if (style.Italic == true) css.Append("font-style:italic;");
                if (style.Underline == true) css.Append("text-decoration:underline;");
                if (style.Color is { Length: > 0 }) css.Append("color:").Append(CssSafe(style.Color)).Append(';');
                if (style.BackColor is { Length: > 0 }) css.Append("background-color:").Append(CssSafe(style.BackColor)).Append(';');
                if (style.Align is not null) css.Append("text-align:").Append(Align(style.Align.Value)).Append(';');
                if (style.VAlign is not null) css.Append("vertical-align:").Append(VAlign(style.VAlign.Value)).Append(';');
                if (style.LineHeight is > 0) css.Append("line-height:").Append(style.LineHeight.Value.ToString("0.##", Css)).Append(';');

                AppendPadding(css, style.Padding);
                AppendBorder(css, style.Border);
            }

            if (direction is not null)
            {
                css.Append("direction:").Append(Direction(direction.Value)).Append(';');
                // An explicit direction on one element is almost always about isolating a run of text
                // (a part number, an account code) from the surrounding paragraph.
                css.Append("unicode-bidi:isolate;");
            }

            return css.ToString();
        }

        private static void AppendPadding(StringBuilder css, PaddingDef? padding)
        {
            if (padding is null) return;
            css.Append("padding:")
               .Append(Mm(padding.TopMm)).Append(' ')
               .Append(Mm(padding.RightMm)).Append(' ')
               .Append(Mm(padding.BottomMm)).Append(' ')
               .Append(Mm(padding.LeftMm)).Append(';');
        }

        private static void AppendBorder(StringBuilder css, BorderDef? border)
        {
            if (border is null) return;

            if (border.All is not null) css.Append("border:").Append(Side(border.All)).Append(';');
            if (border.Top is not null) css.Append("border-top:").Append(Side(border.Top)).Append(';');
            if (border.Right is not null) css.Append("border-right:").Append(Side(border.Right)).Append(';');
            if (border.Bottom is not null) css.Append("border-bottom:").Append(Side(border.Bottom)).Append(';');
            if (border.Left is not null) css.Append("border-left:").Append(Side(border.Left)).Append(';');
        }

        private static string Side(BorderSideDef side) =>
            side.Style == BorderStyle.None
                ? "none"
                : $"{Pt(side.WidthPt)} {side.Style.ToString().ToLowerInvariant()} {CssSafe(side.Color)}";

        /// <summary>
        /// Start and End map to CSS logical values, so "start" means left in an LTR report and right in
        /// an RTL one — which is what a designer means when aligning a label to the reading edge.
        /// </summary>
        public static string Align(HorizontalAlign align) => align switch
        {
            HorizontalAlign.Center => "center",
            HorizontalAlign.Right => "right",
            HorizontalAlign.Justify => "justify",
            HorizontalAlign.Start => "start",
            HorizontalAlign.End => "end",
            _ => "left"
        };

        public static string VAlign(VerticalAlign align) => align switch
        {
            VerticalAlign.Middle => "middle",
            VerticalAlign.Bottom => "bottom",
            _ => "top"
        };

        /// <summary>
        /// Last-ditch guard for values that end up inside a CSS declaration. The validator already
        /// rejects malformed colours at save time; this makes sure a definition that reached the
        /// renderer by some other route still cannot break out of the declaration it sits in.
        /// </summary>
        public static string CssSafe(string value)
        {
            var cleaned = value.Replace(";", string.Empty)
                               .Replace("{", string.Empty)
                               .Replace("}", string.Empty)
                               .Replace("<", string.Empty)
                               .Replace(">", string.Empty)
                               .Replace("\"", string.Empty)
                               .Replace("\\", string.Empty);
            return cleaned.Trim();
        }
    }
}
