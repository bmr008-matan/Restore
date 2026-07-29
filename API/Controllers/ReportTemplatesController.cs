#nullable enable
using API.Reporting.Data;
using API.Reporting.Dtos;
using API.Reporting.Entities;
using API.Reporting.Model;
using API.Reporting.Runtime;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    /// <summary>
    /// Design-time API. This is where a report design is saved: the designer serialises its definition
    /// and PUTs it here, and the id returned by POST is the handle every run-time call uses.
    /// </summary>
    [ApiController]
    [Route("api/report-templates")]
    public class ReportTemplatesController : ControllerBase
    {
        private readonly ReportTemplateStore _store;

        public ReportTemplatesController(ReportTemplateStore store) => _store = store;

        /// <summary>Identity of the caller. A single place to change once authentication is added.</summary>
        private string? CurrentUser => User?.Identity?.Name;

        [HttpGet]
        public async Task<ActionResult<PagedResult<ReportTemplateSummaryDto>>> List(
            [FromQuery] bool includeInactive = false,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50,
            CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 200);
            skip = Math.Max(0, skip);

            var templates = await _store.ListAsync(includeInactive, skip, take, cancellationToken);
            var total = await _store.CountAsync(includeInactive, cancellationToken);

            return Ok(new PagedResult<ReportTemplateSummaryDto>(
                templates.Select(Summary).ToList(), total, skip, take));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ReportTemplateDto>> Get(int id, CancellationToken cancellationToken)
        {
            var template = await _store.FindAsync(id, cancellationToken);
            if (template is null) return NotFound();

            if (!ReportJson.TryDeserialize(template.JsonDefinition, out var definition, out var error))
                return Problem(
                    title: "Stored definition could not be read",
                    detail: $"Report {template.Code} has a definition this server cannot parse: {error}",
                    statusCode: StatusCodes.Status500InternalServerError);

            return Ok(Detail(template, definition!, Array.Empty<ValidationMessageDto>()));
        }

        /// <summary>
        /// Creates a template and returns its id. A request without a definition yields a blank design
        /// with sane page setup, which is what the designer's "new report" does.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<CreatedTemplateDto>> Create(
            CreateTemplateRequest request, CancellationToken cancellationToken)
        {
            var result = await _store.CreateAsync(
                request.Code, request.Name, request.Description, request.Definition,
                CurrentUser, cancellationToken);

            if (result.Conflict is not null) return Conflict(new { message = result.Conflict });
            if (!result.Succeeded) return ValidationFailed(result.Validation!);

            var template = result.Template!;
            return CreatedAtAction(nameof(Get), new { id = template.Id },
                new CreatedTemplateDto(template.Id, template.Code, template.Name, template.Version));
        }

        /// <summary>
        /// Saves a design. Validates, snapshots the previous definition so a bad edit is recoverable, and
        /// bumps the version.
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<ActionResult<ReportTemplateDto>> Update(
            int id, UpdateTemplateRequest request, CancellationToken cancellationToken)
        {
            var result = await _store.UpdateAsync(
                id, request.Name, request.Description, request.Definition,
                request.ExpectedVersion, CurrentUser, cancellationToken);

            if (result.Conflict is not null) return Conflict(new { message = result.Conflict });
            if (result.Validation is { IsValid: false }) return ValidationFailed(result.Validation);
            if (!result.Succeeded) return NotFound();

            var template = result.Template!;
            var definition = ReportJson.Deserialize(template.JsonDefinition);

            // Warnings ride back on a successful save so the designer can surface them without the save
            // having failed — a stale field reference is worth showing, not worth blocking.
            return Ok(Detail(template, definition, Warnings(result.Validation)));
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
            await _store.DeactivateAsync(id, CurrentUser, cancellationToken) ? NoContent() : NotFound();

        [HttpPost("{id:int}/duplicate")]
        public async Task<ActionResult<CreatedTemplateDto>> Duplicate(
            int id, DuplicateTemplateRequest request, CancellationToken cancellationToken)
        {
            var result = await _store.DuplicateAsync(id, request.Code, request.Name, CurrentUser, cancellationToken);

            if (result.Conflict is not null) return Conflict(new { message = result.Conflict });
            if (result.Validation is { IsValid: false }) return ValidationFailed(result.Validation);
            if (!result.Succeeded) return NotFound();

            var template = result.Template!;
            return CreatedAtAction(nameof(Get), new { id = template.Id },
                new CreatedTemplateDto(template.Id, template.Code, template.Name, template.Version));
        }

        [HttpGet("{id:int}/versions")]
        public async Task<ActionResult<IReadOnlyList<TemplateVersionDto>>> Versions(
            int id, CancellationToken cancellationToken)
        {
            if (await _store.FindAsync(id, cancellationToken) is null) return NotFound();

            var versions = await _store.ListVersionsAsync(id, cancellationToken);
            return Ok(versions.Select(v => new TemplateVersionDto(v.Version, v.CreatedAt, v.CreatedBy)).ToList());
        }

        [HttpGet("{id:int}/versions/{version:int}")]
        public async Task<ActionResult<ReportDefinition>> Version(
            int id, int version, CancellationToken cancellationToken)
        {
            var snapshot = await _store.FindVersionAsync(id, version, cancellationToken);
            if (snapshot is null) return NotFound();

            return Ok(ReportJson.Deserialize(snapshot.JsonDefinition));
        }

        /// <summary>
        /// Restores a snapshot by saving it as a new version rather than rewinding the counter, so history
        /// stays append-only and the restore is itself recoverable.
        /// </summary>
        [HttpPost("{id:int}/restore/{version:int}")]
        public async Task<ActionResult<ReportTemplateDto>> Restore(
            int id, int version, CancellationToken cancellationToken)
        {
            var result = await _store.RestoreAsync(id, version, CurrentUser, cancellationToken);

            if (result.Conflict is not null) return Conflict(new { message = result.Conflict });
            if (result.Validation is { IsValid: false }) return ValidationFailed(result.Validation);
            if (!result.Succeeded) return NotFound();

            var template = result.Template!;
            return Ok(Detail(template, ReportJson.Deserialize(template.JsonDefinition), Warnings(result.Validation)));
        }

        /// <summary>
        /// Validates a definition without saving it, so the designer can show problems as they are made.
        /// </summary>
        [HttpPost("validate")]
        public ActionResult<ValidationProblemDto> Validate(
            [FromBody] ReportDefinition definition,
            [FromServices] ReportDefinitionValidator validator)
        {
            var result = validator.Validate(definition);

            return Ok(new ValidationProblemDto(
                result.IsValid ? "Valid" : "This report has problems that prevent saving.",
                result.Errors.Select(e => new ValidationMessageDto(e.Path, e.Message))
                    .Concat(result.Warnings.Select(w => new ValidationMessageDto(w.Path, $"Warning: {w.Message}")))
                    .ToList()));
        }

        private ActionResult ValidationFailed(ValidationResult validation) =>
            BadRequest(new ValidationProblemDto(
                "This report has problems that prevent saving.",
                validation.Errors.Select(e => new ValidationMessageDto(e.Path, e.Message)).ToList()));

        private static IReadOnlyList<ValidationMessageDto> Warnings(ValidationResult? validation) =>
            validation is null
                ? Array.Empty<ValidationMessageDto>()
                : validation.Warnings.Select(w => new ValidationMessageDto(w.Path, w.Message)).ToList();

        private static ReportTemplateSummaryDto Summary(ReportTemplate t) =>
            new(t.Id, t.Code, t.Name, t.Description, t.Version, t.IsActive, t.UpdatedAt, t.UpdatedBy);

        private static ReportTemplateDto Detail(
            ReportTemplate t, ReportDefinition definition, IReadOnlyList<ValidationMessageDto> warnings) =>
            new(t.Id, t.Code, t.Name, t.Description, t.Version, t.IsActive,
                t.CreatedAt, t.UpdatedAt, t.UpdatedBy, definition, warnings);
    }
}
