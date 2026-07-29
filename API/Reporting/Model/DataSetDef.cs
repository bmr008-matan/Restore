#nullable enable
namespace API.Reporting.Model
{
    /// <summary>
    /// The "select connected to it" — a named result set the report can bind tables and fields to.
    /// A data set never carries SQL. It names a predefined table id, and the Oracle reporting package
    /// decides what that means, which keeps query text out of the browser entirely.
    /// </summary>
    public class DataSetDef
    {
        /// <summary>Key referenced by TableElement.DataSetKey and by field lookups.</summary>
        public string Key { get; set; } = "";

        public string? Name { get; set; }

        public DataSourceKind SourceKind { get; set; } = DataSourceKind.OracleTableId;

        /// <summary>Predefined table id from the ReportTableSource catalogue.</summary>
        public int? TableId { get; set; }

        /// <summary>The "enter data" the Oracle package needs alongside the table id.</summary>
        public List<DataSetInput> Inputs { get; set; } = new();

        /// <summary>
        /// Field metadata cached from the catalogue so the designer's field picker works without a
        /// round trip to Oracle. Reconciled against what the provider actually returns at run time.
        /// </summary>
        public List<FieldMeta> Fields { get; set; } = new();

        /// <summary>Applied after group sorting, so it orders rows within the innermost group.</summary>
        public List<SortDef> Sort { get; set; } = new();

        /// <summary>Per-data-set row cap. Falls back to the configured global limit when unset.</summary>
        public int? MaxRows { get; set; }

        public FieldMeta? FindField(string name) =>
            Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One input passed to the data source, bound as a real parameter and never concatenated.</summary>
    public class DataSetInput
    {
        /// <summary>Name the data source knows this input by.</summary>
        public string Name { get; set; } = "";

        public InputKind Kind { get; set; } = InputKind.Literal;

        /// <summary>
        /// A constant for <see cref="InputKind.Literal"/>, a report parameter name for
        /// <see cref="InputKind.Parameter"/>, or an expression for <see cref="InputKind.Expression"/>.
        /// </summary>
        public string? Value { get; set; }
    }

    public class SortDef
    {
        public string Field { get; set; } = "";
        public SortDirection Direction { get; set; } = SortDirection.Asc;
    }
}
