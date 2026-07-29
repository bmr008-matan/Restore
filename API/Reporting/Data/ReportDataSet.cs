#nullable enable
using API.Reporting.Model;

namespace API.Reporting.Data
{
    /// <summary>
    /// One row of report data. Field lookup is case-insensitive on purpose: Oracle hands back
    /// UPPERCASE column names while a designer naturally types "total", and a report should not break
    /// over that.
    /// </summary>
    public sealed class ReportRow
    {
        private readonly Dictionary<string, object?> _values;

        public ReportRow(IDictionary<string, object?> values) =>
            _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);

        public ReportRow() =>
            _values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns null for an unknown field rather than throwing — a missing column in one
        /// row of a pushed data set should not abort the whole render.</summary>
        public object? this[string field]
        {
            get => field is not null && _values.TryGetValue(field, out var v) ? v : null;
            set => _values[field] = value;
        }

        public bool Has(string field) => _values.ContainsKey(field);

        public IReadOnlyDictionary<string, object?> Values => _values;
    }

    /// <summary>A resolved data set: the field metadata actually returned, plus the rows.</summary>
    public sealed class ReportDataSet
    {
        public required string Key { get; init; }

        public List<FieldMeta> Fields { get; init; } = new();

        public List<ReportRow> Rows { get; init; } = new();

        /// <summary>True when the provider stopped at the configured row cap.</summary>
        public bool Truncated { get; set; }

        public static ReportDataSet Empty(string key) => new() { Key = key };

        public FieldMeta? FindField(string name) =>
            Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
