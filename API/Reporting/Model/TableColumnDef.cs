#nullable enable
using System.Text.Json.Serialization;
namespace API.Reporting.Model
{
    /// <summary>One column of a data table.</summary>
    public class TableColumnDef
    {
        /// <summary>Field to print. Mutually exclusive with <see cref="Expression"/>.</summary>
        public string? Field { get; set; }

        /// <summary>Computed value, e.g. "Fields.qty * Fields.price".</summary>
        public string? Expression { get; set; }

        /// <summary>Header text. Falls back to the field's caption, then the field name.</summary>
        public string? Caption { get; set; }

        /// <summary>
        /// Fixed width. When neither width is given the column shares the remaining space equally.
        /// Percent wins over mm if both are set.
        /// </summary>
        public double? WidthMm { get; set; }
        public double? WidthPercent { get; set; }

        public HorizontalAlign? Align { get; set; }
        public string? Format { get; set; }

        public StyleDef? Style { get; set; }
        public StyleDef? HeaderStyle { get; set; }

        public bool Visible { get; set; } = true;

        /// <summary>Rules evaluated per row, so a cell can be styled from its own row's values.</summary>
        public List<ConditionalRuleDef> Rules { get; set; } = new();

        /// <summary>Stable identity for the designer's column list and for aggregate targeting.</summary>
        [JsonIgnore]
        public string ColumnKey => Field ?? Caption ?? Expression ?? "";
    }
}
