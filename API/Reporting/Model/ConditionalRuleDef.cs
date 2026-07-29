#nullable enable
namespace API.Reporting.Model
{
    /// <summary>
    /// Conditional formatting. Attachable to a band, an element, a table column, or a detail row.
    /// Evaluated on the server at render time, which is why the designer's preview and the final PDF
    /// can never disagree about what a rule did.
    /// </summary>
    public class ConditionalRuleDef
    {
        /// <summary>Optional label so a rule list stays readable in the designer.</summary>
        public string? Name { get; set; }

        /// <summary>
        /// C#-like boolean expression over Fields, Params, Group and Report. Examples:
        /// "Fields.total &gt; 10000", "Params.showVat == true",
        /// "Report.User.IsInRole(\"Manager\") == false".
        /// </summary>
        public string Expression { get; set; } = "";

        public RuleActions Actions { get; set; } = new();

        /// <summary>Disabled rules are kept in the template but skipped at render time.</summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// What happens when a rule matches. Null means "leave whatever the style chain produced".
    /// <see cref="Hide"/> short-circuits everything else — a hidden element is not rendered at all.
    /// </summary>
    public class RuleActions
    {
        public bool? Hide { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public bool? Underline { get; set; }
        public string? Color { get; set; }
        public string? BackColor { get; set; }
        public double? FontSizePt { get; set; }
        public HorizontalAlign? Align { get; set; }

        /// <summary>Replaces the rendered text outright. Tokens are still interpolated.</summary>
        public string? TextOverride { get; set; }

        public string? Format { get; set; }

        /// <summary>Projects the non-hide actions onto a style so they join the normal merge chain.</summary>
        public StyleDef ToStyle() => new()
        {
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Color = Color,
            BackColor = BackColor,
            FontSizePt = FontSizePt,
            Align = Align,
            Format = Format
        };
    }
}
