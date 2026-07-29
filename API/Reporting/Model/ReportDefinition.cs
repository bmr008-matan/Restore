#nullable enable
using System.Text.Json.Serialization;
using API.Reporting.Model.Elements;

namespace API.Reporting.Model
{
    /// <summary>
    /// The whole report template, as one document. This is what the designer edits, what the API
    /// stores as a JSON column, and what the renderer consumes — there is no second representation of
    /// a layout anywhere in the system.
    /// </summary>
    public class ReportDefinition
    {
        /// <summary>Bumped when a change to this model needs migrating on load.</summary>
        public const string CurrentSchemaVersion = "1.0";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;

        public string Name { get; set; } = "";

        /// <summary>Title available to text as {ReportTitle}. Falls back to <see cref="Name"/>.</summary>
        public string? Title { get; set; }

        public string? Description { get; set; }

        public PageSetupDef Page { get; set; } = new();

        public List<ReportParameter> Parameters { get; set; } = new();

        public List<DataSetDef> DataSets { get; set; } = new();

        public List<BandDef> Bands { get; set; } = new();

        /// <summary>Base style every band and element inherits from.</summary>
        public StyleDef? DefaultStyle { get; set; }

        [JsonIgnore]
        public string EffectiveTitle => string.IsNullOrWhiteSpace(Title) ? Name : Title!;

        public BandDef? FindBand(BandKind kind) => Bands.FirstOrDefault(b => b.Kind == kind);

        public DataSetDef? FindDataSet(string? key) => string.IsNullOrWhiteSpace(key)
            ? null
            : DataSets.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

        public ReportParameter? FindParameter(string name) =>
            Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Every element on every band, flattened. Tables are yielded, not their columns.</summary>
        public IEnumerable<(BandDef Band, ReportElement Element)> AllElements() =>
            Bands.SelectMany(b => b.Elements.Select(e => (b, e)));

        public IEnumerable<TableElement> AllTables() =>
            Bands.SelectMany(b => b.Elements).OfType<TableElement>();

        /// <summary>
        /// A blank template with sane defaults, used by POST /api/report-templates when the caller
        /// does not supply a definition.
        /// </summary>
        public static ReportDefinition CreateEmpty(string name) => new()
        {
            Name = name,
            Page = new PageSetupDef(),
            Bands = new List<BandDef>
            {
                new() { Kind = BandKind.ReportHeader, HeightMm = 18 },
                new() { Kind = BandKind.PageHeader, HeightMm = 10 },
                new() { Kind = BandKind.Detail, HeightMm = 40 },
                new() { Kind = BandKind.PageFooter, HeightMm = 10 },
                new() { Kind = BandKind.ReportFooter, HeightMm = 12 }
            }
        };
    }
}
