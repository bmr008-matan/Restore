#nullable enable
using System.Text.Json;
using API.Reporting.Data;
using API.Reporting.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    /// <summary>
    /// The predefined table-id catalogue, and a bounded data preview over it.
    ///
    /// This is what the designer's data panel reads: it never sees or sends a query, only picks an id from
    /// this list. When a query builder is added later it will register entries in the same catalogue and
    /// the designer will not need to change.
    /// </summary>
    [ApiController]
    [Route("api/report-sources")]
    public class ReportSourcesController : ControllerBase
    {
        private readonly ReportTableSourceCatalog _catalog;

        public ReportSourcesController(ReportTableSourceCatalog catalog) => _catalog = catalog;

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<TableSourceDto>>> List(CancellationToken cancellationToken)
        {
            var sources = await _catalog.ListAsync(cancellationToken);

            return Ok(sources.Select(s => new TableSourceDto(
                s.TableId, s.Name, s.Description, s.SourceKind, s.Fields, s.Inputs)).ToList());
        }

        /// <summary>Field metadata for one source, for the designer's field picker.</summary>
        [HttpGet("{tableId:int}/fields")]
        public async Task<ActionResult<TableSourceDto>> Fields(int tableId, CancellationToken cancellationToken)
        {
            var source = await _catalog.FindAsync(tableId, cancellationToken);
            if (source is null) return NotFound();

            return Ok(new TableSourceDto(
                source.TableId, source.Name, source.Description, source.SourceKind, source.Fields, source.Inputs));
        }

        /// <summary>
        /// A few real rows, so the designer can show sample values beside field names. Row count is capped
        /// by configuration: this is a design aid, not a data export.
        /// </summary>
        [HttpPost("{tableId:int}/sample")]
        public async Task<ActionResult<DataPreviewDto>> Sample(
            int tableId,
            [FromBody] Dictionary<string, JsonElement>? inputs,
            CancellationToken cancellationToken)
        {
            var converted = inputs?.ToDictionary(
                i => i.Key,
                i => (object?)Unwrap(i.Value),
                StringComparer.OrdinalIgnoreCase);

            var preview = await _catalog.PreviewAsync(tableId, converted, cancellationToken);
            if (preview is null) return NotFound();

            return Ok(new DataPreviewDto(
                preview.Fields,
                preview.Rows.Select(r => new Dictionary<string, object?>(r.Values)).ToList(),
                preview.Truncated));
        }

        /// <summary>Flattens JSON into plain scalars, so providers never see a JsonElement.</summary>
        private static object? Unwrap(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
