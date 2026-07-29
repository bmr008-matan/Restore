#nullable enable
using API.Reporting.Data;
using API.Reporting.Model;

namespace API.Reporting.Runtime
{
    /// <summary>
    /// The values one expression evaluation can see: the current row, the report parameters, the
    /// enclosing group's aggregates, and the report context. Any of them may be absent — a band rule
    /// has no row, a flat table has no group.
    /// </summary>
    public sealed class ExpressionScope
    {
        public static readonly IReadOnlyDictionary<string, object?> NoValues =
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, object?> Fields { get; init; } = NoValues;

        public IReadOnlyDictionary<string, object?> Params { get; init; } = NoValues;

        public IReadOnlyDictionary<string, object?> Group { get; init; } = NoValues;

        public ReportContext Report { get; init; } = new();

        /// <summary>
        /// Declared CLR type per reference, keyed "Fields.total", "Params.minTotal",
        /// "Group.SumOfTotal". Populated by <see cref="TypeMap"/> from the definition's own metadata.
        ///
        /// This exists so an expression's meaning does not depend on what the first row happened to
        /// contain: without it, "Fields.total &gt; 0" would compile differently when total is null in
        /// row 1 than when it holds a value, which is exactly the kind of intermittent behaviour that
        /// makes reporting bugs impossible to pin down.
        /// </summary>
        public IReadOnlyDictionary<string, Type> DeclaredTypes { get; init; } = TypeMap.Empty;

        public ExpressionScope WithRow(ReportRow row) => new()
        {
            Fields = row.Values,
            Params = Params,
            Group = Group,
            Report = Report,
            DeclaredTypes = DeclaredTypes
        };

        public ExpressionScope WithGroup(IReadOnlyDictionary<string, object?> aggregates) => new()
        {
            Fields = Fields,
            Params = Params,
            Group = aggregates,
            Report = Report,
            DeclaredTypes = DeclaredTypes
        };

        public object? Lookup(string scope, string name)
        {
            var bag = scope switch
            {
                "Fields" => Fields,
                "Params" => Params,
                "Group" => Group,
                _ => NoValues
            };
            return bag.TryGetValue(name, out var value) ? value : null;
        }
    }

    /// <summary>Builds the declared-type map for an <see cref="ExpressionScope"/>.</summary>
    public static class TypeMap
    {
        public static readonly IReadOnlyDictionary<string, Type> Empty =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, Type> Create() => new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, Type> AddFields(
            this Dictionary<string, Type> map, IEnumerable<FieldMeta> fields)
        {
            foreach (var field in fields) map[$"Fields.{field.Name}"] = field.ClrType;
            return map;
        }

        public static Dictionary<string, Type> AddParameters(
            this Dictionary<string, Type> map, IEnumerable<ReportParameter> parameters)
        {
            foreach (var p in parameters) map[$"Params.{p.Name}"] = ClrTypeOf(p);
            return map;
        }

        public static Dictionary<string, Type> AddAggregates(
            this Dictionary<string, Type> map,
            IEnumerable<AggregateDef> aggregates,
            Func<string, FieldMeta?> resolveField)
        {
            foreach (var agg in aggregates)
                map[$"Group.{agg.Key}"] = ClrTypeOf(agg, resolveField);

            // The group key itself, and the plain row count, are always available.
            map["Group.Count"] = typeof(long?);
            return map;
        }

        public static Type ClrTypeOf(ReportParameter p) => p.Type switch
        {
            ParameterType.Int => typeof(long?),
            ParameterType.Decimal => typeof(decimal?),
            ParameterType.Date => typeof(DateTime?),
            ParameterType.Bool => typeof(bool?),
            // Multi-value parameters arrive as a list, so Contains() works in expressions.
            ParameterType.MultiSelect => typeof(IReadOnlyList<object?>),
            ParameterType.DateRange => typeof(IReadOnlyList<object?>),
            _ => typeof(string)
        };

        /// <summary>
        /// Sum and Avg always widen to decimal so that summing integers cannot overflow or truncate.
        /// Count is a whole number. Min, Max, First and Last keep the underlying field's type.
        /// </summary>
        public static Type ClrTypeOf(AggregateDef agg, Func<string, FieldMeta?> resolveField)
        {
            switch (agg.Function)
            {
                case AggregateFunction.Count:
                case AggregateFunction.CountDistinct:
                    return typeof(long?);
                case AggregateFunction.Sum:
                case AggregateFunction.Avg:
                    return typeof(decimal?);
                default:
                    var meta = agg.Field is null ? null : resolveField(agg.Field);
                    return meta?.ClrType ?? typeof(object);
            }
        }
    }
}
