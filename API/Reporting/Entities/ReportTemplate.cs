#nullable enable
namespace API.Reporting.Entities
{
    /// <summary>
    /// A stored report design. This row is the single source of truth for a template: the designer
    /// serialises its in-memory definition to JSON and PUTs it here, and the returned <see cref="Id"/> is
    /// the handle every later call uses.
    ///
    /// The definition is a JSON column rather than a normalised set of tables. That is deliberate: the
    /// definition model gains element types, rule actions and aggregate functions over time, and none of
    /// those should need a schema migration. The trade-off is that the database cannot query inside a
    /// definition, which is fine — nothing needs to.
    /// </summary>
    public class ReportTemplate
    {
        public int Id { get; set; }

        /// <summary>
        /// Stable, human-usable identifier. Integrators reference reports by this rather than by id, so
        /// a call site reads "INVOICE_SUMMARY" instead of a brittle number.
        /// </summary>
        public string Code { get; set; } = "";

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        /// <summary>The serialised <see cref="Model.ReportDefinition"/>.</summary>
        public string JsonDefinition { get; set; } = "";

        /// <summary>Incremented on every save. Matches the latest <see cref="ReportTemplateVersion"/>.</summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Deletes are soft so that an id already wired into another system keeps resolving. An inactive
        /// template is hidden from the list but can still be fetched and restored.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }

        public List<ReportTemplateVersion> Versions { get; set; } = new();
    }

    /// <summary>
    /// A snapshot of a definition as it was before a save. Designing a report is iterative and a bad
    /// edit is easy to make, so every save keeps the previous state and the API can restore it.
    /// </summary>
    public class ReportTemplateVersion
    {
        public int Id { get; set; }

        public int ReportTemplateId { get; set; }
        public ReportTemplate? ReportTemplate { get; set; }

        public int Version { get; set; }

        public string JsonDefinition { get; set; } = "";

        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
    }
}
