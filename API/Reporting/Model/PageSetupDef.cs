#nullable enable
using System.Text.Json.Serialization;

namespace API.Reporting.Model
{
    /// <summary>
    /// Owns everything about the physical page. The renderer emits this as a CSS @page rule and
    /// asks Chromium to prefer it, so page size and margins come from the definition alone.
    /// </summary>
    public class PageSetupDef
    {
        public PaperSize PaperSize { get; set; } = PaperSize.A4;

        /// <summary>Only consulted when <see cref="PaperSize"/> is <see cref="PaperSize.Custom"/>.</summary>
        public double? CustomWidthMm { get; set; }
        public double? CustomHeightMm { get; set; }

        public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

        public MarginsDef Margins { get; set; } = new();

        /// <summary>
        /// Height reserved for the repeating page header. Chromium draws the header template inside
        /// the top page margin, so this must not exceed <see cref="MarginsDef.TopMm"/>; the validator
        /// enforces that rather than letting the header silently clip.
        /// </summary>
        public double HeaderHeightMm { get; set; }

        public double FooterHeightMm { get; set; }

        public TextDirection Direction { get; set; } = TextDirection.Ltr;

        /// <summary>Culture used for all number and date formatting, e.g. "he-IL".</summary>
        public string Culture { get; set; } = "en-US";

        /// <summary>Paper dimensions in portrait, before orientation is applied.</summary>
        private static readonly Dictionary<PaperSize, (double W, double H)> PortraitSizesMm = new()
        {
            [PaperSize.A3] = (297, 420),
            [PaperSize.A4] = (210, 297),
            [PaperSize.A5] = (148, 210),
            [PaperSize.Letter] = (215.9, 279.4),
            [PaperSize.Legal] = (215.9, 355.6)
        };

        /// <summary>Final page width in mm, with orientation applied.</summary>
        [JsonIgnore]
        public double WidthMm => Orientation == PageOrientation.Portrait ? BaseSizeMm.W : BaseSizeMm.H;

        /// <summary>Final page height in mm, with orientation applied.</summary>
        [JsonIgnore]
        public double HeightMm => Orientation == PageOrientation.Portrait ? BaseSizeMm.H : BaseSizeMm.W;

        /// <summary>Width available to content once left and right margins are removed.</summary>
        [JsonIgnore]
        public double ContentWidthMm => WidthMm - Margins.LeftMm - Margins.RightMm;

        [JsonIgnore]
        private (double W, double H) BaseSizeMm =>
            PaperSize == PaperSize.Custom
                ? (CustomWidthMm ?? 210, CustomHeightMm ?? 297)
                : PortraitSizesMm[PaperSize];
    }

    public class MarginsDef
    {
        public double TopMm { get; set; } = 15;
        public double RightMm { get; set; } = 12;
        public double BottomMm { get; set; } = 15;
        public double LeftMm { get; set; } = 12;
    }
}
