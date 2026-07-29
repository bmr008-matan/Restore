#nullable enable
using API.Reporting.Model;

namespace API.Reporting.Entities
{
    /// <summary>
    /// One entry in the predefined table-id catalogue — the fixed list of data sources a designer may
    /// bind a table or a lookup parameter to.
    ///
    /// The catalogue is what keeps query text out of the browser entirely: the designer picks a table id,
    /// and the Oracle reporting package decides what that id means. Field metadata is cached here so the
    /// designer's field picker works without a round trip to Oracle.
    /// </summary>
    public class ReportTableSource
    {
        /// <summary>The predefined id passed to the data source. Assigned, not generated.</summary>
        public int TableId { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        /// <summary>Which provider resolves this id.</summary>
        public DataSourceKind SourceKind { get; set; } = DataSourceKind.OracleTableId;

        /// <summary>Serialised <see cref="FieldMeta"/> list.</summary>
        public string FieldsJson { get; set; } = "[]";

        /// <summary>
        /// Serialised list of <see cref="TableSourceInput"/> describing what this source expects to be
        /// given, so the designer can prompt for the right inputs instead of the author guessing.
        /// </summary>
        public string InputsJson { get; set; } = "[]";

        /// <summary>Hidden sources stay resolvable for existing templates but are not offered.</summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Describes one input a table source expects.</summary>
    public class TableSourceInput
    {
        public string Name { get; set; } = "";
        public string? Label { get; set; }
        public FieldDataType DataType { get; set; } = FieldDataType.String;
        public bool Required { get; set; }
    }
}
