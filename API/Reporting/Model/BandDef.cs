#nullable enable
using System.Text.Json.Serialization;
using API.Reporting.Model.Elements;

namespace API.Reporting.Model
{
    /// <summary>
    /// A horizontal band of the report. Bands are what make pagination predictable: report bands are
    /// emitted once in document flow, page bands become the repeating Chromium header and footer, and
    /// the detail band holds the tables that grow and paginate.
    ///
    /// Group headers and footers are deliberately *not* bands — they live on
    /// <see cref="Elements.TableElement.Groups"/>, so a group header can never drift away from the
    /// level it belongs to.
    /// </summary>
    public class BandDef
    {
        public BandKind Kind { get; set; }

        /// <summary>
        /// Designed height. Report and detail bands grow past this if their content needs more room;
        /// page header and footer bands are hard-limited by the page margin reserved for them.
        /// </summary>
        public double HeightMm { get; set; } = 20;

        public bool Visible { get; set; } = true;

        public StyleDef? Style { get; set; }

        public List<ReportElement> Elements { get; set; } = new();

        /// <summary>
        /// Rules evaluated once per render, against parameters and report context only — a band is not
        /// inside a row, so it has no Fields to read.
        /// </summary>
        public List<ConditionalRuleDef> Rules { get; set; } = new();

        /// <summary>True for the two bands Chromium renders inside the page margins.</summary>
        [JsonIgnore]
        public bool IsPageBand => Kind is BandKind.PageHeader or BandKind.PageFooter;
    }
}
