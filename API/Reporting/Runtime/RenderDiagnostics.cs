#nullable enable
namespace API.Reporting.Runtime
{
    /// <summary>
    /// Collects non-fatal problems found while rendering — a rule whose expression does not compile,
    /// an aggregate over a non-numeric field, a pushed data set missing a declared column.
    ///
    /// These deliberately do not abort the render: a report that mostly works is more useful than a
    /// 500. But they are not swallowed either — the runtime surfaces them on the response and in the
    /// log, so a broken expression is visible instead of silently behaving as "false".
    /// </summary>
    public sealed class RenderDiagnostics
    {
        private readonly List<RenderDiagnostic> _items = new();

        /// <summary>Guards against a bad expression inside a 50,000-row loop flooding the log.</summary>
        private const int MaxPerKey = 3;

        private readonly Dictionary<string, int> _countsByKey = new(StringComparer.Ordinal);

        public IReadOnlyList<RenderDiagnostic> Items => _items;

        public bool HasAny => _items.Count > 0;

        public void Add(string path, string message)
        {
            var key = path + "|" + message;
            _countsByKey.TryGetValue(key, out var seen);
            _countsByKey[key] = seen + 1;
            if (seen >= MaxPerKey) return;

            _items.Add(new RenderDiagnostic(path, message));
        }

        /// <summary>How many times a diagnostic actually occurred, including suppressed repeats.</summary>
        public int OccurrenceCount(string path, string message) =>
            _countsByKey.TryGetValue(path + "|" + message, out var n) ? n : 0;

        public override string ToString() =>
            string.Join("; ", _items.Select(i => $"{i.Path}: {i.Message}"));
    }

    public sealed record RenderDiagnostic(string Path, string Message);
}
