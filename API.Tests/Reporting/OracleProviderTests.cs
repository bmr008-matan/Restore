using System.Data;
using System.Text.Json;
using API.Reporting.Configuration;
using API.Reporting.Data;
using API.Reporting.Model;
using Oracle.ManagedDataAccess.Client;

namespace API.Tests.Reporting;

/// <summary>
/// Covers the Oracle provider's testable surface: how inputs are bound, how the cursor is read, and how
/// Oracle types map onto the report model.
///
/// No Oracle instance is involved, and that is a real limitation — the actual call to the package needs
/// one round of verification against a live database. What these tests can prove is the part most likely
/// to be wrong and most costly if it is: that every value is bound as a parameter rather than
/// concatenated into SQL, that parameter names are honoured rather than positions, and that Oracle's own
/// value types never leak into the engine.
/// </summary>
public class OracleProviderTests
{
    private static readonly ReportingOptions.OracleOptions DefaultOptions = new();

    private static OracleCommand BuildCommand(
        IReadOnlyDictionary<string, object?> inputs,
        ReportingOptions.OracleOptions? options = null,
        int tableId = 1042)
    {
        // Constructing a command needs no open connection, which is what makes this assertable offline.
        var connection = new OracleConnection();
        return OracleRefCursorDataProvider.BuildCommand(connection, tableId, inputs, options ?? DefaultOptions);
    }

    [Fact]
    public void TheCommandIsAStoredProcedureCall_NotConstructedSql()
    {
        using var command = BuildCommand(new Dictionary<string, object?>());

        Assert.Equal(CommandType.StoredProcedure, command.CommandType);
        Assert.Equal("PKG_REPORTS.GET_DATA", command.CommandText);

        // Nothing that could carry an injected fragment: no SELECT, no concatenation, no literals.
        Assert.DoesNotContain("SELECT", command.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("'", command.CommandText);
    }

    [Fact]
    public void ParametersAreBoundByName_SoADifferentDeclarationOrderCannotSwapThem()
    {
        using var command = BuildCommand(new Dictionary<string, object?>());

        Assert.True(command.BindByName);
    }

    [Fact]
    public void TheTableIdIsBoundAsANumericInputParameter()
    {
        using var command = BuildCommand(new Dictionary<string, object?>(), tableId: 7788);

        var parameter = command.Parameters["p_table_id"];
        Assert.Equal(OracleDbType.Int32, parameter.OracleDbType);
        Assert.Equal(ParameterDirection.Input, parameter.Direction);
        Assert.Equal(7788, parameter.Value);
    }

    [Fact]
    public void TheCursorIsAnOutputRefCursorParameter()
    {
        using var command = BuildCommand(new Dictionary<string, object?>());

        var parameter = command.Parameters["p_cursor"];
        Assert.Equal(OracleDbType.RefCursor, parameter.OracleDbType);
        Assert.Equal(ParameterDirection.Output, parameter.Direction);
    }

    [Fact]
    public void InputsTravelInASingleClobParameter()
    {
        using var command = BuildCommand(new Dictionary<string, object?> { ["brand"] = "Nike" });

        var parameter = command.Parameters["p_params"];
        Assert.Equal(OracleDbType.Clob, parameter.OracleDbType);
        Assert.Equal(ParameterDirection.Input, parameter.Direction);
    }

    [Fact]
    public void EveryPackageAndParameterNameIsConfigurable()
    {
        // The point of this: an existing package can be targeted from appsettings with no code change.
        var options = new ReportingOptions.OracleOptions
        {
            PackageName = "MY_SCHEMA.RPT_PKG",
            ProcedureName = "FETCH_ROWS",
            TableIdParameter = "in_table",
            InputsParameter = "in_args",
            CursorParameter = "out_rows"
        };

        using var command = BuildCommand(new Dictionary<string, object?>(), options);

        Assert.Equal("MY_SCHEMA.RPT_PKG.FETCH_ROWS", command.CommandText);
        Assert.NotNull(command.Parameters["in_table"]);
        Assert.NotNull(command.Parameters["in_args"]);
        Assert.NotNull(command.Parameters["out_rows"]);
    }

    [Fact]
    public void AValueContainingSqlIsCarriedAsData_NotAsPartOfTheStatement()
    {
        var hostile = "Nike'; DROP TABLE PRODUCTS; --";

        using var command = BuildCommand(new Dictionary<string, object?> { ["brand"] = hostile });

        // Never in the command text — the statement is a fixed procedure call.
        Assert.DoesNotContain("DROP TABLE", command.CommandText, StringComparison.OrdinalIgnoreCase);

        // It survives intact inside the bound CLOB payload, as data. Compared after JSON decoding
        // because System.Text.Json escapes the apostrophe to ' — itself a second layer of defence,
        // since the package receives it as a JSON string value rather than as raw text.
        using var payload = JsonDocument.Parse((string)command.Parameters["p_params"].Value!);
        Assert.Equal(hostile, payload.RootElement.GetProperty("brand").GetString());
    }

    [Fact]
    public void InputsSerialiseAsAJsonObject()
    {
        var json = OracleRefCursorDataProvider.SerialiseInputs(new Dictionary<string, object?>
        {
            ["brand"] = "Nike",
            ["minPrice"] = 12.5m,
            ["inStockOnly"] = true,
            ["missing"] = null
        });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("Nike", root.GetProperty("brand").GetString());
        Assert.Equal(12.5m, root.GetProperty("minPrice").GetDecimal());
        Assert.True(root.GetProperty("inStockOnly").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("missing").ValueKind);
    }

    [Fact]
    public void DatesSerialiseAsIso8601_SoSessionSettingsCannotShiftThem()
    {
        // Dates travelling as locale-formatted text is a classic source of off-by-a-day bugs.
        var json = OracleRefCursorDataProvider.SerialiseInputs(new Dictionary<string, object?>
        {
            ["fromDate"] = new DateTime(2026, 1, 31, 14, 30, 0)
        });

        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("fromDate").GetString();

        Assert.StartsWith("2026-01-31T14:30:00", value);
    }

    [Fact]
    public void MultiValueInputsSerialiseAsAJsonArray()
    {
        // A collection, so the package does not have to split a delimited string.
        var json = OracleRefCursorDataProvider.SerialiseInputs(new Dictionary<string, object?>
        {
            ["branchIds"] = new List<object?> { 3L, 7L, 11L }
        });

        using var document = JsonDocument.Parse(json);
        var array = document.RootElement.GetProperty("branchIds");

        Assert.Equal(JsonValueKind.Array, array.ValueKind);
        Assert.Equal(3, array.GetArrayLength());
    }

    [Fact]
    public void ReadingACursor_TakesFieldNamesAndTypesFromItsSchema()
    {
        var reader = FakeReader.With(
            columns: new[] { ("SKU", typeof(string)), ("QTY", typeof(long)), ("TOTAL", typeof(decimal)) },
            rows: new object?[][]
            {
                new object?[] { "A-1", 3L, 30.5m },
                new object?[] { "A-2", 5L, 50.0m }
            });

        var result = OracleRefCursorDataProvider.Read(reader, new DataSetDef { Key = "detail" }, maxRows: 100);

        Assert.Equal(new[] { "SKU", "QTY", "TOTAL" }, result.Fields.Select(f => f.Name));
        Assert.Equal(FieldDataType.String, result.Fields[0].DataType);
        Assert.Equal(FieldDataType.Int, result.Fields[1].DataType);
        Assert.Equal(FieldDataType.Decimal, result.Fields[2].DataType);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("A-1", result.Rows[0]["SKU"]);
    }

    [Fact]
    public void DeclaredMetadataWins_SoATemplatesCaptionsAndFormatsSurvive()
    {
        var dataSet = new DataSetDef
        {
            Key = "detail",
            Fields =
            {
                new FieldMeta { Name = "TOTAL", Caption = "Line total", DataType = FieldDataType.Decimal, Format = "#,##0.00" }
            }
        };

        var reader = FakeReader.With(
            columns: new[] { ("TOTAL", typeof(decimal)) },
            rows: new object?[][] { new object?[] { 10m } });

        var result = OracleRefCursorDataProvider.Read(reader, dataSet, maxRows: 10);

        Assert.Equal("Line total", result.Fields[0].Caption);
        Assert.Equal("#,##0.00", result.Fields[0].Format);
    }

    [Fact]
    public void ColumnNamesAreMatchedCaseInsensitively_BecauseOracleReturnsThemUppercase()
    {
        // A designer types "total"; Oracle hands back "TOTAL". A report must not break over that.
        var reader = FakeReader.With(
            columns: new[] { ("TOTAL", typeof(decimal)) },
            rows: new object?[][] { new object?[] { 42m } });

        var result = OracleRefCursorDataProvider.Read(reader, new DataSetDef { Key = "d" }, maxRows: 10);

        Assert.Equal(42m, result.Rows[0]["total"]);
        Assert.Equal(42m, result.Rows[0]["Total"]);
    }

    [Fact]
    public void NullsBecomeNull_NotDBNull()
    {
        var reader = FakeReader.With(
            columns: new[] { ("TOTAL", typeof(decimal)) },
            rows: new object?[][] { new object?[] { null } });

        var result = OracleRefCursorDataProvider.Read(reader, new DataSetDef { Key = "d" }, maxRows: 10);

        Assert.Null(result.Rows[0]["TOTAL"]);
    }

    [Fact]
    public void ReadingStopsAtTheRowCapAndSaysSo()
    {
        var rows = Enumerable.Range(0, 50)
            .Select(i => new object?[] { (long)i })
            .ToArray();

        var reader = FakeReader.With(new[] { ("N", typeof(long)) }, rows);

        var result = OracleRefCursorDataProvider.Read(reader, new DataSetDef { Key = "d" }, maxRows: 10);

        Assert.Equal(10, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    [Theory]
    [InlineData(typeof(string), FieldDataType.String)]
    [InlineData(typeof(long), FieldDataType.Int)]
    [InlineData(typeof(int), FieldDataType.Int)]
    [InlineData(typeof(decimal), FieldDataType.Decimal)]
    [InlineData(typeof(double), FieldDataType.Decimal)]
    [InlineData(typeof(DateTime), FieldDataType.Date)]
    [InlineData(typeof(bool), FieldDataType.Bool)]
    [InlineData(typeof(byte[]), FieldDataType.String)]
    public void ClrTypesMapOntoTheReportModel(Type clrType, FieldDataType expected)
    {
        Assert.Equal(expected, OracleRefCursorDataProvider.MapType(clrType));
    }

    [Fact]
    public void OracleNumberMapsToDecimal_BecauseGuessingIntWouldTruncateAPrice()
    {
        // The cursor schema does not say whether a NUMBER column holds integers or fractions.
        Assert.Equal(FieldDataType.Decimal,
            OracleRefCursorDataProvider.MapType(typeof(Oracle.ManagedDataAccess.Types.OracleDecimal)));
    }

    [Fact]
    public void OracleValueTypesAreUnwrappedSoTheyNeverReachTheEngine()
    {
        // Left wrapped, these would make every comparison and format in the engine behave oddly.
        Assert.Equal(42L, OracleRefCursorDataProvider.Convert(new Oracle.ManagedDataAccess.Types.OracleDecimal(42)));
        Assert.Equal(1.5m, OracleRefCursorDataProvider.Convert(new Oracle.ManagedDataAccess.Types.OracleDecimal(1.5m)));
        Assert.Equal("hi", OracleRefCursorDataProvider.Convert(new Oracle.ManagedDataAccess.Types.OracleString("hi")));
        Assert.Null(OracleRefCursorDataProvider.Convert(Oracle.ManagedDataAccess.Types.OracleString.Null));
        Assert.Null(OracleRefCursorDataProvider.Convert(DBNull.Value));
        Assert.Null(OracleRefCursorDataProvider.Convert(null));
    }

    [Fact]
    public void TheProviderIsOnlyConsideredConfiguredWhenAConnectionStringExists()
    {
        var without = new ReportingOptions();
        Assert.False(OracleRefCursorDataProvider.IsConfigured(without));

        var with = new ReportingOptions();
        with.Oracle.ConnectionString = "User Id=x;Password=y;Data Source=z";
        Assert.True(OracleRefCursorDataProvider.IsConfigured(with));
    }

    [Fact]
    public async Task ADataSetWithNoTableId_FailsWithAClearMessage()
    {
        var options = new ReportingOptions();
        options.Oracle.ConnectionString = "User Id=x;Password=y;Data Source=z";

        var provider = new OracleRefCursorDataProvider(
            new Microsoft.Extensions.Options.OptionsWrapper<ReportingOptions>(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OracleRefCursorDataProvider>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetDataAsync(new DataSetDef { Key = "detail" },
                new Dictionary<string, object?>(), 100, CancellationToken.None));

        Assert.Contains("no table id", exception.Message);
    }
}

/// <summary>
/// Minimal IDataReader over in-memory rows, so cursor reading and type mapping can be covered without a
/// database. Only the members the provider actually uses are implemented.
/// </summary>
internal sealed class FakeReader : IDataReader
{
    private readonly (string Name, Type Type)[] _columns;
    private readonly object?[][] _rows;
    private int _index = -1;

    private FakeReader((string, Type)[] columns, object?[][] rows)
    {
        _columns = columns;
        _rows = rows;
    }

    public static FakeReader With((string, Type)[] columns, object?[][] rows) => new(columns, rows);

    public int FieldCount => _columns.Length;
    public string GetName(int i) => _columns[i].Name;
    public Type GetFieldType(int i) => _columns[i].Type;
    public bool Read() => ++_index < _rows.Length;
    public object GetValue(int i) => _rows[_index][i]!;
    public bool IsDBNull(int i) => _rows[_index][i] is null or DBNull;

    public void Dispose() { }
    public void Close() { }
    public bool IsClosed => false;
    public int Depth => 0;
    public int RecordsAffected => 0;
    public bool NextResult() => false;
    public DataTable? GetSchemaTable() => null;

    public object this[int i] => GetValue(i);
    public object this[string name] => GetValue(GetOrdinal(name));
    public int GetOrdinal(string name) =>
        Array.FindIndex(_columns, c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool GetBoolean(int i) => (bool)GetValue(i);
    public byte GetByte(int i) => (byte)GetValue(i);
    public long GetBytes(int i, long o, byte[]? buffer, int bo, int length) => throw new NotSupportedException();
    public char GetChar(int i) => (char)GetValue(i);
    public long GetChars(int i, long o, char[]? buffer, int bo, int length) => throw new NotSupportedException();
    public IDataReader GetData(int i) => throw new NotSupportedException();
    public string GetDataTypeName(int i) => _columns[i].Type.Name;
    public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
    public decimal GetDecimal(int i) => (decimal)GetValue(i);
    public double GetDouble(int i) => (double)GetValue(i);
    public float GetFloat(int i) => (float)GetValue(i);
    public Guid GetGuid(int i) => (Guid)GetValue(i);
    public short GetInt16(int i) => (short)GetValue(i);
    public int GetInt32(int i) => (int)GetValue(i);
    public long GetInt64(int i) => (long)GetValue(i);
    public string GetString(int i) => (string)GetValue(i);
    public int GetValues(object?[] values) => throw new NotSupportedException();
}
