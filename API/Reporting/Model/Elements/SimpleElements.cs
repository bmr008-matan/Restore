#nullable enable
namespace API.Reporting.Model.Elements
{
    /// <summary>
    /// Static or token-bearing text. Tokens are interpolated at render time: {@paramName} for a
    /// parameter, {fieldName} for a field of the bound data set, and {ReportTitle}, {Date},
    /// {DateTime}, {CurrentUser} for report metadata.
    /// </summary>
    public class TextElement : ReportElement
    {
        public string Text { get; set; } = "";

        /// <summary>Data set used to resolve bare {field} tokens. Optional.</summary>
        public string? DataSetKey { get; set; }
    }

    /// <summary>A single bound value — one field or one expression, formatted.</summary>
    public class FieldElement : ReportElement
    {
        public string? Field { get; set; }
        public string? Expression { get; set; }

        /// <summary>Data set the field comes from. Required when the report has more than one.</summary>
        public string? DataSetKey { get; set; }

        public string? Format { get; set; }

        /// <summary>Printed when the resolved value is null or empty.</summary>
        public string? NullText { get; set; }
    }

    /// <summary>
    /// An image. Either a fixed <see cref="Source"/> or a per-row <see cref="Field"/> holding a path
    /// or data URI. Chromium's header and footer templates cannot fetch external URLs, so images used
    /// in page bands are inlined as data URIs by the renderer.
    /// </summary>
    public class ImageElement : ReportElement
    {
        /// <summary>Data URI, or a path relative to the client's public folder.</summary>
        public string? Source { get; set; }

        public string? Field { get; set; }
        public string? DataSetKey { get; set; }

        public ImageFit Fit { get; set; } = ImageFit.Contain;
    }

    /// <summary>A rule. Thickness and colour come from the element's own properties, not its style.</summary>
    public class LineElement : ReportElement
    {
        public LineOrientation Orientation { get; set; } = LineOrientation.Horizontal;
        public double ThicknessPt { get; set; } = 0.5;
        public string Color { get; set; } = "#000000";
    }

    /// <summary>A filled or outlined rectangle, for panels and backgrounds. Styling only.</summary>
    public class BoxElement : ReportElement
    {
        public double CornerRadiusMm { get; set; }
    }

    /// <summary>
    /// Page numbering. Only meaningful inside a PageHeader or PageFooter band: page numbers are
    /// substituted by Chromium itself, and Chromium only does that inside its header and footer
    /// templates. The validator rejects this element anywhere else rather than rendering a literal.
    /// </summary>
    public class PageNumberElement : ReportElement
    {
        /// <summary>Supports {PageNumber} and {TotalPages}, plus the usual metadata tokens.</summary>
        public string Template { get; set; } = "{PageNumber} / {TotalPages}";
    }
}
