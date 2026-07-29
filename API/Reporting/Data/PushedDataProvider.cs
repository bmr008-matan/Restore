#nullable enable
using System.Text.Json;
using API.Reporting.Model;

namespace API.Reporting.Data
{
    /// <summary>
    /// Serves a data set straight from rows supplied on the generate request instead of from a database.
    ///
    /// This is what lets one saved template run three ways with no template change: fully resolved by
    /// the server, fully supplied by the caller, or a mix (push the document header, fetch the detail
    /// lines). A caller that already holds its data skips the database round trip entirely.
    ///
    /// Rows are handed over per request rather than injected, so this provider is stateless and safe as
    /// a singleton.
    /// </summary>
    public sealed class PushedDataProvider : IReportDataProvider
    {
        /// <summary>Per-request rows, keyed by data set key.</summary>
        public sealed class Payload
        {
            private readonly Dictionary<string, List<ReportRow>> _rows = new(StringComparer.OrdinalIgnoreCase);

            public static readonly Payload Empty = new();

            public bool Has(string key) => _rows.ContainsKey(key);

            public IReadOnlyList<ReportRow> Get(string key) =>
                _rows.TryGetValue(key, out var rows) ? rows : Array.Empty<ReportRow>();

            public IEnumerable<string> Keys => _rows.Keys;

            public void Add(string key, List<ReportRow> rows) => _rows[key] = rows;

            /// <summary>
            /// Converts the JSON shape the API accepts — an object of arrays of flat objects — into rows.
            /// Nested objects and arrays are rejected rather than flattened or stringified, because
            /// quietly turning a nested object into "System.Object" in a cell is worse than a 400.
            /// </summary>
            public static Payload FromJson(
                IReadOnlyDictionary<string, JsonElement>? data, out List<string> errors)
            {
                var payload = new Payload();
                errors = new List<string>();

                if (data is null) return payload;

                foreach (var (key, element) in data)
                {
                    if (element.ValueKind != JsonValueKind.Array)
                    {
                        errors.Add($"Pushed data for \"{key}\" must be an array of objects.");
                        continue;
                    }

                    var rows = new List<ReportRow>();
                    var index = 0;

                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            errors.Add($"Pushed data for \"{key}\" row {index} must be an object.");
                            index++;
                            continue;
                        }

                        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var property in item.EnumerateObject())
                        {
                            if (!TryReadScalar(property.Value, out var value))
                            {
                                errors.Add(
                                    $"Pushed data for \"{key}\" row {index} field \"{property.Name}\" is a " +
                                    $"{property.Value.ValueKind}; only strings, numbers, booleans and null are supported.");
                                continue;
                            }
                            values[property.Name] = value;
                        }

                        rows.Add(new ReportRow(values));
                        index++;
                    }

                    payload.Add(key, rows);
                }

                return payload;
            }

            private static bool TryReadScalar(JsonElement element, out object? value)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        // ISO date strings become real dates so that date formatting and comparison work
                        // on pushed data exactly as they do on data from a provider.
                        var text = element.GetString();
                        value = LooksLikeDate(text) && DateTime.TryParse(text,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var parsed)
                            ? parsed
                            : text;
                        return true;

                    case JsonValueKind.Number:
                        value = element.TryGetInt64(out var l) ? l : element.GetDecimal();
                        return true;

                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        value = element.GetBoolean();
                        return true;

                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        value = null;
                        return true;

                    default:
                        value = null;
                        return false;
                }
            }

            /// <summary>
            /// Cheap shape check before attempting a date parse, so ordinary text like "2 of 3" is not
            /// accidentally interpreted as a date by a permissive parser.
            /// </summary>
            private static bool LooksLikeDate(string? text) =>
                text is { Length: >= 10 }
                && char.IsDigit(text[0]) && char.IsDigit(text[1]) && char.IsDigit(text[2]) && char.IsDigit(text[3])
                && text[4] == '-';
        }

        public DataSourceKind Kind => DataSourceKind.PushedData;

        private readonly Payload _payload;

        public PushedDataProvider(Payload payload) => _payload = payload;

        public Task<ReportDataSet> GetDataAsync(
            DataSetDef dataSet,
            IReadOnlyDictionary<string, object?> inputs,
            int maxRows,
            CancellationToken cancellationToken)
        {
            var rows = _payload.Get(dataSet.Key);
            var truncated = rows.Count > maxRows;

            return Task.FromResult(new ReportDataSet
            {
                Key = dataSet.Key,
                // Declared metadata wins so the template's formats and types apply; anything the caller
                // sent that the template does not declare is still carried, just untyped.
                Fields = MergeFields(dataSet, rows),
                Rows = truncated ? rows.Take(maxRows).ToList() : rows.ToList(),
                Truncated = truncated
            });
        }

        /// <summary>
        /// Reconciles the declared fields against what actually arrived. Extra fields are inferred from
        /// the first row rather than dropped; missing declared fields are left in place so their columns
        /// render empty and the diagnostic explains why, instead of the column silently disappearing.
        /// </summary>
        private static List<FieldMeta> MergeFields(DataSetDef dataSet, IReadOnlyList<ReportRow> rows)
        {
            var fields = dataSet.Fields.Select(f => new FieldMeta
            {
                Name = f.Name, Caption = f.Caption, DataType = f.DataType, Format = f.Format
            }).ToList();

            var known = new HashSet<string>(fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            var first = rows.FirstOrDefault();
            if (first is null) return fields;

            foreach (var (name, value) in first.Values)
            {
                if (!known.Add(name)) continue;
                fields.Add(new FieldMeta { Name = name, DataType = Infer(value) });
            }

            return fields;
        }

        private static FieldDataType Infer(object? value) => value switch
        {
            long or int or short => FieldDataType.Int,
            decimal or double or float => FieldDataType.Decimal,
            DateTime => FieldDataType.Date,
            bool => FieldDataType.Bool,
            _ => FieldDataType.String
        };
    }
}
