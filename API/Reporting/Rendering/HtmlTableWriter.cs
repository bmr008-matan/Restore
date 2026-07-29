#nullable enable
using System.Text;
using API.Reporting.Data;
using API.Reporting.Model;
using API.Reporting.Model.Elements;
using API.Reporting.Runtime;

namespace API.Reporting.Rendering
{
    /// <summary>
    /// Renders a data table, including multi-level group headers and footers, aggregates, conditional
    /// formatting and page-break control.
    ///
    /// It emits a real HTML table rather than a grid of divs for one specific reason: Chromium reprints
    /// a thead after every page break, which is what makes "repeat column headers on each page" work at
    /// all. Everything else about the layout is arranged around keeping that property.
    /// </summary>
    public sealed class HtmlTableWriter
    {
        private readonly ExpressionEvaluator _evaluator;
        private readonly ValueFormatter _formatter;
        private readonly TokenInterpolator _interpolator;
        private readonly RenderDiagnostics _diagnostics;
        private readonly ReportDefinition _definition;

        public HtmlTableWriter(
            ReportDefinition definition,
            ExpressionEvaluator evaluator,
            ValueFormatter formatter,
            TokenInterpolator interpolator,
            RenderDiagnostics diagnostics)
        {
            _definition = definition;
            _evaluator = evaluator;
            _formatter = formatter;
            _interpolator = interpolator;
            _diagnostics = diagnostics;
        }

        public void Write(
            StringBuilder html,
            TableElement table,
            ReportDataSet data,
            GroupedRows grouped,
            ExpressionScope scope,
            string path)
        {
            var columns = table.VisibleColumns.ToList();
            if (columns.Count == 0) return;

            if (grouped.AllRows.Count == 0)
            {
                WriteEmpty(html, table);
                return;
            }

            var context = new TableContext(table, columns, data, scope, path);

            html.Append("<table class=\"rpt-table\"")
                .Append(Html.StyleAttr(CssBuilder.InlineStyle(table.Style, table.Direction)))
                .Append('>');

            WriteColGroup(html, columns);
            if (table.ShowColumnHeaders) WriteHeader(html, context);

            // A single tbody when nothing asks for keep-together; otherwise one per group instance at
            // the outermost level that does.
            var keepLevel = FindKeepTogetherLevel(table);

            if (grouped.HasGroups)
            {
                if (keepLevel is null) html.Append("<tbody>");
                foreach (var node in grouped.Roots)
                    WriteGroup(html, context, node, keepLevel);
                if (keepLevel is null) html.Append("</tbody>");
            }
            else
            {
                html.Append("<tbody>");
                WriteDetailRows(html, context, grouped.AllRows, startIndex: 0);
                html.Append("</tbody>");
            }

            if (table.GrandTotals.Count > 0)
                WriteGrandTotal(html, context, grouped);

            html.Append("</table>");
        }

        private void WriteEmpty(StringBuilder html, TableElement table)
        {
            var text = string.IsNullOrWhiteSpace(table.EmptyText) ? "No data" : table.EmptyText!;
            html.Append("<div class=\"rpt-empty\">").Append(Html.Text(text)).Append("</div>");
        }

        /// <summary>
        /// Keep-together is implemented with a tbody carrying break-inside: avoid. Nested tbody
        /// elements are not valid HTML, so only one level can be honoured — the outermost that asks.
        /// The validator warns when a template requests more than one.
        /// </summary>
        private static int? FindKeepTogetherLevel(TableElement table)
        {
            for (var i = 0; i < table.Groups.Count; i++)
                if (table.Groups[i].KeepTogether) return i;
            return null;
        }

        private static void WriteColGroup(StringBuilder html, List<TableColumnDef> columns)
        {
            html.Append("<colgroup>");
            foreach (var column in columns)
            {
                html.Append("<col");
                if (column.WidthPercent is > 0)
                    html.Append(" style=\"width:").Append(column.WidthPercent.Value.ToString("0.##",
                        System.Globalization.CultureInfo.InvariantCulture)).Append("%\"");
                else if (column.WidthMm is > 0)
                    html.Append(" style=\"width:").Append(CssBuilder.Mm(column.WidthMm.Value)).Append('"');
                html.Append('>');
            }
            html.Append("</colgroup>");
        }

        private void WriteHeader(StringBuilder html, TableContext context)
        {
            var table = context.Table;

            // Column headers only go in a thead when they should repeat. Otherwise they are an ordinary
            // first body row, so they print once.
            html.Append(table.RepeatHeaderOnEachPage ? "<thead><tr>" : "<tbody><tr>");

            foreach (var column in context.Columns)
            {
                var style = StyleDef.MergeAll(
                    _definition.DefaultStyle,
                    new StyleDef { Bold = true },
                    table.HeaderStyle,
                    BorderStyle(table),
                    column.HeaderStyle,
                    AlignmentFor(column, context));

                html.Append("<th")
                    .Append(Html.StyleAttr(CssBuilder.InlineStyle(style)))
                    .Append('>')
                    .Append(Html.Text(Caption(column, context)))
                    .Append("</th>");
            }

            html.Append(table.RepeatHeaderOnEachPage ? "</tr></thead>" : "</tr></tbody>");
        }

        private string Caption(TableColumnDef column, TableContext context)
        {
            if (column.Caption is { Length: > 0 }) return column.Caption;
            if (column.Field is { Length: > 0 })
                return context.Data.FindField(column.Field)?.EffectiveCaption ?? column.Field;
            return column.Expression ?? "";
        }

        private void WriteGroup(StringBuilder html, TableContext context, GroupNode node, int? keepLevel)
        {
            var ownsTbody = keepLevel == node.Level;
            if (ownsTbody) html.Append("<tbody class=\"rpt-keep\">");

            var definition = node.Definition;
            var groupScope = context.Scope.WithGroup(node.Aggregates);

            if (definition.ShowHeader)
                WriteGroupHeader(html, context, node, groupScope);

            if (node.IsLeaf)
                WriteDetailRows(html, context, node.Rows, startIndex: 0, groupScope);
            else
                foreach (var child in node.Children)
                    WriteGroup(html, context, child, keepLevel);

            if (definition.ShowFooter && (definition.Aggregates.Count > 0 || definition.FooterText is { Length: > 0 }))
                WriteGroupFooter(html, context, node, groupScope);

            if (ownsTbody) html.Append("</tbody>");
        }

        private void WriteGroupHeader(StringBuilder html, TableContext context, GroupNode node, ExpressionScope groupScope)
        {
            var definition = node.Definition;

            // Page-break-before is applied to the group's first printed row, which is how a break lands
            // in the right place inside a table.
            var classes = new List<string> { "rpt-grp-h", $"rpt-grp-h-{node.Level}" };
            if (definition.PageBreakBefore) classes.Add("rpt-break-before");

            var label = BuildGroupLabel(definition.HeaderText, node, groupScope, context, definition.ShowItemCount);

            var style = StyleDef.MergeAll(
                _definition.DefaultStyle,
                new StyleDef { Bold = true, BackColor = "#EEEEEE" },
                BorderStyle(context.Table),
                definition.HeaderStyle);

            if (!TryApplyRules(definition.Rules, groupScope, $"{context.Path}.groups[{node.Level}]", ref style))
                return;

            WriteAggregateRow(html, context, classes, label, style,
                definition.HeaderAggregates, node.Aggregates, groupScope);
        }

        private void WriteGroupFooter(StringBuilder html, TableContext context, GroupNode node, ExpressionScope groupScope)
        {
            var definition = node.Definition;

            var classes = new List<string> { "rpt-grp-f", $"rpt-grp-f-{node.Level}" };
            if (definition.PageBreakAfter) classes.Add("rpt-break-after");

            var label = BuildGroupLabel(definition.FooterText, node, groupScope, context, showItemCount: false);

            var style = StyleDef.MergeAll(
                _definition.DefaultStyle,
                new StyleDef
                {
                    Bold = true,
                    Border = new BorderDef { Top = new BorderSideDef { WidthPt = 0.75 } }
                },
                definition.FooterStyle);

            WriteAggregateRow(html, context, classes, label, style,
                definition.Aggregates, node.Aggregates, groupScope);
        }

        private void WriteGrandTotal(StringBuilder html, TableContext context, GroupedRows grouped)
        {
            var table = context.Table;
            var scope = context.Scope.WithGroup(grouped.GrandTotals);

            var style = StyleDef.MergeAll(
                _definition.DefaultStyle,
                new StyleDef
                {
                    Bold = true,
                    Border = new BorderDef { Top = new BorderSideDef { WidthPt = 1.5, Style = Model.BorderStyle.Double } }
                },
                table.GrandTotalStyle);

            var label = _interpolator.Interpolate(table.GrandTotalLabel, scope, _diagnostics, $"{context.Path}.grandTotalLabel");

            html.Append("<tbody>");
            WriteAggregateRow(html, context, new List<string> { "rpt-grand" }, label, style,
                table.GrandTotals, grouped.GrandTotals, scope);
            html.Append("</tbody>");
        }

        /// <summary>
        /// Writes a label-plus-totals row. Totals that name a target column are placed in that column so
        /// a subtotal sits directly under the values it adds up; totals without one are folded into the
        /// label, and the label spans everything up to the first targeted column.
        /// </summary>
        private void WriteAggregateRow(
            StringBuilder html,
            TableContext context,
            List<string> classes,
            string label,
            StyleDef? rowStyle,
            List<AggregateDef> aggregates,
            IReadOnlyDictionary<string, object?> values,
            ExpressionScope scope)
        {
            var columns = context.Columns;

            var targeted = new Dictionary<int, AggregateDef>();
            var untargeted = new List<AggregateDef>();

            foreach (var aggregate in aggregates)
            {
                var index = aggregate.TargetColumn is { Length: > 0 }
                    ? columns.FindIndex(c => string.Equals(c.ColumnKey, aggregate.TargetColumn, StringComparison.OrdinalIgnoreCase))
                    : -1;

                if (index >= 0) targeted[index] = aggregate;
                else untargeted.Add(aggregate);
            }

            var labelText = new StringBuilder(label);
            foreach (var aggregate in untargeted)
            {
                var text = FormatAggregate(aggregate, values);
                if (text.Length == 0) continue;
                if (labelText.Length > 0) labelText.Append("  ");
                if (aggregate.Label is { Length: > 0 }) labelText.Append(aggregate.Label).Append(' ');
                labelText.Append(text);
            }

            var firstTargeted = targeted.Keys.Count == 0 ? columns.Count : targeted.Keys.Min();
            var labelSpan = Math.Max(1, firstTargeted);

            html.Append("<tr class=\"").Append(string.Join(' ', classes)).Append('"')
                .Append(Html.StyleAttr(CssBuilder.InlineStyle(rowStyle)))
                .Append('>');

            html.Append("<td");
            if (labelSpan > 1) html.Append(" colspan=\"").Append(labelSpan).Append('"');
            html.Append('>').Append(Html.Text(labelText.ToString())).Append("</td>");

            for (var i = labelSpan; i < columns.Count; i++)
            {
                var cellStyle = StyleDef.Merge(AlignmentFor(columns[i], context), null);
                html.Append("<td").Append(Html.StyleAttr(CssBuilder.InlineStyle(cellStyle))).Append('>');

                if (targeted.TryGetValue(i, out var aggregate))
                {
                    if (aggregate.Label is { Length: > 0 }) html.Append(Html.Text(aggregate.Label)).Append(' ');
                    html.Append(Html.Text(FormatAggregate(aggregate, values)));
                }

                html.Append("</td>");
            }

            html.Append("</tr>");
        }

        private string FormatAggregate(AggregateDef aggregate, IReadOnlyDictionary<string, object?> values)
        {
            values.TryGetValue(aggregate.Key, out var value);
            return _formatter.Format(value, aggregate.Format ?? aggregate.Style?.Format);
        }

        private string BuildGroupLabel(
            string? template,
            GroupNode node,
            ExpressionScope groupScope,
            TableContext context,
            bool showItemCount)
        {
            // With no template the group key alone is the sensible label.
            var text = string.IsNullOrWhiteSpace(template)
                ? node.KeyDisplay
                : _interpolator.Interpolate(template, groupScope, _diagnostics,
                    $"{context.Path}.groups[{node.Level}].label",
                    extras: GroupExtras(node));

            if (showItemCount) text += $" ({node.ItemCount})";
            return text;
        }

        /// <summary>
        /// Makes the group's own key reachable from its label template by field name, so
        /// "{brand} ({Count})" works in a group header even though the header is not inside a row.
        /// </summary>
        private static Dictionary<string, object?> GroupExtras(GroupNode node)
        {
            var extras = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["GroupKey"] = node.Key,
                ["Count"] = (long)node.ItemCount
            };

            if (node.Definition.Field is { Length: > 0 }) extras[node.Definition.Field] = node.Key;

            foreach (var pair in node.Aggregates) extras[pair.Key] = pair.Value;
            return extras;
        }

        private void WriteDetailRows(
            StringBuilder html,
            TableContext context,
            IReadOnlyList<ReportRow> rows,
            int startIndex,
            ExpressionScope? groupScope = null)
        {
            var table = context.Table;
            var baseScope = groupScope ?? context.Scope;

            for (var i = 0; i < rows.Count; i++)
            {
                var rowScope = baseScope.WithRow(rows[i]);
                var isAlternate = (startIndex + i) % 2 == 1;

                var rowStyle = StyleDef.MergeAll(
                    _definition.DefaultStyle,
                    table.RowStyle,
                    isAlternate ? table.AlternateRowStyle : null);

                if (!TryApplyRules(table.RowRules, rowScope, $"{context.Path}.rowRules", ref rowStyle))
                    continue;

                html.Append("<tr")
                    .Append(Html.StyleAttr(CssBuilder.InlineStyle(rowStyle)))
                    .Append('>');

                foreach (var column in context.Columns)
                    WriteCell(html, context, column, rowScope);

                html.Append("</tr>");
            }
        }

        private void WriteCell(StringBuilder html, TableContext context, TableColumnDef column, ExpressionScope rowScope)
        {
            var meta = column.Field is { Length: > 0 } ? context.Data.FindField(column.Field) : null;

            var value = column.Field is { Length: > 0 }
                ? rowScope.Fields.TryGetValue(column.Field, out var raw) ? raw : null
                : _evaluator.EvaluateValue(column.Expression, rowScope, _diagnostics, $"{context.Path}.column");

            var style = StyleDef.MergeAll(
                AlignmentFor(column, context),
                BorderStyle(context.Table),
                column.Style);

            // A rule may hide the cell's content or override its text, so rules resolve before the value
            // is formatted rather than after.
            string? textOverride = null;
            var hidden = false;

            foreach (var rule in column.Rules.Where(r => r.Enabled))
            {
                if (!_evaluator.EvaluateBool(rule.Expression, rowScope, _diagnostics, $"{context.Path}.column.rule"))
                    continue;

                if (rule.Actions.Hide == true) hidden = true;
                if (rule.Actions.TextOverride is { Length: > 0 }) textOverride = rule.Actions.TextOverride;
                style = StyleDef.Merge(style, rule.Actions.ToStyle());
            }

            html.Append("<td").Append(Html.StyleAttr(CssBuilder.InlineStyle(style))).Append('>');

            if (!hidden)
            {
                var text = textOverride is { Length: > 0 }
                    ? _interpolator.Interpolate(textOverride, rowScope, _diagnostics, $"{context.Path}.column.textOverride")
                    : _formatter.Format(value, column.Format ?? style?.Format ?? meta?.Format);

                html.Append(Html.Text(text));
            }

            html.Append("</td>");
        }

        /// <summary>
        /// Applies a rule list to a style, returning false when a rule hides the whole row or group.
        /// </summary>
        private bool TryApplyRules(
            List<ConditionalRuleDef> rules, ExpressionScope scope, string path, ref StyleDef? style)
        {
            foreach (var rule in rules.Where(r => r.Enabled))
            {
                if (!_evaluator.EvaluateBool(rule.Expression, scope, _diagnostics, path)) continue;
                if (rule.Actions.Hide == true) return false;
                style = StyleDef.Merge(style, rule.Actions.ToStyle());
            }
            return true;
        }

        /// <summary>
        /// Numeric columns default to right alignment, which is what a money or quantity column wants.
        /// Physical right rather than logical end, so an amount column looks the same in a Hebrew report
        /// as in an English one; a designer who wants otherwise sets Align explicitly.
        /// </summary>
        private StyleDef? AlignmentFor(TableColumnDef column, TableContext context)
        {
            if (column.Align is not null) return new StyleDef { Align = column.Align };

            var meta = column.Field is { Length: > 0 } ? context.Data.FindField(column.Field) : null;
            return meta?.IsNumeric == true ? new StyleDef { Align = HorizontalAlign.Right } : null;
        }

        private static StyleDef? BorderStyle(TableElement table) =>
            table.CellBorder is null ? null : new StyleDef { Border = table.CellBorder };

        private sealed record TableContext(
            TableElement Table,
            List<TableColumnDef> Columns,
            ReportDataSet Data,
            ExpressionScope Scope,
            string Path);
    }
}
