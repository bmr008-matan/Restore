#nullable enable
namespace API.Reporting.Model
{
    /// <summary>
    /// Visual styling. Every property is nullable and null always means "inherit from the level
    /// above" — report default, then band, then element, then column, then conditional rule.
    /// <see cref="Merge"/> is the single place that ordering is applied.
    /// </summary>
    public class StyleDef
    {
        public string? FontFamily { get; set; }
        public double? FontSizePt { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public bool? Underline { get; set; }

        /// <summary>CSS colour, e.g. "#B00020". Validated on save.</summary>
        public string? Color { get; set; }
        public string? BackColor { get; set; }

        public HorizontalAlign? Align { get; set; }
        public VerticalAlign? VAlign { get; set; }

        public BorderDef? Border { get; set; }
        public PaddingDef? Padding { get; set; }

        /// <summary>.NET format string applied to non-string values, e.g. "#,##0.00" or "dd/MM/yyyy".</summary>
        public string? Format { get; set; }

        public double? LineHeight { get; set; }

        /// <summary>
        /// Returns a new style where every property set on <paramref name="over"/> wins and
        /// everything else falls through to <paramref name="under"/>. Either side may be null.
        /// </summary>
        public static StyleDef? Merge(StyleDef? under, StyleDef? over)
        {
            if (under is null) return over;
            if (over is null) return under;

            return new StyleDef
            {
                FontFamily = over.FontFamily ?? under.FontFamily,
                FontSizePt = over.FontSizePt ?? under.FontSizePt,
                Bold = over.Bold ?? under.Bold,
                Italic = over.Italic ?? under.Italic,
                Underline = over.Underline ?? under.Underline,
                Color = over.Color ?? under.Color,
                BackColor = over.BackColor ?? under.BackColor,
                Align = over.Align ?? under.Align,
                VAlign = over.VAlign ?? under.VAlign,
                Border = BorderDef.Merge(under.Border, over.Border),
                Padding = over.Padding ?? under.Padding,
                Format = over.Format ?? under.Format,
                LineHeight = over.LineHeight ?? under.LineHeight
            };
        }

        /// <summary>Folds a whole chain of styles together, lowest priority first.</summary>
        public static StyleDef? MergeAll(params StyleDef?[] chain)
        {
            StyleDef? result = null;
            foreach (var style in chain) result = Merge(result, style);
            return result;
        }
    }

    public class BorderDef
    {
        public BorderSideDef? All { get; set; }
        public BorderSideDef? Top { get; set; }
        public BorderSideDef? Right { get; set; }
        public BorderSideDef? Bottom { get; set; }
        public BorderSideDef? Left { get; set; }

        public static BorderDef? Merge(BorderDef? under, BorderDef? over)
        {
            if (under is null) return over;
            if (over is null) return under;

            return new BorderDef
            {
                All = over.All ?? under.All,
                Top = over.Top ?? under.Top,
                Right = over.Right ?? under.Right,
                Bottom = over.Bottom ?? under.Bottom,
                Left = over.Left ?? under.Left
            };
        }
    }

    public class BorderSideDef
    {
        public BorderStyle Style { get; set; } = BorderStyle.Solid;
        public double WidthPt { get; set; } = 0.5;
        public string Color { get; set; } = "#000000";
    }

    public class PaddingDef
    {
        public double TopMm { get; set; }
        public double RightMm { get; set; } = 1;
        public double BottomMm { get; set; }
        public double LeftMm { get; set; } = 1;
    }
}
