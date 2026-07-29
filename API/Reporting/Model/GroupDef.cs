#nullable enable
using System.Text.Json.Serialization;
namespace API.Reporting.Model
{
    /// <summary>
    /// One level of grouping on a table. Multi-level grouping is expressed by the *order* of
    /// TableElement.Groups — index 0 is the outermost level, so reordering the list reorders the
    /// nesting and nothing else has to change.
    /// </summary>
    public class GroupDef
    {
        /// <summary>Field to group by. Mutually exclusive with <see cref="Expression"/>.</summary>
        public string? Field { get; set; }

        /// <summary>Expression to group by, for derived keys such as a year or a first letter.</summary>
        public string? Expression { get; set; }

        public SortDirection SortDirection { get; set; } = SortDirection.Asc;

        public bool ShowHeader { get; set; } = true;
        public bool ShowFooter { get; set; } = true;

        /// <summary>
        /// Group header label. Tokens are interpolated, so "{brand} ({Count} rows)" resolves against
        /// the group key, the group's aggregates and the report parameters.
        /// </summary>
        public string? HeaderText { get; set; }

        /// <summary>Group footer label, typically "Total {brand}:".</summary>
        public string? FooterText { get; set; }

        /// <summary>Totals printed in the group footer, aligned under their target columns.</summary>
        public List<AggregateDef> Aggregates { get; set; } = new();

        /// <summary>Totals printed in the group header, for a summary-before-detail layout.</summary>
        public List<AggregateDef> HeaderAggregates { get; set; } = new();

        public bool PageBreakBefore { get; set; }
        public bool PageBreakAfter { get; set; }

        /// <summary>
        /// Asks the renderer to keep the whole group on one page. Implemented by wrapping the group
        /// in a tbody with break-inside: avoid, so only the outermost level that requests it can be
        /// honoured — nested tbody elements are not valid HTML.
        /// </summary>
        public bool KeepTogether { get; set; }

        /// <summary>Reprints the group header when the group spills onto a new page.</summary>
        public bool RepeatHeaderOnNewPage { get; set; }

        /// <summary>Appends the row count to the group header.</summary>
        public bool ShowItemCount { get; set; }

        public StyleDef? HeaderStyle { get; set; }
        public StyleDef? FooterStyle { get; set; }

        public List<ConditionalRuleDef> Rules { get; set; } = new();

        /// <summary>Human-readable name for this level, used in validation messages and the designer.</summary>
        [JsonIgnore]
        public string DisplayKey => Field ?? Expression ?? "(unset)";
    }
}
