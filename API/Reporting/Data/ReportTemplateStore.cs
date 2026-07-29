#nullable enable
using API.Data;
using API.Reporting.Entities;
using API.Reporting.Model;
using API.Reporting.Runtime;
using Microsoft.EntityFrameworkCore;

namespace API.Reporting.Data
{
    /// <summary>Outcome of a save, so the controller can distinguish "invalid" from "conflict".</summary>
    public sealed class SaveTemplateResult
    {
        public ReportTemplate? Template { get; init; }
        public ValidationResult? Validation { get; init; }
        public string? Conflict { get; init; }

        public bool Succeeded => Template is not null;
    }

    /// <summary>
    /// Reads and writes stored templates.
    ///
    /// Every write validates the definition first, so a malformed template can never reach the database
    /// and therefore never reaches the renderer. Every write also snapshots the previous definition, which
    /// is what makes a bad edit in the designer recoverable.
    /// </summary>
    public sealed class ReportTemplateStore
    {
        private readonly StoreContext _context;
        private readonly ReportDefinitionValidator _validator;
        private readonly TimeProvider _time;

        public ReportTemplateStore(
            StoreContext context, ReportDefinitionValidator validator, TimeProvider? time = null)
        {
            _context = context;
            _validator = validator;
            _time = time ?? TimeProvider.System;
        }

        public Task<List<ReportTemplate>> ListAsync(
            bool includeInactive, int skip, int take, CancellationToken cancellationToken)
        {
            var query = _context.ReportTemplates.AsNoTracking();
            if (!includeInactive) query = query.Where(t => t.IsActive);

            return query
                .OrderBy(t => t.Name)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        public Task<int> CountAsync(bool includeInactive, CancellationToken cancellationToken) =>
            includeInactive
                ? _context.ReportTemplates.CountAsync(cancellationToken)
                : _context.ReportTemplates.CountAsync(t => t.IsActive, cancellationToken);

        public Task<ReportTemplate?> FindAsync(int id, CancellationToken cancellationToken) =>
            _context.ReportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        /// <summary>
        /// Resolves either a numeric id or a code. Integrators reference reports by code, and the run-time
        /// endpoints accept both so a call site can use whichever is stable for them.
        /// </summary>
        public async Task<ReportTemplate?> ResolveAsync(string idOrCode, CancellationToken cancellationToken)
        {
            if (int.TryParse(idOrCode, out var id))
            {
                var byId = await FindAsync(id, cancellationToken);
                if (byId is not null) return byId;
            }

            // EF cannot translate string.Equals with a comparer, so codes are normalised on write and
            // compared against the normalised form here.
            var code = Normalise(idOrCode);
            return await _context.ReportTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Code == code, cancellationToken);
        }

        public async Task<ReportDefinition?> ResolveDefinitionAsync(string idOrCode, CancellationToken cancellationToken)
        {
            var template = await ResolveAsync(idOrCode, cancellationToken);
            return template is null ? null : ReportJson.Deserialize(template.JsonDefinition);
        }

        public async Task<SaveTemplateResult> CreateAsync(
            string? code, string? name, string? description, ReportDefinition? definition,
            string? user, CancellationToken cancellationToken)
        {
            // A create with no definition yields a usable blank design rather than an empty row the
            // designer would then have to repair.
            definition ??= ReportDefinition.CreateEmpty(name ?? "New report");
            if (!string.IsNullOrWhiteSpace(name)) definition.Name = name!;

            var validation = _validator.Validate(definition);
            if (!validation.IsValid) return new SaveTemplateResult { Validation = validation };

            var normalised = Normalise(string.IsNullOrWhiteSpace(code) ? GenerateCode(definition.Name) : code!);

            if (await _context.ReportTemplates.AnyAsync(t => t.Code == normalised, cancellationToken))
                return new SaveTemplateResult { Conflict = $"A report with code \"{normalised}\" already exists." };

            var now = _time.GetUtcNow().UtcDateTime;
            var template = new ReportTemplate
            {
                Code = normalised,
                Name = definition.Name,
                Description = description ?? definition.Description,
                JsonDefinition = ReportJson.Serialize(definition),
                Version = 1,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = user,
                UpdatedAt = now,
                UpdatedBy = user
            };

            _context.ReportTemplates.Add(template);
            await _context.SaveChangesAsync(cancellationToken);

            return new SaveTemplateResult { Template = template, Validation = validation };
        }

        /// <summary>
        /// Saves a design. The previous definition is snapshotted before being overwritten, and the
        /// version is bumped so a caller can tell whether it is looking at stale content.
        /// </summary>
        public async Task<SaveTemplateResult> UpdateAsync(
            int id, string? name, string? description, ReportDefinition definition,
            int? expectedVersion, string? user, CancellationToken cancellationToken)
        {
            var template = await _context.ReportTemplates
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

            if (template is null) return new SaveTemplateResult();

            // Optimistic concurrency, and opt-in: a caller that passes the version it loaded is told when
            // someone else has saved in between rather than silently overwriting their work.
            if (expectedVersion is not null && expectedVersion != template.Version)
                return new SaveTemplateResult
                {
                    Conflict = $"This report has changed since you loaded it (you have version " +
                               $"{expectedVersion}, current is {template.Version}). Reload before saving."
                };

            if (!string.IsNullOrWhiteSpace(name)) definition.Name = name!;

            var validation = _validator.Validate(definition);
            if (!validation.IsValid) return new SaveTemplateResult { Validation = validation };

            var now = _time.GetUtcNow().UtcDateTime;

            _context.ReportTemplateVersions.Add(new ReportTemplateVersion
            {
                ReportTemplateId = template.Id,
                Version = template.Version,
                JsonDefinition = template.JsonDefinition,
                CreatedAt = now,
                CreatedBy = template.UpdatedBy
            });

            template.Name = definition.Name;
            template.Description = description ?? definition.Description;
            template.JsonDefinition = ReportJson.Serialize(definition);
            template.Version++;
            template.UpdatedAt = now;
            template.UpdatedBy = user;

            await _context.SaveChangesAsync(cancellationToken);

            return new SaveTemplateResult { Template = template, Validation = validation };
        }

        /// <summary>Soft delete, so an id already referenced elsewhere keeps resolving.</summary>
        public async Task<bool> DeactivateAsync(int id, string? user, CancellationToken cancellationToken)
        {
            var template = await _context.ReportTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (template is null) return false;

            template.IsActive = false;
            template.UpdatedAt = _time.GetUtcNow().UtcDateTime;
            template.UpdatedBy = user;

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<SaveTemplateResult> DuplicateAsync(
            int id, string? newCode, string? newName, string? user, CancellationToken cancellationToken)
        {
            var source = await FindAsync(id, cancellationToken);
            if (source is null) return new SaveTemplateResult();

            var definition = ReportJson.Deserialize(source.JsonDefinition);
            definition.Name = newName ?? $"{source.Name} (copy)";

            return await CreateAsync(newCode, definition.Name, source.Description, definition, user, cancellationToken);
        }

        public Task<List<ReportTemplateVersion>> ListVersionsAsync(int id, CancellationToken cancellationToken) =>
            _context.ReportTemplateVersions.AsNoTracking()
                .Where(v => v.ReportTemplateId == id)
                .OrderByDescending(v => v.Version)
                .ToListAsync(cancellationToken);

        public Task<ReportTemplateVersion?> FindVersionAsync(
            int id, int version, CancellationToken cancellationToken) =>
            _context.ReportTemplateVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.ReportTemplateId == id && v.Version == version, cancellationToken);

        /// <summary>
        /// Restores an old snapshot by saving it as a new version, rather than rewinding the version
        /// counter. History stays append-only, so the restore itself is recoverable too.
        /// </summary>
        public async Task<SaveTemplateResult> RestoreAsync(
            int id, int version, string? user, CancellationToken cancellationToken)
        {
            var snapshot = await FindVersionAsync(id, version, cancellationToken);
            if (snapshot is null) return new SaveTemplateResult();

            var definition = ReportJson.Deserialize(snapshot.JsonDefinition);
            return await UpdateAsync(id, null, null, definition, null, user, cancellationToken);
        }

        /// <summary>
        /// Codes are upper-cased and non-alphanumerics become underscores, so that "Invoice Summary"
        /// and "invoice-summary" cannot become two different codes for the same thing.
        /// </summary>
        public static string Normalise(string code)
        {
            var chars = code.Trim().Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
            return new string(chars.ToArray());
        }

        private static string GenerateCode(string name)
        {
            var basis = Normalise(name);
            if (basis.Length > 40) basis = basis[..40];
            return basis.Length == 0 ? $"REPORT_{Guid.NewGuid():N}"[..24] : basis;
        }
    }
}
