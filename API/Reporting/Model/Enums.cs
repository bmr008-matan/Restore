#nullable enable
using System.Text.Json.Serialization;

namespace API.Reporting.Model
{
    /// <summary>
    /// Every enum in the definition model is serialised by name rather than by ordinal, so that
    /// stored template JSON stays readable and stays valid when new members are inserted.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PaperSize>))]
    public enum PaperSize { A3, A4, A5, Letter, Legal, Custom }

    [JsonConverter(typeof(JsonStringEnumConverter<PageOrientation>))]
    public enum PageOrientation { Portrait, Landscape }

    [JsonConverter(typeof(JsonStringEnumConverter<TextDirection>))]
    public enum TextDirection { Ltr, Rtl }

    [JsonConverter(typeof(JsonStringEnumConverter<BandKind>))]
    public enum BandKind
    {
        /// <summary>Rendered once at the very start of the document.</summary>
        ReportHeader,

        /// <summary>Repeated at the top of every page via the Chromium header template.</summary>
        PageHeader,

        /// <summary>The body of the report. Holds the data tables.</summary>
        Detail,

        /// <summary>Repeated at the bottom of every page via the Chromium footer template.</summary>
        PageFooter,

        /// <summary>Rendered once at the very end of the document.</summary>
        ReportFooter
    }

    [JsonConverter(typeof(JsonStringEnumConverter<HorizontalAlign>))]
    public enum HorizontalAlign { Left, Center, Right, Justify, Start, End }

    [JsonConverter(typeof(JsonStringEnumConverter<VerticalAlign>))]
    public enum VerticalAlign { Top, Middle, Bottom }

    [JsonConverter(typeof(JsonStringEnumConverter<ParameterType>))]
    public enum ParameterType { Text, Int, Decimal, Date, DateRange, Bool, Select, MultiSelect }

    [JsonConverter(typeof(JsonStringEnumConverter<DataSourceKind>))]
    public enum DataSourceKind
    {
        /// <summary>Resolved by calling the Oracle reporting package with a predefined table id.</summary>
        OracleTableId,

        /// <summary>Rows are supplied inline by the caller in the generate request.</summary>
        PushedData,

        /// <summary>Local sample data over the SQLite store. Development and tests only.</summary>
        Sample
    }

    [JsonConverter(typeof(JsonStringEnumConverter<InputKind>))]
    public enum InputKind { Literal, Parameter, Expression }

    [JsonConverter(typeof(JsonStringEnumConverter<SortDirection>))]
    public enum SortDirection { Asc, Desc }

    [JsonConverter(typeof(JsonStringEnumConverter<FieldDataType>))]
    public enum FieldDataType { String, Int, Decimal, Date, Bool }

    [JsonConverter(typeof(JsonStringEnumConverter<AggregateFunction>))]
    public enum AggregateFunction { Sum, Avg, Min, Max, Count, CountDistinct, First, Last }

    [JsonConverter(typeof(JsonStringEnumConverter<LineOrientation>))]
    public enum LineOrientation { Horizontal, Vertical }

    [JsonConverter(typeof(JsonStringEnumConverter<ImageFit>))]
    public enum ImageFit { Contain, Cover, Fill, None }

    [JsonConverter(typeof(JsonStringEnumConverter<BorderStyle>))]
    public enum BorderStyle { None, Solid, Dashed, Dotted, Double }
}
