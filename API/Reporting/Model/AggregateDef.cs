#nullable enable
using System.Text.Json.Serialization;
namespace API.Reporting.Model
{
    /// <summary>
    /// One computed total. Used for group footers, group headers and the report grand total.
    /// </summary>
    public class AggregateDef
    {
        public AggregateFunction Function { get; set; } = AggregateFunction.Sum;

        /// <summary>Field being aggregated. Ignored by <see cref="AggregateFunction.Count"/>.</summary>
        public string? Field { get; set; }

        /// <summary>
        /// Field name of the table column this total is printed under, so subtotals line up with the
        /// values they add up. When null the total is written into the group's label cell instead.
        /// </summary>
        public string? TargetColumn { get; set; }

        public string? Format { get; set; }

        /// <summary>Optional prefix, e.g. "Subtotal: ".</summary>
        public string? Label { get; set; }

        public StyleDef? Style { get; set; }

        /// <summary>
        /// Key this aggregate is exposed under to expressions, as Group.key. Defaults to
        /// function plus field, e.g. "SumOfTotal", so rules can read a subtotal.
        /// </summary>
        [JsonIgnore]
        public string Key => string.IsNullOrWhiteSpace(Field)
            ? Function.ToString()
            : $"{Function}Of{char.ToUpperInvariant(Field[0])}{Field[1..]}";
    }
}
