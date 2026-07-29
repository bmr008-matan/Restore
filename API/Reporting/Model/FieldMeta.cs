#nullable enable
using System.Text.Json.Serialization;
namespace API.Reporting.Model
{
    /// <summary>
    /// Describes one column of a data set. Used by the designer's field picker, by the expression
    /// evaluator to give each field a real CLR type, and by the validator to reject references to
    /// fields that do not exist.
    /// </summary>
    public class FieldMeta
    {
        public string Name { get; set; } = "";

        /// <summary>Default column caption. Falls back to <see cref="Name"/>.</summary>
        public string? Caption { get; set; }

        public FieldDataType DataType { get; set; } = FieldDataType.String;

        /// <summary>Default format applied when a column or element does not override it.</summary>
        public string? Format { get; set; }

        [JsonIgnore]
        public string EffectiveCaption => string.IsNullOrWhiteSpace(Caption) ? Name : Caption;

        /// <summary>
        /// Nullable CLR type this field maps to. Nullable throughout on purpose: a null cell must
        /// compare with C#'s lifted-operator semantics rather than blowing up an expression.
        /// </summary>
        [JsonIgnore]
        public Type ClrType => DataType switch
        {
            FieldDataType.Int => typeof(long?),
            FieldDataType.Decimal => typeof(decimal?),
            FieldDataType.Date => typeof(DateTime?),
            FieldDataType.Bool => typeof(bool?),
            _ => typeof(string)
        };

        /// <summary>Numeric fields are the only ones Sum and Avg accept.</summary>
        [JsonIgnore]
        public bool IsNumeric => DataType is FieldDataType.Int or FieldDataType.Decimal;
    }
}
