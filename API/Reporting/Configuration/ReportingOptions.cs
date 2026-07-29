#nullable enable
namespace API.Reporting.Configuration
{
    /// <summary>
    /// Everything the reporting subsystem reads from configuration, bound from the "Reporting" section.
    /// Kept in one place so an operator can see the whole surface at once.
    /// </summary>
    public sealed class ReportingOptions
    {
        public const string SectionName = "Reporting";

        public ChromiumOptions Chromium { get; set; } = new();
        public OracleOptions Oracle { get; set; } = new();
        public LimitOptions Limits { get; set; } = new();
        public FontOptions Fonts { get; set; } = new();
        public DatabaseOptions Database { get; set; } = new();

        public sealed class ChromiumOptions
        {
            /// <summary>
            /// Path to an existing Chromium or Chrome binary. When empty, PuppeteerSharp downloads its
            /// own copy on first use — convenient for a developer, but a deployment should point this at
            /// a browser it already ships so start-up is predictable and offline-safe.
            /// </summary>
            public string? ExecutablePath { get; set; }

            /// <summary>
            /// Containers generally cannot use the Chromium sandbox, and /dev/shm is usually too small,
            /// hence these defaults. font-render-hinting=none keeps text metrics consistent between
            /// hosts, which matters when a report's column widths are tuned to fit.
            /// </summary>
            public string[] LaunchArguments { get; set; } =
            {
                "--no-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--font-render-hinting=none"
            };

            /// <summary>How many pages may render at once. Each one costs real browser memory.</summary>
            public int MaxConcurrentRenders { get; set; } = 4;

            /// <summary>Abandons a render rather than letting a pathological page hang a request.</summary>
            public int RenderTimeoutSeconds { get; set; } = 60;
        }

        public sealed class OracleOptions
        {
            public string? ConnectionString { get; set; }

            /// <summary>
            /// The reporting package and procedure, and the names of its three parameters. Every one is
            /// configurable so the provider can be pointed at an existing package without a code change.
            /// </summary>
            public string PackageName { get; set; } = "PKG_REPORTS";
            public string ProcedureName { get; set; } = "GET_DATA";
            public string TableIdParameter { get; set; } = "p_table_id";
            public string InputsParameter { get; set; } = "p_params";
            public string CursorParameter { get; set; } = "p_cursor";

            public int CommandTimeoutSeconds { get; set; } = 60;
        }

        public sealed class LimitOptions
        {
            /// <summary>
            /// Row cap per data set. A report that would return a million rows is a mistake, and
            /// discovering it as a 413 is much better than as an exhausted browser process.
            /// </summary>
            public int MaxRowsPerDataSet { get; set; } = 50_000;

            /// <summary>Cap on the whole generate call, including data fetch and PDF rendering.</summary>
            public int TotalTimeoutSeconds { get; set; } = 120;

            /// <summary>Rows returned by the designer's data preview endpoint.</summary>
            public int PreviewRowCount { get; set; } = 25;
        }

        public sealed class FontOptions
        {
            /// <summary>
            /// Inlines fonts as base64 @font-face rules, making output independent of what is installed
            /// on the host. Off by default because it adds roughly 1.3x each font file's size to every
            /// rendered document.
            /// </summary>
            public bool Embed { get; set; }

            public string Directory { get; set; } = "Reporting/Fonts";

            /// <summary>Family name the embedded faces are registered under.</summary>
            public string EmbeddedFamilyName { get; set; } = "ReportEmbedded";

            /// <summary>
            /// Fallback chain. DejaVu Sans leads because it covers Hebrew letters and niqqud and ships
            /// with most Linux images; Liberation Sans and FreeSans are the usual alternates.
            /// </summary>
            public string[] FamilyStack { get; set; } =
            {
                "DejaVu Sans",
                "Noto Sans Hebrew",
                "Liberation Sans",
                "FreeSans",
                "Arial",
                "sans-serif"
            };
        }

        public sealed class DatabaseOptions
        {
            /// <summary>
            /// Which provider stores the templates: "Sqlite" for local development and CI, so the
            /// designer runs with no Oracle instance, or "Oracle" in production so there is one database.
            /// </summary>
            public string Provider { get; set; } = "Sqlite";
        }
    }
}
