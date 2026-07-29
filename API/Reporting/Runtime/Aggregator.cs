#nullable enable
using API.Reporting.Data;
using API.Reporting.Model;

namespace API.Reporting.Runtime
{
    /// <summary>
    /// Computes group totals and grand totals. Kept deliberately dumb — it is handed the exact rows to
    /// aggregate and knows nothing about grouping — so that a subtotal, a nested subtotal and the grand
    /// total all go through the same arithmetic and cannot drift apart.
    /// </summary>
    public static class Aggregator
    {
        /// <summary>
        /// Sums and averages widen to decimal so adding integer columns can neither overflow nor
        /// truncate. Nulls are skipped rather than counted as zero, which is what makes Avg correct.
        /// </summary>
        public static object? Compute(
            AggregateFunction function,
            string? field,
            IReadOnlyList<ReportRow> rows,
            RenderDiagnostics? diagnostics = null,
            string path = "aggregate")
        {
            if (function == AggregateFunction.Count)
                return (long)rows.Count;

            if (string.IsNullOrWhiteSpace(field))
            {
                diagnostics?.Add(path, $"{function} needs a field to aggregate.");
                return null;
            }

            switch (function)
            {
                case AggregateFunction.CountDistinct:
                    return (long)rows
                        .Select(r => r[field])
                        .Where(v => v is not null and not DBNull)
                        .Select(v => v!.ToString())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                case AggregateFunction.Sum:
                case AggregateFunction.Avg:
                {
                    var numbers = NumericValues(rows, field, diagnostics, path);
                    if (numbers.Count == 0) return null;
                    var sum = numbers.Sum();
                    return function == AggregateFunction.Sum ? sum : sum / numbers.Count;
                }

                case AggregateFunction.Min:
                case AggregateFunction.Max:
                    return MinOrMax(rows, field, function == AggregateFunction.Min);

                case AggregateFunction.First:
                    return rows.Select(r => r[field]).FirstOrDefault(v => v is not null and not DBNull);

                case AggregateFunction.Last:
                    return rows.Select(r => r[field]).LastOrDefault(v => v is not null and not DBNull);

                default:
                    diagnostics?.Add(path, $"Unsupported aggregate function {function}.");
                    return null;
            }
        }

        private static List<decimal> NumericValues(
            IReadOnlyList<ReportRow> rows, string field, RenderDiagnostics? diagnostics, string path)
        {
            var numbers = new List<decimal>(rows.Count);
            var nonNumeric = 0;

            foreach (var row in rows)
            {
                var raw = row[field];
                if (raw is null or DBNull) continue;

                var value = ValueFormatter.ToDecimal(raw);
                if (value.HasValue) numbers.Add(value.Value);
                else nonNumeric++;
            }

            if (nonNumeric > 0)
                diagnostics?.Add(path,
                    $"Skipped {nonNumeric} non-numeric value(s) in \"{field}\" while aggregating.");

            return numbers;
        }

        /// <summary>
        /// Compares numerically when every value is a number, and with IComparable otherwise, so Min
        /// and Max work on dates and text as well as amounts. Mixed types fall back to comparing only
        /// values that share a type with the first one, rather than guessing at an ordering across types.
        /// </summary>
        private static object? MinOrMax(IReadOnlyList<ReportRow> rows, string field, bool wantMin)
        {
            var values = rows
                .Select(r => r[field])
                .Where(v => v is not null and not DBNull)
                .Select(v => v!)
                .ToList();

            if (values.Count == 0) return null;

            var numbers = values.Select(v => (Raw: v, Number: ValueFormatter.ToDecimal(v))).ToList();
            if (numbers.All(n => n.Number.HasValue))
            {
                var ordered = wantMin
                    ? numbers.MinBy(n => n.Number!.Value)
                    : numbers.MaxBy(n => n.Number!.Value);
                return ordered.Raw;
            }

            var best = values[0];
            foreach (var value in values.Skip(1))
            {
                if (value.GetType() != best.GetType() || value is not IComparable comparable) continue;

                var comparison = comparable.CompareTo(best);
                if (wantMin ? comparison < 0 : comparison > 0) best = value;
            }

            return best;
        }

        /// <summary>
        /// Computes a whole set of aggregates over the same rows, keyed by
        /// <see cref="AggregateDef.Key"/> so expressions can read them as Group.SumOfTotal.
        /// </summary>
        public static Dictionary<string, object?> ComputeAll(
            IEnumerable<AggregateDef> aggregates,
            IReadOnlyList<ReportRow> rows,
            RenderDiagnostics? diagnostics = null,
            string path = "aggregate")
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                // Always available, whether or not the template declared a Count aggregate.
                ["Count"] = (long)rows.Count
            };

            foreach (var agg in aggregates)
                result[agg.Key] = Compute(agg.Function, agg.Field, rows, diagnostics, path);

            return result;
        }
    }
}
