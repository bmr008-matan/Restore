#nullable enable
using API.Data;
using API.Reporting.Entities;
using API.Reporting.Model;
using Microsoft.EntityFrameworkCore;

namespace API.Reporting.Data
{
    /// <summary>
    /// Seeds the table-id catalogue and the demo templates.
    ///
    /// Both are idempotent and additive: an existing row is left alone rather than overwritten, so a
    /// restart never discards a design someone has edited. Only genuinely missing rows are inserted.
    /// </summary>
    public static class ReportingSeeder
    {
        public static async Task SeedAsync(
            StoreContext context, ILogger logger, CancellationToken cancellationToken = default)
        {
            await SeedTableSourcesAsync(context, logger, cancellationToken);
            await SeedTemplatesAsync(context, logger, cancellationToken);
        }

        private static async Task SeedTableSourcesAsync(
            StoreContext context, ILogger logger, CancellationToken cancellationToken)
        {
            var existing = await context.ReportTableSources
                .Select(s => s.TableId)
                .ToListAsync(cancellationToken);

            var missing = BuildCatalog().Where(s => !existing.Contains(s.TableId)).ToList();
            if (missing.Count == 0) return;

            context.ReportTableSources.AddRange(missing);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Seeded {Count} report table source(s).", missing.Count);
        }

        private static async Task SeedTemplatesAsync(
            StoreContext context, ILogger logger, CancellationToken cancellationToken)
        {
            var existing = await context.ReportTemplates
                .Select(t => t.Code)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var added = 0;

            foreach (var (code, definition) in SampleReports.All())
            {
                var normalised = ReportTemplateStore.Normalise(code);
                if (existing.Contains(normalised)) continue;

                context.ReportTemplates.Add(new ReportTemplate
                {
                    Code = normalised,
                    Name = definition.Name,
                    Description = definition.Description,
                    JsonDefinition = ReportJson.Serialize(definition),
                    Version = 1,
                    IsActive = true,
                    CreatedAt = now,
                    CreatedBy = "seed",
                    UpdatedAt = now,
                    UpdatedBy = "seed"
                });
                added++;
            }

            if (added == 0) return;

            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} demo report template(s).", added);
        }

        /// <summary>
        /// The catalogue. In a real deployment these describe the table ids the Oracle reporting package
        /// understands; the three seeded here are backed by the local sample provider so the designer is
        /// usable with no Oracle instance.
        /// </summary>
        private static List<ReportTableSource> BuildCatalog() => new()
        {
            new ReportTableSource
            {
                TableId = SampleDataProvider.ProductsTableId,
                Name = "Product stock",
                Description = "Catalogue items with price, quantity and computed stock value. " +
                              "Local sample data, for development and demos.",
                SourceKind = DataSourceKind.Sample,
                FieldsJson = ReportTableSourceCatalog.Serialise(SampleDataProvider.ProductFields),
                InputsJson = ReportTableSourceCatalog.Serialise(new[]
                {
                    new TableSourceInput { Name = "brand", Label = "Brand", DataType = FieldDataType.String },
                    new TableSourceInput { Name = "type", Label = "Type", DataType = FieldDataType.String },
                    new TableSourceInput { Name = "minPrice", Label = "Minimum price", DataType = FieldDataType.Decimal },
                    new TableSourceInput { Name = "inStockOnly", Label = "In stock only", DataType = FieldDataType.Bool }
                })
            },
            new ReportTableSource
            {
                TableId = SampleDataProvider.BrandsTableId,
                Name = "Brands (lookup)",
                Description = "Distinct brands. Intended as the lookup behind a Select parameter.",
                SourceKind = DataSourceKind.Sample,
                FieldsJson = ReportTableSourceCatalog.Serialise(SampleDataProvider.LookupFields),
                InputsJson = "[]"
            },
            new ReportTableSource
            {
                TableId = SampleDataProvider.TypesTableId,
                Name = "Types (lookup)",
                Description = "Distinct product types. Intended as the lookup behind a Select parameter.",
                SourceKind = DataSourceKind.Sample,
                FieldsJson = ReportTableSourceCatalog.Serialise(SampleDataProvider.LookupFields),
                InputsJson = "[]"
            }
        };
    }
}
