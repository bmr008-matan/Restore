#nullable enable
using API.Data;
using API.Reporting.Configuration;
using API.Reporting.Model;
using Microsoft.EntityFrameworkCore;

namespace API.Reporting.Data
{
    /// <summary>
    /// Serves report data from the local product catalogue over EF Core.
    ///
    /// This exists so the whole system — designer, preview, generate, tests — is runnable and verifiable
    /// with no Oracle instance anywhere in sight. It implements exactly the same interface as the Oracle
    /// provider, so the code paths under test are the real ones rather than a mock.
    ///
    /// Table ids match the seeded catalogue in Reporting/tableSources.json.
    /// </summary>
    public sealed class SampleDataProvider : IReportDataProvider
    {
        /// <summary>Product rows, one per catalogue item, shaped like a sales line.</summary>
        public const int ProductsTableId = 1001;

        /// <summary>Distinct brands, intended as a lookup for a Select parameter.</summary>
        public const int BrandsTableId = 1002;

        /// <summary>Distinct types, intended as a lookup for a Select parameter.</summary>
        public const int TypesTableId = 1003;

        private readonly StoreContext _context;
        private readonly ILogger<SampleDataProvider> _logger;

        public SampleDataProvider(StoreContext context, ILogger<SampleDataProvider> logger)
        {
            _context = context;
            _logger = logger;
        }

        public DataSourceKind Kind => DataSourceKind.Sample;

        public static IReadOnlyList<FieldMeta> ProductFields { get; } = new List<FieldMeta>
        {
            new() { Name = "id", Caption = "Id", DataType = FieldDataType.Int },
            new() { Name = "name", Caption = "Product", DataType = FieldDataType.String },
            new() { Name = "brand", Caption = "Brand", DataType = FieldDataType.String },
            new() { Name = "type", Caption = "Type", DataType = FieldDataType.String },
            new() { Name = "price", Caption = "Price", DataType = FieldDataType.Decimal, Format = "#,##0.00" },
            new() { Name = "quantityInStock", Caption = "In stock", DataType = FieldDataType.Int },
            new() { Name = "stockValue", Caption = "Stock value", DataType = FieldDataType.Decimal, Format = "#,##0.00" }
        };

        public static IReadOnlyList<FieldMeta> LookupFields { get; } = new List<FieldMeta>
        {
            new() { Name = "value", Caption = "Value", DataType = FieldDataType.String },
            new() { Name = "label", Caption = "Label", DataType = FieldDataType.String }
        };

        public async Task<ReportDataSet> GetDataAsync(
            DataSetDef dataSet,
            IReadOnlyDictionary<string, object?> inputs,
            int maxRows,
            CancellationToken cancellationToken)
        {
            return dataSet.TableId switch
            {
                BrandsTableId => await LookupAsync(dataSet.Key, p => p.Brand, maxRows, cancellationToken),
                TypesTableId => await LookupAsync(dataSet.Key, p => p.Type, maxRows, cancellationToken),
                _ => await ProductsAsync(dataSet, inputs, maxRows, cancellationToken)
            };
        }

        private async Task<ReportDataSet> ProductsAsync(
            DataSetDef dataSet,
            IReadOnlyDictionary<string, object?> inputs,
            int maxRows,
            CancellationToken cancellationToken)
        {
            var query = _context.Products.AsNoTracking();

            // Inputs are applied as real query filters rather than being ignored, so that exercising the
            // sample provider genuinely exercises input binding end to end.
            if (TryGetString(inputs, "brand", out var brand))
                query = query.Where(p => p.Brand == brand);

            if (TryGetString(inputs, "type", out var type))
                query = query.Where(p => p.Type == type);

            if (TryGetDecimal(inputs, "minPrice", out var minPrice))
            {
                // Prices are stored in minor units (the catalogue seeds 20000 for 200.00).
                var minor = (long)(minPrice * 100m);
                query = query.Where(p => p.Price >= minor);
            }

            if (TryGetBool(inputs, "inStockOnly", out var inStockOnly) && inStockOnly)
                query = query.Where(p => p.QuantityInStock > 0);

            // One row over the cap, so truncation is detected without a second count query.
            var products = await query
                .OrderBy(p => p.Brand).ThenBy(p => p.Type).ThenBy(p => p.Name)
                .Take(maxRows + 1)
                .ToListAsync(cancellationToken);

            var truncated = products.Count > maxRows;
            if (truncated)
            {
                products.RemoveAt(products.Count - 1);
                _logger.LogWarning("Data set {Key} hit the {MaxRows} row cap.", dataSet.Key, maxRows);
            }

            var rows = products.Select(p =>
            {
                var price = p.Price / 100m;
                return new ReportRow(new Dictionary<string, object?>
                {
                    ["id"] = (long)p.Id,
                    ["name"] = p.Name,
                    ["brand"] = p.Brand,
                    ["type"] = p.Type,
                    ["price"] = price,
                    ["quantityInStock"] = (long)p.QuantityInStock,
                    ["stockValue"] = price * p.QuantityInStock
                });
            }).ToList();

            return new ReportDataSet
            {
                Key = dataSet.Key,
                Fields = ProductFields.ToList(),
                Rows = rows,
                Truncated = truncated
            };
        }

        private async Task<ReportDataSet> LookupAsync(
            string key,
            // Fully qualified: API.Reporting.Entities also exists, so a relative "Entities.Product"
            // resolves to the wrong namespace from here.
            System.Linq.Expressions.Expression<Func<API.Entities.Product, string>> selector,
            int maxRows,
            CancellationToken cancellationToken)
        {
            var values = await _context.Products.AsNoTracking()
                .Select(selector)
                .Distinct()
                .OrderBy(v => v)
                .Take(maxRows)
                .ToListAsync(cancellationToken);

            return new ReportDataSet
            {
                Key = key,
                Fields = LookupFields.ToList(),
                Rows = values.Select(v => new ReportRow(new Dictionary<string, object?>
                {
                    ["value"] = v,
                    ["label"] = v
                })).ToList()
            };
        }

        private static bool TryGetString(
            IReadOnlyDictionary<string, object?> inputs, string name, out string value)
        {
            value = "";
            if (!inputs.TryGetValue(name, out var raw) || raw is null) return false;

            value = raw.ToString() ?? "";
            return value.Length > 0;
        }

        private static bool TryGetDecimal(
            IReadOnlyDictionary<string, object?> inputs, string name, out decimal value)
        {
            value = 0m;
            if (!inputs.TryGetValue(name, out var raw)) return false;

            var parsed = Runtime.ValueFormatter.ToDecimal(raw);
            if (parsed is null) return false;

            value = parsed.Value;
            return true;
        }

        private static bool TryGetBool(
            IReadOnlyDictionary<string, object?> inputs, string name, out bool value)
        {
            value = false;
            if (!inputs.TryGetValue(name, out var raw) || raw is null) return false;

            if (raw is bool flag) { value = flag; return true; }
            return bool.TryParse(raw.ToString(), out value);
        }
    }
}
