#nullable enable
namespace API.Reporting.Runtime
{
    /// <summary>
    /// Report-level facts exposed to expressions as <c>Report</c>. Unlike fields and parameters this is
    /// a real typed object, so member access on it resolves at parse time.
    /// </summary>
    public sealed class ReportContext
    {
        public string Name { get; init; } = "";
        public string Title { get; init; } = "";

        /// <summary>When this render started, in the report's culture for display purposes.</summary>
        public DateTime RunAt { get; init; } = DateTime.Now;

        public UserContext User { get; init; } = UserContext.Anonymous;

        /// <summary>
        /// Total row count across all data sets. Deliberately not a page count: pagination happens
        /// inside Chromium, after every rule has already been evaluated, so no expression can depend
        /// on which page a row lands on.
        /// </summary>
        public int RowCount { get; init; }
    }

    /// <summary>
    /// The current user, exposed as <c>Report.User</c>. This is what makes role-conditional bands and
    /// columns possible — "hide this column unless the viewer is a Manager".
    /// </summary>
    public sealed class UserContext
    {
        public static readonly UserContext Anonymous = new();

        public string Name { get; init; } = "";

        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

        /// <summary>Case-insensitive, so a template does not have to match the claim's casing.</summary>
        public bool IsInRole(string role) =>
            Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
    }
}
