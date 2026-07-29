#nullable enable
using System.Text.Json;
using API.Data;
using API.Reporting.Configuration;
using API.Reporting.Entities;
using API.Reporting.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace API.Reporting.Data
{
    /// <summary>A catalogue entry with its metadata already deserialised, ready for the designer.</summary>
    public sealed record TableSourceInfo(
        int TableId,
        string Name,
        string? Description,
        DataSourceKind SourceKind,
        List<FieldMeta> Fields,
        List<TableSourceInput> Inputs);

    /// <summary>
    /// Reads the predefined table-id catalogue and can preview rows from a source.
    ///
    /// This is the seam the future query builder plugs into: it will add entries to the same catalogue,
    /// and the designer will not have to change to see them.
    /// </summary>
    public sealed class ReportTableSourceCatalog
    {
        private readonly StoreContext _context;
        private readonly IServiceProvider _services;
        private readonly ReportingOptions _options;

        public ReportTableSourceCatalog(
            StoreContext context, IServiceProvider services, IOptions<ReportingOptions> options)
        {
            _context = context;
            _services = services;
            _options = options.Value;
        }

        public async Task<List<TableSourceInfo>> ListAsync(CancellationToken cancellationToken)
        {
            var sources = await _context.ReportTableSources.AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync(cancellationToken);

            return sources.Select(Map).ToList();
        }

        public async Task<TableSourceInfo?> FindAsync(int tableId, CancellationToken cancellationToken)
        {
            var source = await _context.ReportTableSources.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TableId == tableId, cancellationToken);

            return source is null ? null : Map(source);
        }

        /// <summary>
        /// Fetches a handful of rows so the designer can show real values next to field names. Bounded by
        /// the configured preview count — this is a design aid, not a data export.
        /// </summary>
        public async Task<ReportDataSet?> PreviewAsync(
            int tableId,
            IReadOnlyDictionary<string, object?>? inputs,
            CancellationToken cancellationToken)
        {
            var source = await FindAsync(tableId, cancellationToken);
            if (source is null) return null;

            var provider = _services.GetServices<IReportDataProvider>()
                .FirstOrDefault(p => p.Kind == source.SourceKind);

            if (provider is null) return null;

            var dataSet = new DataSetDef
            {
                Key = "preview",
                SourceKind = source.SourceKind,
                TableId = tableId,
                Fields = source.Fields
            };

            return await provider.GetDataAsync(
                dataSet,
                inputs ?? new Dictionary<string, object?>(),
                _options.Limits.PreviewRowCount,
                cancellationToken);
        }

        private static TableSourceInfo Map(ReportTableSource source) => new(
            source.TableId,
            source.Name,
            source.Description,
            source.SourceKind,
            Deserialise<FieldMeta>(source.FieldsJson),
            Deserialise<TableSourceInput>(source.InputsJson));

        /// <summary>
        /// Catalogue metadata is data, and a hand-edited row should not take an endpoint down, so a
        /// malformed payload degrades to an empty list rather than throwing.
        /// </summary>
        private static List<T> Deserialise<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<T>();

            try
            {
                return JsonSerializer.Deserialize<List<T>>(json, ReportJson.Options) ?? new List<T>();
            }
            catch (JsonException)
            {
                return new List<T>();
            }
        }

        /// <summary>Serialises metadata for storage, using the same options as everything else.</summary>
        public static string Serialise<T>(IEnumerable<T> items) =>
            JsonSerializer.Serialize(items, ReportJson.Options);
    }
}
