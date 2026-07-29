using API.Entities;
using API.Reporting.Entities;
using Microsoft.EntityFrameworkCore;

namespace API.Data
{
    public class StoreContext : DbContext
    {
        public StoreContext(DbContextOptions options) : base(options)
        {

        }

        public DbSet<Product> Products { get; set; }

        public DbSet<ReportTemplate> ReportTemplates { get; set; }
        public DbSet<ReportTemplateVersion> ReportTemplateVersions { get; set; }
        public DbSet<ReportTableSource> ReportTableSources { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Explicit lengths throughout the reporting tables. SQLite ignores them, but Oracle does not:
            // an unbounded string maps to NVARCHAR2(2000) or worse, and an index over one fails outright.
            // Keeping the mapping provider-neutral is what lets the same migrations target both.
            builder.Entity<ReportTemplate>(entity =>
            {
                entity.Property(t => t.Code).HasMaxLength(60).IsRequired();
                entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
                entity.Property(t => t.Description).HasMaxLength(1000);
                entity.Property(t => t.CreatedBy).HasMaxLength(120);
                entity.Property(t => t.UpdatedBy).HasMaxLength(120);

                // The definition is unbounded on purpose: it is a document, and a report with many
                // columns and rules can run to tens of kilobytes.
                entity.Property(t => t.JsonDefinition).IsRequired();

                entity.HasIndex(t => t.Code).IsUnique();

                entity.HasMany(t => t.Versions)
                      .WithOne(v => v.ReportTemplate)
                      .HasForeignKey(v => v.ReportTemplateId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ReportTemplateVersion>(entity =>
            {
                entity.Property(v => v.JsonDefinition).IsRequired();
                entity.Property(v => v.CreatedBy).HasMaxLength(120);

                // One row per template per version, which also makes the history lookup an index seek.
                entity.HasIndex(v => new { v.ReportTemplateId, v.Version }).IsUnique();
            });

            builder.Entity<ReportTableSource>(entity =>
            {
                // Declared explicitly: "TableId" matches neither of EF's key conventions ("Id" or
                // "ReportTableSourceId"), so without this the entity has no primary key.
                entity.HasKey(s => s.TableId);

                // Table ids are assigned by whoever owns the reporting package, not generated here.
                entity.Property(s => s.TableId).ValueGeneratedNever();

                entity.Property(s => s.Name).HasMaxLength(200).IsRequired();
                entity.Property(s => s.Description).HasMaxLength(1000);
                entity.Property(s => s.FieldsJson).IsRequired();
                entity.Property(s => s.InputsJson).IsRequired();

                // Stored as text rather than an ordinal so the column stays readable and a reordered
                // enum cannot silently repoint every row at a different provider.
                entity.Property(s => s.SourceKind).HasConversion<string>().HasMaxLength(40);
            });
        }
    }
}
