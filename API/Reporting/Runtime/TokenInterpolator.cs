#nullable enable
using System.Text.RegularExpressions;

namespace API.Reporting.Runtime
{
    /// <summary>
    /// Expands the <c>{token}</c> placeholders that appear in text elements, group labels and page
    /// header and footer text.
    ///
    /// Supported forms:
    /// <c>{@param}</c> a report parameter, <c>{field}</c> a field of the current row or the current
    /// group's key and aggregates, <c>{ReportTitle}</c> <c>{ReportName}</c> <c>{Date}</c>
    /// <c>{DateTime}</c> <c>{Time}</c> <c>{CurrentUser}</c> report metadata, and <c>{Count}</c> the
    /// current group's row count. Any token may carry a format after a colon, e.g.
    /// <c>{total:#,##0.00}</c> or <c>{soldAt:dd/MM/yyyy}</c>.
    ///
    /// <c>{PageNumber}</c> and <c>{TotalPages}</c> are intentionally left untouched: only Chromium can
    /// substitute those, and it only does so inside its header and footer templates. The header/footer
    /// builder converts them there, and the validator rejects them everywhere else rather than letting
    /// a literal "{PageNumber}" print in the middle of a report.
    /// </summary>
    public sealed class TokenInterpolator
    {
        private static readonly Regex TokenPattern = new(
            @"\{(?<name>@?[A-Za-z_][A-Za-z0-9_.]*)(?::(?<format>[^{}]+))?\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Tokens only Chromium can resolve, passed through verbatim.</summary>
        public static readonly IReadOnlySet<string> PageTokens =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PageNumber", "TotalPages" };

        private readonly ValueFormatter _formatter;

        public TokenInterpolator(ValueFormatter formatter) => _formatter = formatter;

        public string Interpolate(
            string? text,
            ExpressionScope scope,
            RenderDiagnostics diagnostics,
            string path,
            IReadOnlyDictionary<string, object?>? extras = null)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (!text.Contains('{')) return text;

            return TokenPattern.Replace(text, match =>
            {
                var name = match.Groups["name"].Value;
                var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;

                if (PageTokens.Contains(name)) return match.Value;

                if (!TryResolve(name, scope, extras, out var value))
                {
                    diagnostics.Add(path, $"Unknown token \"{match.Value}\".");
                    return match.Value;
                }

                return _formatter.Format(value, format);
            });
        }

        private bool TryResolve(
            string name,
            ExpressionScope scope,
            IReadOnlyDictionary<string, object?>? extras,
            out object? value)
        {
            // Explicit parameter reference: {@fromDate}
            if (name.StartsWith('@'))
            {
                var paramName = name[1..];
                if (scope.Params.TryGetValue(paramName, out value)) return true;
                value = null;
                return false;
            }

            if (extras is not null && extras.TryGetValue(name, out value)) return true;

            switch (name)
            {
                case "ReportTitle": value = scope.Report.Title; return true;
                case "ReportName": value = scope.Report.Name; return true;
                case "Date": value = scope.Report.RunAt.Date; return true;
                case "DateTime": value = scope.Report.RunAt; return true;
                case "Time": value = TimeOnly.FromDateTime(scope.Report.RunAt); return true;
                case "CurrentUser": value = scope.Report.User.Name; return true;
                case "RowCount": value = scope.Report.RowCount; return true;
            }

            // Scoped forms, e.g. {Fields.total} or {Group.SumOfTotal}, for disambiguation.
            var dot = name.IndexOf('.');
            if (dot > 0)
            {
                var prefix = name[..dot];
                var rest = name[(dot + 1)..];
                if (prefix is "Fields" or "Params" or "Group")
                {
                    value = scope.Lookup(prefix, rest);
                    return true;
                }
            }

            // Bare name: current row wins, then the group's aggregates, then parameters. Row first
            // because that is what a designer dragging a field onto a band means by "{total}".
            if (scope.Fields.TryGetValue(name, out value)) return true;
            if (scope.Group.TryGetValue(name, out value)) return true;
            if (scope.Params.TryGetValue(name, out value)) return true;

            value = null;
            return false;
        }

        /// <summary>Reports whether text references a page token, used by the validator.</summary>
        public static bool ContainsPageToken(string? text) =>
            !string.IsNullOrEmpty(text)
            && TokenPattern.Matches(text).Any(m => PageTokens.Contains(m.Groups["name"].Value));
    }
}
