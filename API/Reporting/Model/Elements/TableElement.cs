#nullable enable
using System.Text.Json.Serialization;
namespace API.Reporting.Model.Elements
{
    /// <summary>
    /// A data table: the element that grows down the page, paginates, and carries the multi-level
    /// grouping. Rendered as a real HTML table because that is what lets Chromium repeat the column
    /// header row on every page.
    /// </summary>
    public class TableElement : ReportElement
    {
        /// <summary>Data set supplying the rows. Required.</summary>
        public string DataSetKey { get; set; } = "";

        public List<TableColumnDef> Columns { get; set; } = new();

        /// <summary>
        /// Group levels, outermost first. An empty list means a flat table. Reordering this list
        /// reorders the nesting — nothing else in the definition refers to a level by index.
        /// </summary>
        public List<GroupDef> Groups { get; set; } = new();

        /// <summary>Totals over every row, printed once at the end of the table.</summary>
        public List<AggregateDef> GrandTotals { get; set; } = new();

        public string? GrandTotalLabel { get; set; } = "Total";

        public bool ShowColumnHeaders { get; set; } = true;

        /// <summary>
        /// Emits the column headers in a thead, which Chromium reprints after every page break.
        /// Turning this off moves them into the body so they appear once.
        /// </summary>
        public bool RepeatHeaderOnEachPage { get; set; } = true;

        public StyleDef? HeaderStyle { get; set; }
        public StyleDef? RowStyle { get; set; }

        /// <summary>Applied to odd-numbered detail rows for banding. Null disables banding.</summary>
        public StyleDef? AlternateRowStyle { get; set; }

        public StyleDef? GrandTotalStyle { get; set; }

        /// <summary>Border applied to every cell.</summary>
        public BorderDef? CellBorder { get; set; }

        /// <summary>Rules evaluated once per detail row, for whole-row highlighting.</summary>
        public List<ConditionalRuleDef> RowRules { get; set; } = new();

        /// <summary>Printed instead of the table when the data set comes back empty.</summary>
        public string? EmptyText { get; set; } = "No data";

        [JsonIgnore]
        public IEnumerable<TableColumnDef> VisibleColumns => Columns.Where(c => c.Visible);
    }
}
