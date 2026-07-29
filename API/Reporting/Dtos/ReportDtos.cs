#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Reporting.Model;

namespace API.Reporting.Dtos
{
    /// <summary>List row. Deliberately excludes the definition, which can be tens of kilobytes.</summary>
    public sealed record ReportTemplateSummaryDto(
        int Id,
        string Code,
        string Name,
        string? Description,
        int Version,
        bool IsActive,
        DateTime UpdatedAt,
        string? UpdatedBy);

    public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

    /// <summary>A template with its definition, for the designer to load.</summary>
    public sealed record ReportTemplateDto(
        int Id,
        string Code,
        string Name,
        string? Description,
        int Version,
        bool IsActive,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        string? UpdatedBy,
        ReportDefinition Definition,
        IReadOnlyList<ValidationMessageDto> Warnings);

    /// <summary>What POST returns: the id every later call uses.</summary>
    public sealed record CreatedTemplateDto(int Id, string Code, string Name, int Version);

    public sealed record ValidationMessageDto(string Path, string Message);

    public sealed record TemplateVersionDto(int Version, DateTime CreatedAt, string? CreatedBy);

    public sealed class CreateTemplateRequest
    {
        /// <summary>Optional. Derived from the name when omitted.</summary>
        public string? Code { get; set; }

        public string? Name { get; set; }
        public string? Description { get; set; }

        /// <summary>Omit to get a blank design with sane page setup rather than an unusable row.</summary>
        public ReportDefinition? Definition { get; set; }
    }

    public sealed class UpdateTemplateRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }

        public ReportDefinition Definition { get; set; } = new();

        /// <summary>
        /// The version the caller loaded. Supplying it turns the save into an optimistic-concurrency
        /// check, so two people editing the same report are told about it instead of one silently losing
        /// their work.
        /// </summary>
        public int? ExpectedVersion { get; set; }
    }

    public sealed class DuplicateTemplateRequest
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>
    /// The generate request. One shape covers both modes: supply <see cref="Parameters"/> and let the
    /// server fetch, or supply <see cref="Data"/> and skip the database, or both for a mix.
    /// </summary>
    public sealed class GenerateReportRequest
    {
        /// <summary>
        /// Parameter values, validated against what the template declares before any data is fetched.
        /// Left as JsonElement so the binder can coerce each one to its declared type.
        /// </summary>
        public Dictionary<string, JsonElement>? Parameters { get; set; }

        /// <summary>
        /// Rows supplied inline, keyed by data set. Any key present here overrides that data set's bound
        /// source entirely — no Oracle call is made for it.
        /// </summary>
        public Dictionary<string, JsonElement>? Data { get; set; }

        public GenerateOptionsDto? Options { get; set; }
    }

    public sealed class GenerateOptionsDto
    {
        /// <summary>Overrides the report's culture for this run, so one template serves two languages.</summary>
        public string? Culture { get; set; }

        public TextDirection? Direction { get; set; }

        /// <summary>Render inline in a viewer rather than as a download.</summary>
        public bool Inline { get; set; }

        public string? FileName { get; set; }
    }

    /// <summary>Preview request: an unsaved definition rendered without being stored.</summary>
    public sealed class PreviewReportRequest
    {
        public ReportDefinition Definition { get; set; } = new();
        public Dictionary<string, JsonElement>? Parameters { get; set; }
        public Dictionary<string, JsonElement>? Data { get; set; }
        public GenerateOptionsDto? Options { get; set; }
    }

    /// <summary>
    /// JSON envelope form of a generated report. Oracle and PL/SQL callers, and older integration tools,
    /// handle this far more easily than a binary stream.
    /// </summary>
    public sealed record GeneratedReportDto(
        string FileName,
        string ContentType,
        int PageCount,
        long ElapsedMs,
        string Base64,
        IReadOnlyList<ValidationMessageDto> Diagnostics);

    /// <summary>Parameter metadata, so a caller can discover what to send.</summary>
    public sealed record ReportParameterDto(
        string Name,
        string Label,
        ParameterType Type,
        bool Required,
        string? DefaultValue,
        bool Hidden,
        int Order,
        int? LookupTableId,
        IReadOnlyList<LookupOptionDto>? Options);

    public sealed record LookupOptionDto(string Value, string Label);

    public sealed record ReportParametersDto(
        int Id,
        string Code,
        string Name,
        IReadOnlyList<ReportParameterDto> Parameters);

    /// <summary>A catalogue entry, for the designer's data panel.</summary>
    public sealed record TableSourceDto(
        int TableId,
        string Name,
        string? Description,
        DataSourceKind SourceKind,
        IReadOnlyList<FieldMeta> Fields,
        IReadOnlyList<Entities.TableSourceInput> Inputs);

    /// <summary>Rows and columns from a data preview, shown next to field names in the designer.</summary>
    public sealed record DataPreviewDto(
        IReadOnlyList<FieldMeta> Fields,
        IReadOnlyList<Dictionary<string, object?>> Rows,
        bool Truncated);

    /// <summary>Validation response body, used for 400s from save and generate.</summary>
    public sealed record ValidationProblemDto(
        string Message,
        IReadOnlyList<ValidationMessageDto> Errors)
    {
        [JsonIgnore]
        public bool HasErrors => Errors.Count > 0;
    }
}
