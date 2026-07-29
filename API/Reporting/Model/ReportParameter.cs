#nullable enable
using System.Text.Json.Serialization;
namespace API.Reporting.Model
{
    /// <summary>
    /// A value the report asks for at run time. Drives both the designer's parameter editor and the
    /// auto-generated run dialog, and is what integrators discover via GET /api/reports/{id}/parameters.
    /// </summary>
    public class ReportParameter
    {
        /// <summary>Identifier used in expressions as Params.name and in dataset inputs.</summary>
        public string Name { get; set; } = "";

        /// <summary>Prompt shown to the user. Falls back to <see cref="Name"/> when unset.</summary>
        public string? Label { get; set; }

        public ParameterType Type { get; set; } = ParameterType.Text;

        public bool Required { get; set; }

        /// <summary>
        /// Default as a string, resolved by the parameter binder. Supports the relative-date tokens
        /// @Today, @Yesterday, @StartOfMonth, @EndOfMonth, @StartOfYear, @EndOfYear and @CurrentUser.
        /// </summary>
        public string? DefaultValue { get; set; }

        /// <summary>For Select and MultiSelect: the predefined table id supplying the options.</summary>
        public int? LookupTableId { get; set; }

        /// <summary>Field in the lookup data set used as the stored value.</summary>
        public string? ValueField { get; set; }

        /// <summary>Field in the lookup data set shown to the user.</summary>
        public string? DisplayField { get; set; }

        /// <summary>Display order in the generated run dialog.</summary>
        public int Order { get; set; }

        /// <summary>Bound and usable in expressions, but never prompted for.</summary>
        public bool Hidden { get; set; }

        [JsonIgnore]
        public string EffectiveLabel => string.IsNullOrWhiteSpace(Label) ? Name : Label;

        /// <summary>True when the parameter carries more than one value.</summary>
        [JsonIgnore]
        public bool IsMultiValue => Type is ParameterType.MultiSelect or ParameterType.DateRange;
    }
}
