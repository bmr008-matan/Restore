#nullable enable
using System.Data;
using System.Text.Json;
using API.Reporting.Configuration;
using API.Reporting.Model;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace API.Reporting.Data
{
    /// <summary>
    /// Resolves a data set by calling the Oracle reporting package with a predefined table id and the
    /// caller's inputs, then reading the REF CURSOR it returns.
    ///
    /// The contract, with every identifier configurable under Reporting:Oracle so an existing package can
    /// be targeted without a code change:
    ///
    /// <code>
    /// PKG_REPORTS.GET_DATA(p_table_id IN NUMBER, p_params IN CLOB, p_cursor OUT SYS_REFCURSOR)
    /// </code>
    ///
    /// Inputs are passed as a JSON document in the CLOB rather than as a fixed parameter list, so adding
    /// a new input to a report never changes the procedure signature. Everything is bound as a real
    /// OracleParameter — no SQL text is ever built from user or template input.
    ///
    /// This is the only file in the project that touches a non-MIT dependency, which is why it sits
    /// behind IReportDataProvider: nothing upstream of this interface knows Oracle exists.
    /// </summary>
    public sealed class OracleRefCursorDataProvider : IReportDataProvider
    {
        private readonly ReportingOptions.OracleOptions _options;
        private readonly ILogger<OracleRefCursorDataProvider> _logger;

        public OracleRefCursorDataProvider(
            IOptions<ReportingOptions> options, ILogger<OracleRefCursorDataProvider> logger)
        {
            _options = options.Value.Oracle;
            _logger = logger;
        }

        public DataSourceKind Kind => DataSourceKind.OracleTableId;

        /// <summary>True when a connection string is configured, so start-up can skip registering this.</summary>
        public static bool IsConfigured(ReportingOptions options) =>
            !string.IsNullOrWhiteSpace(options.Oracle.ConnectionString);

        public async Task<ReportDataSet> GetDataAsync(
            DataSetDef dataSet,
            IReadOnlyDictionary<string, object?> inputs,
            int maxRows,
            CancellationToken cancellationToken)
        {
            if (dataSet.TableId is null)
                throw new InvalidOperationException(
                    $"Data set \"{dataSet.Key}\" reads from Oracle but has no table id.");

            if (string.IsNullOrWhiteSpace(_options.ConnectionString))
                throw new InvalidOperationException(
                    "Reporting:Oracle:ConnectionString is not configured, so Oracle data sets cannot be resolved.");

            await using var connection = new OracleConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = BuildCommand(connection, dataSet.TableId.Value, inputs, _options);

            // The cursor arrives as an output parameter, so the call itself returns no rows.
            await command.ExecuteNonQueryAsync(cancellationToken);

            var cursorParameter = command.Parameters[_options.CursorParameter];
            if (cursorParameter.Value is not OracleRefCursor cursor)
                throw new InvalidOperationException(
                    $"{_options.PackageName}.{_options.ProcedureName} did not return a cursor in " +
                    $"\"{_options.CursorParameter}\". Check that the parameter is declared OUT SYS_REFCURSOR.");

            await using var reader = cursor.GetDataReader();
            return Read(reader, dataSet, maxRows, _logger);
        }

        /// <summary>
        /// Builds the command. Separated out and internal so a test can assert on the parameters without
        /// needing a live database — the point being that every value is bound, never interpolated.
        /// </summary>
        internal static OracleCommand BuildCommand(
            OracleConnection connection,
            int tableId,
            IReadOnlyDictionary<string, object?> inputs,
            ReportingOptions.OracleOptions options)
        {
            var command = connection.CreateCommand();

            command.CommandText = $"{options.PackageName}.{options.ProcedureName}";
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = options.CommandTimeoutSeconds;

            // Without this ODP.NET binds positionally and ignores the names, so a package whose
            // parameters are declared in a different order would silently receive them swapped.
            command.BindByName = true;

            command.Parameters.Add(new OracleParameter(options.TableIdParameter, OracleDbType.Int32)
            {
                Direction = ParameterDirection.Input,
                Value = tableId
            });

            command.Parameters.Add(new OracleParameter(options.InputsParameter, OracleDbType.Clob)
            {
                Direction = ParameterDirection.Input,
                Value = SerialiseInputs(inputs)
            });

            command.Parameters.Add(new OracleParameter(options.CursorParameter, OracleDbType.RefCursor)
            {
                Direction = ParameterDirection.Output
            });

            return command;
        }

        /// <summary>
        /// Renders the inputs as a JSON object for the CLOB parameter. Dates go out as ISO 8601 so the
        /// package can parse them unambiguously regardless of either side's session settings, which is a
        /// common source of off-by-a-day bugs when dates travel as locale-formatted text.
        /// </summary>
        internal static string SerialiseInputs(IReadOnlyDictionary<string, object?> inputs)
        {
            var normalised = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, value) in inputs)
            {
                normalised[name] = value switch
                {
                    DateTime date => date.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    DateOnly date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    // A multi-value parameter arrives as a list; keep it a JSON array so the package can
                    // treat it as a collection rather than having to split a delimited string.
                    IEnumerable<object?> list => list.Select(NormaliseScalar).ToList(),
                    _ => NormaliseScalar(value)
                };
            }

            return JsonSerializer.Serialize(normalised, ReportJson.Options);
        }

        private static object? NormaliseScalar(object? value) => value switch
        {
            DateTime date => date.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            DBNull => null,
            _ => value
        };

        /// <summary>
        /// Reads the cursor into rows, taking field metadata from the cursor's own schema. Internal so a
        /// test can drive it with any IDataReader, which is how the mapping is covered without Oracle.
        /// </summary>
        internal static ReportDataSet Read(
            IDataReader reader, DataSetDef dataSet, int maxRows, ILogger? logger = null)
        {
            var fields = new List<FieldMeta>(reader.FieldCount);
            var names = new string[reader.FieldCount];

            for (var i = 0; i < reader.FieldCount; i++)
            {
                names[i] = reader.GetName(i);

                // The template's declared metadata wins when it has any, so a report's chosen captions and
                // formats survive; the cursor supplies the type for anything it did not declare.
                var declared = dataSet.FindField(names[i]);
                fields.Add(declared is not null
                    ? new FieldMeta
                    {
                        Name = declared.Name,
                        Caption = declared.Caption,
                        DataType = declared.DataType,
                        Format = declared.Format
                    }
                    : new FieldMeta
                    {
                        Name = names[i],
                        DataType = MapType(reader.GetFieldType(i))
                    });
            }

            var rows = new List<ReportRow>();
            var truncated = false;

            while (reader.Read())
            {
                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    logger?.LogWarning(
                        "Data set {Key} (table id {TableId}) hit the {MaxRows} row cap.",
                        dataSet.Key, dataSet.TableId, maxRows);
                    break;
                }

                var values = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                    values[names[i]] = reader.IsDBNull(i) ? null : Convert(reader.GetValue(i));

                rows.Add(new ReportRow(values));
            }

            return new ReportDataSet
            {
                Key = dataSet.Key,
                Fields = fields,
                Rows = rows,
                Truncated = truncated
            };
        }

        /// <summary>
        /// Unwraps ODP.NET's own value types into plain CLR values. Left alone they would reach the
        /// expression evaluator and formatter as OracleDecimal and friends, where every comparison and
        /// format would behave oddly.
        /// </summary>
        internal static object? Convert(object? value) => value switch
        {
            null or DBNull => null,
            OracleDecimal d => d.IsNull ? null : ToClrNumber(d),
            OracleString s => s.IsNull ? null : s.Value,
            OracleDate d => d.IsNull ? null : d.Value,
            OracleTimeStamp t => t.IsNull ? null : t.Value,
            OracleTimeStampTZ t => t.IsNull ? null : t.Value,
            OracleTimeStampLTZ t => t.IsNull ? null : t.Value,
            OracleClob c => c.IsNull ? null : c.Value,
            OracleBinary b => b.IsNull ? null : b.Value,
            OracleIntervalDS i => i.IsNull ? null : i.Value,
            _ => value
        };

        /// <summary>
        /// Oracle NUMBER has no direct CLR equivalent, so pick the narrowest type that holds it: whole
        /// numbers become long where they fit, everything else decimal. Values beyond decimal's range
        /// degrade to double rather than throwing, since a report should still print.
        /// </summary>
        private static object ToClrNumber(OracleDecimal number)
        {
            try
            {
                if (number.IsInt && OracleDecimal.Abs(number) <= long.MaxValue)
                    return number.ToInt64();

                return number.Value;
            }
            catch (OverflowException)
            {
                return number.ToDouble();
            }
        }

        internal static FieldDataType MapType(Type type)
        {
            if (type == typeof(bool)) return FieldDataType.Bool;
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return FieldDataType.Date;

            if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long))
                return FieldDataType.Int;

            if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
                return FieldDataType.Decimal;

            // OracleDecimal covers both integers and fractions, and the cursor's schema does not say
            // which. Decimal is the safe choice: it formats whole numbers correctly, whereas guessing Int
            // would truncate a price.
            if (type == typeof(OracleDecimal)) return FieldDataType.Decimal;

            return FieldDataType.String;
        }
    }
}
