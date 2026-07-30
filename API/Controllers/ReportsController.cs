#nullable enable
using System.Text.Json;
using API.Reporting.Data;
using API.Reporting.Dtos;
using API.Reporting.Model;
using API.Reporting.Runtime;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    /// <summary>
    /// Run-time API, used by the UI and by other systems alike.
    ///
    /// The flow is: discover what to send with GET {idOrCode}/parameters, then POST
    /// {idOrCode}/generate with parameters, or with the data itself, or both.
    /// </summary>
    [ApiController]
    [Route("api/reports")]
    public class ReportsController : ControllerBase
    {
        private readonly ReportTemplateStore _store;
        private readonly ReportRuntime _runtime;
        private readonly ReportTableSourceCatalog _catalog;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(
            ReportTemplateStore store,
            ReportRuntime runtime,
            ReportTableSourceCatalog catalog,
            ILogger<ReportsController> logger)
        {
            _store = store;
            _runtime = runtime;
            _catalog = catalog;
            _logger = logger;
        }

        private string? CurrentUser => User?.Identity?.Name;

        private IReadOnlyList<string> CurrentRoles =>
            User?.Claims
                .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList() as IReadOnlyList<string> ?? Array.Empty<string>();

        /// <summary>
        /// Parameter metadata for a report, including the resolved options of any Select. This is what
        /// lets an integrator discover the contract without reading the template.
        /// </summary>
        [HttpGet("{idOrCode}/parameters")]
        public async Task<ActionResult<ReportParametersDto>> Parameters(
            string idOrCode, CancellationToken cancellationToken)
        {
            var template = await _store.ResolveAsync(idOrCode, cancellationToken);
            if (template is null) return NotFound();

            var definition = ReportJson.Deserialize(template.JsonDefinition);
            var parameters = new List<ReportParameterDto>();

            foreach (var parameter in definition.Parameters.OrderBy(p => p.Order))
            {
                var options = await LookupOptionsAsync(parameter, cancellationToken);
                parameters.Add(new ReportParameterDto(
                    parameter.Name,
                    parameter.EffectiveLabel,
                    parameter.Type,
                    parameter.Required,
                    parameter.DefaultValue,
                    parameter.Hidden,
                    parameter.Order,
                    parameter.LookupTableId,
                    options));
            }

            return Ok(new ReportParametersDto(template.Id, template.Code, template.Name, parameters));
        }

        /// <summary>
        /// Generates a PDF from a saved template.
        ///
        /// Parameters are validated before any data source is touched, so a bad request costs neither a
        /// query nor a browser page. Any data set key present in the request body is served from that data
        /// instead of its bound source, which is what lets one template run either mode.
        /// </summary>
        [HttpPost("{idOrCode}/generate")]
        // Declared explicitly because the action returns IActionResult, which tells Swashbuckle nothing.
        // The content type goes on each response rather than on a [Produces] attribute: [Produces]
        // applies to every response, which would document the JSON error bodies as application/pdf.
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
        [ProducesResponseType(typeof(ValidationProblemDto), StatusCodes.Status400BadRequest, "application/json")]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
        public async Task<IActionResult> Generate(
            string idOrCode, GenerateReportRequest request, CancellationToken cancellationToken)
        {
            var template = await _store.ResolveAsync(idOrCode, cancellationToken);
            if (template is null) return NotFound();

            var definition = ReportJson.Deserialize(template.JsonDefinition);

            return await RenderAsync(definition, request.Parameters, request.Data, request.Options,
                template.Code, asJson: false, cancellationToken);
        }

        /// <summary>
        /// The same render, wrapped in a JSON envelope. PL/SQL and older integration callers handle a
        /// base64 string far more easily than a binary stream.
        /// </summary>
        [HttpPost("{idOrCode}/generate/base64")]
        [ProducesResponseType(typeof(GeneratedReportDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GenerateBase64(
            string idOrCode, GenerateReportRequest request, CancellationToken cancellationToken)
        {
            var template = await _store.ResolveAsync(idOrCode, cancellationToken);
            if (template is null) return NotFound();

            var definition = ReportJson.Deserialize(template.JsonDefinition);

            return await RenderAsync(definition, request.Parameters, request.Data, request.Options,
                template.Code, asJson: true, cancellationToken);
        }

        /// <summary>
        /// Renders an unsaved definition. This is the designer's live preview, and it never persists
        /// anything — which is what keeps the preview and the final PDF the same render.
        /// </summary>
        [HttpPost("preview")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
        [ProducesResponseType(typeof(ValidationProblemDto), StatusCodes.Status400BadRequest, "application/json")]
        public Task<IActionResult> Preview(PreviewReportRequest request, CancellationToken cancellationToken) =>
            RenderAsync(request.Definition, request.Parameters, request.Data, request.Options,
                "preview", asJson: false, cancellationToken);

        private async Task<IActionResult> RenderAsync(
            ReportDefinition definition,
            Dictionary<string, JsonElement>? parameters,
            Dictionary<string, JsonElement>? data,
            GenerateOptionsDto? options,
            string codeForFileName,
            bool asJson,
            CancellationToken cancellationToken)
        {
            var pushed = PushedDataProvider.Payload.FromJson(data, out var dataErrors);
            if (dataErrors.Count > 0)
                return BadRequest(new ValidationProblemDto(
                    "The supplied data could not be read.",
                    dataErrors.Select(e => new ValidationMessageDto("data", e)).ToList()));

            var request = new ReportRequest
            {
                Parameters = parameters?.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.OrdinalIgnoreCase),
                PushedData = pushed,
                Culture = options?.Culture,
                Direction = options?.Direction,
                UserName = CurrentUser,
                UserRoles = CurrentRoles
            };

            ReportRenderResult result;
            try
            {
                result = await _runtime.RenderAsync(definition, request, cancellationToken);
            }
            catch (ReportRequestException ex)
            {
                // Everything the caller can fix — a missing required parameter, a bad type, an unknown
                // data set key — arrives here as a 400 listing every problem at once.
                return BadRequest(new ValidationProblemDto(
                    ex.Message,
                    ex.Errors.Select(e => new ValidationMessageDto(e.Parameter, e.Message)).ToList()));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The client went away; nothing to report.
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Report {Code} exceeded its time budget.", codeForFileName);
                return StatusCode(StatusCodes.Status504GatewayTimeout, new
                {
                    message = "The report took too long to generate. Narrow the parameters, or raise " +
                              "Reporting:Limits:TotalTimeoutSeconds."
                });
            }

            var fileName = SafeFileName(options?.FileName ?? $"{codeForFileName}.pdf");
            var diagnostics = result.Diagnostics
                .Select(d => new ValidationMessageDto(d.Path, d.Message))
                .ToList();

            if (asJson)
                return Ok(new GeneratedReportDto(
                    fileName, "application/pdf", result.PageCount, result.ElapsedMs,
                    Convert.ToBase64String(result.Pdf), diagnostics));

            // Headers callers actually use: page count for pagination UI, duration for logging, and a
            // truncation flag so a clipped report is visible rather than silently short.
            Response.Headers["X-Report-Page-Count"] = result.PageCount.ToString();
            Response.Headers["X-Report-Duration-Ms"] = result.ElapsedMs.ToString();
            if (result.AnyDataTruncated) Response.Headers["X-Report-Truncated"] = "true";
            if (diagnostics.Count > 0) Response.Headers["X-Report-Diagnostics"] = diagnostics.Count.ToString();

            return options?.Inline == true
                ? File(result.Pdf, "application/pdf")
                : File(result.Pdf, "application/pdf", fileName);
        }

        private async Task<List<LookupOptionDto>?> LookupOptionsAsync(
            ReportParameter parameter, CancellationToken cancellationToken)
        {
            if (parameter.LookupTableId is null) return null;

            var preview = await _catalog.PreviewAsync(parameter.LookupTableId.Value, null, cancellationToken);
            if (preview is null) return null;

            var valueField = parameter.ValueField ?? "value";
            var displayField = parameter.DisplayField ?? valueField;

            return preview.Rows
                .Select(row => new LookupOptionDto(
                    row[valueField]?.ToString() ?? "",
                    row[displayField]?.ToString() ?? row[valueField]?.ToString() ?? ""))
                .Where(o => o.Value.Length > 0)
                .ToList();
        }

        /// <summary>
        /// Reduces a caller-supplied name to something safe to put in a Content-Disposition header: no
        /// path separators, no control characters that could inject a header, and no dot runs that would
        /// leave a traversal-looking name like "..\..\etc". Falls back to a sensible default when nothing
        /// usable is left.
        /// </summary>
        private static string SafeFileName(string requested)
        {
            // Take the last segment first, so a caller sending a path contributes only its file name.
            var lastSeparator = requested.LastIndexOfAny(new[] { '/', '\\' });
            var candidate = lastSeparator >= 0 ? requested[(lastSeparator + 1)..] : requested;

            var kept = new string(candidate
                .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ')
                .ToArray());

            // Collapse dot runs, then trim leading and trailing dots and spaces.
            while (kept.Contains("..", StringComparison.Ordinal))
                kept = kept.Replace("..", ".", StringComparison.Ordinal);

            kept = kept.Trim(' ', '.');

            if (kept.Length == 0) kept = "report";
            if (!kept.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) kept += ".pdf";

            return kept;
        }
    }
}
