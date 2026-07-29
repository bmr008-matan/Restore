#nullable enable
using System.Text.Json.Serialization;

namespace API.Reporting.Model.Elements
{
    /// <summary>
    /// Base of everything that can be placed on a band. Elements are positioned freely inside their
    /// band in millimetres, measured from the band's top-left in the report's reading direction.
    ///
    /// The type discriminator is a short string ("text", "table", ...) rather than a CLR type name so
    /// that stored template JSON does not depend on assembly or namespace names. Adding a new element
    /// type means one more JsonDerivedType attribute here and a case in the renderer — no migration,
    /// because the definition is persisted as a JSON column.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type",
                     UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
    [JsonDerivedType(typeof(TextElement), "text")]
    [JsonDerivedType(typeof(FieldElement), "field")]
    [JsonDerivedType(typeof(ImageElement), "image")]
    [JsonDerivedType(typeof(LineElement), "line")]
    [JsonDerivedType(typeof(BoxElement), "box")]
    [JsonDerivedType(typeof(TableElement), "table")]
    [JsonDerivedType(typeof(PageNumberElement), "pageNumber")]
    public abstract class ReportElement
    {
        /// <summary>Stable id. The designer uses it for selection; rules may target it.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

        /// <summary>Optional designer-facing name.</summary>
        public string? Name { get; set; }

        public double XMm { get; set; }
        public double YMm { get; set; }
        public double WidthMm { get; set; } = 40;
        public double HeightMm { get; set; } = 6;

        public bool Visible { get; set; } = true;

        public StyleDef? Style { get; set; }

        public List<ConditionalRuleDef> Rules { get; set; } = new();

        /// <summary>
        /// Overrides the report direction for this element only — the escape hatch for an LTR part
        /// number or account code sitting inside an otherwise right-to-left report.
        /// </summary>
        public TextDirection? Direction { get; set; }
    }
}
