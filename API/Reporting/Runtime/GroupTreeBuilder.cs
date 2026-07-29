#nullable enable
using API.Reporting.Data;
using API.Reporting.Model;

namespace API.Reporting.Runtime
{
    /// <summary>
    /// One node of the grouping tree. Leaf nodes (those at the innermost declared level) carry the
    /// detail rows; interior nodes carry child groups. <see cref="Rows"/> always holds every row
    /// beneath the node, at any depth, so aggregates at an outer level cover the whole subtree.
    /// </summary>
    public sealed class GroupNode
    {
        /// <summary>Zero-based nesting level, matching the index in TableElement.Groups.</summary>
        public required int Level { get; init; }

        public required GroupDef Definition { get; init; }

        /// <summary>Raw group key, before formatting.</summary>
        public object? Key { get; init; }

        /// <summary>Formatted key, used in the group header label.</summary>
        public string KeyDisplay { get; set; } = "";

        public List<GroupNode> Children { get; } = new();

        /// <summary>Every row beneath this node, in render order.</summary>
        public List<ReportRow> Rows { get; } = new();

        /// <summary>Aggregates over <see cref="Rows"/>, keyed by <see cref="AggregateDef.Key"/>.</summary>
        public Dictionary<string, object?> Aggregates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsLeaf => Children.Count == 0;

        public int ItemCount => Rows.Count;
    }

    /// <summary>Result of grouping a data set for one table.</summary>
    public sealed class GroupedRows
    {
        /// <summary>Top-level groups. Empty when the table declares no grouping.</summary>
        public List<GroupNode> Roots { get; init; } = new();

        /// <summary>Every row, sorted. This is what a flat table renders directly.</summary>
        public List<ReportRow> AllRows { get; init; } = new();

        public Dictionary<string, object?> GrandTotals { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasGroups => Roots.Count > 0;
    }

    /// <summary>
    /// Sorts rows by their group keys and builds the nesting tree, computing aggregates at every level
    /// plus the grand total.
    /// </summary>
    public sealed class GroupTreeBuilder
    {
        private readonly ExpressionEvaluator _evaluator;
        private readonly ValueFormatter _formatter;

        public GroupTreeBuilder(ExpressionEvaluator evaluator, ValueFormatter formatter)
        {
            _evaluator = evaluator;
            _formatter = formatter;
        }

        public GroupedRows Build(
            IReadOnlyList<ReportRow> rows,
            IReadOnlyList<GroupDef> groups,
            IReadOnlyList<SortDef> withinGroupSort,
            IReadOnlyList<AggregateDef> grandTotals,
            ExpressionScope scope,
            RenderDiagnostics diagnostics,
            string path)
        {
            // Group keys are computed once per row up front. Doing it lazily inside the sort comparer
            // would re-evaluate expressions O(n log n) times instead of O(n).
            var keyed = rows.Select(row => new KeyedRow(row, ComputeKeys(row, groups, scope, diagnostics, path))).ToList();

            var sorted = SortRows(keyed, groups, withinGroupSort);
            var allRows = sorted.Select(k => k.Row).ToList();

            var result = new GroupedRows
            {
                AllRows = allRows,
                GrandTotals = Aggregator.ComputeAll(grandTotals, allRows, diagnostics, $"{path}.grandTotal")
            };

            if (groups.Count == 0) return result;

            result.Roots.AddRange(BuildLevel(sorted, groups, level: 0, diagnostics, path));
            return result;
        }

        private object?[] ComputeKeys(
            ReportRow row,
            IReadOnlyList<GroupDef> groups,
            ExpressionScope scope,
            RenderDiagnostics diagnostics,
            string path)
        {
            var keys = new object?[groups.Count];
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                keys[i] = group.Field is { Length: > 0 }
                    ? row[group.Field]
                    : _evaluator.EvaluateValue(group.Expression, scope.WithRow(row), diagnostics, $"{path}.groups[{i}]");
            }
            return keys;
        }

        /// <summary>
        /// Sorts by every group key in declaration order, then by the data set's own sort. That
        /// ordering is what makes a single forward pass sufficient to detect group boundaries.
        /// </summary>
        private static List<KeyedRow> SortRows(
            List<KeyedRow> rows, IReadOnlyList<GroupDef> groups, IReadOnlyList<SortDef> withinGroupSort)
        {
            if (groups.Count == 0 && withinGroupSort.Count == 0) return rows;

            IOrderedEnumerable<KeyedRow>? ordered = null;

            for (var i = 0; i < groups.Count; i++)
            {
                var index = i;
                var descending = groups[i].SortDirection == SortDirection.Desc;

                ordered = ordered is null
                    ? Order(rows, r => r.Keys[index], descending)
                    : Then(ordered, r => r.Keys[index], descending);
            }

            foreach (var sort in withinGroupSort)
            {
                var field = sort.Field;
                var descending = sort.Direction == SortDirection.Desc;

                ordered = ordered is null
                    ? Order(rows, r => r.Row[field], descending)
                    : Then(ordered, r => r.Row[field], descending);
            }

            return ordered?.ToList() ?? rows;
        }

        private static IOrderedEnumerable<KeyedRow> Order(
            IEnumerable<KeyedRow> source, Func<KeyedRow, object?> selector, bool descending) =>
            descending
                ? source.OrderByDescending(selector, GroupKeyComparer.Instance)
                : source.OrderBy(selector, GroupKeyComparer.Instance);

        private static IOrderedEnumerable<KeyedRow> Then(
            IOrderedEnumerable<KeyedRow> source, Func<KeyedRow, object?> selector, bool descending) =>
            descending
                ? source.ThenByDescending(selector, GroupKeyComparer.Instance)
                : source.ThenBy(selector, GroupKeyComparer.Instance);

        /// <summary>
        /// Walks the sorted rows once, cutting a new node whenever the key at this level changes, and
        /// recursing for the level below.
        /// </summary>
        private List<GroupNode> BuildLevel(
            List<KeyedRow> rows,
            IReadOnlyList<GroupDef> groups,
            int level,
            RenderDiagnostics diagnostics,
            string path)
        {
            var nodes = new List<GroupNode>();
            var definition = groups[level];

            var start = 0;
            while (start < rows.Count)
            {
                var key = rows[start].Keys[level];

                var end = start + 1;
                while (end < rows.Count && GroupKeyComparer.Instance.Equals(rows[end].Keys[level], key)) end++;

                var slice = rows.GetRange(start, end - start);
                var node = new GroupNode { Level = level, Definition = definition, Key = key };

                node.KeyDisplay = _formatter.Format(key, definition.HeaderStyle?.Format);
                node.Rows.AddRange(slice.Select(k => k.Row));

                // Aggregates cover the whole subtree, so an outer subtotal is the sum of its inner ones.
                var nodePath = $"{path}.groups[{level}]";
                node.Aggregates = Aggregator.ComputeAll(definition.Aggregates
                    .Concat(definition.HeaderAggregates), node.Rows, diagnostics, nodePath);

                if (level + 1 < groups.Count)
                    node.Children.AddRange(BuildLevel(slice, groups, level + 1, diagnostics, path));

                nodes.Add(node);
                start = end;
            }

            return nodes;
        }

        private sealed record KeyedRow(ReportRow Row, object?[] Keys);
    }

    /// <summary>
    /// Orders and compares group keys. Nulls sort first and are equal to each other, numbers compare
    /// numerically regardless of whether the provider returned int, decimal or a numeric string, and
    /// text compares case-insensitively so "Nike" and "NIKE" do not become two separate groups.
    /// </summary>
    public sealed class GroupKeyComparer : IComparer<object?>, IEqualityComparer<object?>
    {
        public static readonly GroupKeyComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (IsNull(x)) return IsNull(y) ? 0 : -1;
            if (IsNull(y)) return 1;

            var nx = ValueFormatter.ToDecimal(x);
            var ny = ValueFormatter.ToDecimal(y);
            if (nx.HasValue && ny.HasValue) return nx.Value.CompareTo(ny.Value);

            if (x is DateTime dx && y is DateTime dy) return dx.CompareTo(dy);

            return string.Compare(Text(x), Text(y), StringComparison.OrdinalIgnoreCase);
        }

        public new bool Equals(object? x, object? y) => Compare(x, y) == 0;

        public int GetHashCode(object? obj) =>
            IsNull(obj) ? 0 : Text(obj).ToUpperInvariant().GetHashCode();

        private static bool IsNull(object? value) => value is null or DBNull;

        private static string Text(object? value) => value?.ToString() ?? "";
    }
}
