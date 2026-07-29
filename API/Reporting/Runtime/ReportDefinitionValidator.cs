#nullable enable
using System.Text.RegularExpressions;
using API.Reporting.Model;
using API.Reporting.Model.Elements;

namespace API.Reporting.Runtime
{
    public sealed record ValidationError(string Path, string Message);

    public sealed class ValidationResult
    {
        public List<ValidationError> Errors { get; } = new();

        /// <summary>Things worth telling the designer about that do not block a save.</summary>
        public List<ValidationError> Warnings { get; } = new();

        public bool IsValid => Errors.Count == 0;

        public void Error(string path, string message) => Errors.Add(new ValidationError(path, message));
        public void Warn(string path, string message) => Warnings.Add(new ValidationError(path, message));
    }

    /// <summary>
    /// Checks a definition before it is stored or rendered. This is the gate that keeps a malformed
    /// template from ever reaching the renderer, which is what allows the renderer itself to be written
    /// without defensive checks on every field.
    ///
    /// Errors block the save. Warnings do not, but they surface in the designer — a report referencing a
    /// field that is not in the cached metadata is usually a mistake, but it can also just mean the
    /// catalogue is stale, so failing the save outright would be wrong.
    /// </summary>
    public sealed class ReportDefinitionValidator
    {
        private static readonly Regex ColorPattern = new(
            @"^(#[0-9A-Fa-f]{3}|#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}|rgba?\([^)]*\)|[a-zA-Z]+)$",
            RegexOptions.Compiled);

        private static readonly Regex IdentifierPattern = new(
            @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private readonly ExpressionEvaluator _evaluator;

        public ReportDefinitionValidator(ExpressionEvaluator evaluator) => _evaluator = evaluator;

        public ValidationResult Validate(ReportDefinition definition)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(definition.Name))
                result.Error("name", "A report needs a name.");

            ValidatePage(definition, result);
            ValidateParameters(definition, result);
            ValidateDataSets(definition, result);
            ValidateBands(definition, result);

            return result;
        }

        private static void ValidatePage(ReportDefinition definition, ValidationResult result)
        {
            var page = definition.Page;

            if (page.PaperSize == PaperSize.Custom)
            {
                if (page.CustomWidthMm is null or <= 0 || page.CustomHeightMm is null or <= 0)
                    result.Error("page", "A custom paper size needs a positive width and height in mm.");
            }

            if (page.Margins.LeftMm < 0 || page.Margins.RightMm < 0
                || page.Margins.TopMm < 0 || page.Margins.BottomMm < 0)
                result.Error("page.margins", "Margins cannot be negative.");

            if (page.ContentWidthMm <= 0)
                result.Error("page.margins",
                    "Left and right margins leave no room for content on this paper size.");

            // Chromium draws the header and footer templates inside the page margin. If the band is
            // taller than the margin reserved for it, the header silently clips instead of pushing the
            // body down, which is confusing enough to be worth blocking.
            if (page.HeaderHeightMm > page.Margins.TopMm)
                result.Error("page.headerHeightMm",
                    $"The page header is {page.HeaderHeightMm}mm but the top margin is only " +
                    $"{page.Margins.TopMm}mm. Increase the top margin, or the header will be cut off.");

            if (page.FooterHeightMm > page.Margins.BottomMm)
                result.Error("page.footerHeightMm",
                    $"The page footer is {page.FooterHeightMm}mm but the bottom margin is only " +
                    $"{page.Margins.BottomMm}mm. Increase the bottom margin, or the footer will be cut off.");

            if (!string.IsNullOrWhiteSpace(page.Culture))
            {
                try
                {
                    System.Globalization.CultureInfo.GetCultureInfo(page.Culture);
                }
                catch (System.Globalization.CultureNotFoundException)
                {
                    result.Warn("page.culture",
                        $"Culture \"{page.Culture}\" is not available on the server; " +
                        "formatting will fall back to invariant.");
                }
            }
        }

        private static void ValidateParameters(ReportDefinition definition, ValidationResult result)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < definition.Parameters.Count; i++)
            {
                var parameter = definition.Parameters[i];
                var path = $"parameters[{i}]";

                if (string.IsNullOrWhiteSpace(parameter.Name))
                {
                    result.Error(path, "A parameter needs a name.");
                    continue;
                }

                // Parameter names become identifiers in expressions, so they have to look like one.
                if (!IdentifierPattern.IsMatch(parameter.Name))
                    result.Error(path,
                        $"\"{parameter.Name}\" is not a usable parameter name. Use letters, digits and " +
                        "underscores, starting with a letter.");

                if (!seen.Add(parameter.Name))
                    result.Error(path, $"Duplicate parameter name \"{parameter.Name}\".");

                if (parameter.Type is ParameterType.Select or ParameterType.MultiSelect)
                {
                    if (parameter.LookupTableId is null)
                        result.Error(path,
                            $"\"{parameter.EffectiveLabel}\" is a {parameter.Type} but has no lookup table id.");
                    else if (string.IsNullOrWhiteSpace(parameter.ValueField))
                        result.Error(path,
                            $"\"{parameter.EffectiveLabel}\" needs a value field from its lookup table.");
                }

                if (parameter is { Required: true, Hidden: true, DefaultValue: null or "" })
                    result.Error(path,
                        $"\"{parameter.EffectiveLabel}\" is hidden and required but has no default, " +
                        "so the report can never run.");
            }
        }

        private static void ValidateDataSets(ReportDefinition definition, ValidationResult result)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < definition.DataSets.Count; i++)
            {
                var dataSet = definition.DataSets[i];
                var path = $"dataSets[{i}]";

                if (string.IsNullOrWhiteSpace(dataSet.Key))
                {
                    result.Error(path, "A data set needs a key.");
                    continue;
                }

                if (!seen.Add(dataSet.Key))
                    result.Error(path, $"Duplicate data set key \"{dataSet.Key}\".");

                if (dataSet.SourceKind == DataSourceKind.OracleTableId && dataSet.TableId is null)
                    result.Error(path,
                        $"Data set \"{dataSet.Key}\" reads from Oracle but has no table id selected.");

                foreach (var input in dataSet.Inputs)
                {
                    if (string.IsNullOrWhiteSpace(input.Name))
                        result.Error(path, $"An input of data set \"{dataSet.Key}\" has no name.");

                    if (input.Kind == InputKind.Parameter
                        && definition.FindParameter(input.Value ?? "") is null)
                        result.Error(path,
                            $"Input \"{input.Name}\" of data set \"{dataSet.Key}\" is bound to " +
                            $"parameter \"{input.Value}\", which this report does not declare.");
                }

                foreach (var sort in dataSet.Sort)
                {
                    if (dataSet.Fields.Count > 0 && dataSet.FindField(sort.Field) is null)
                        result.Warn(path,
                            $"Data set \"{dataSet.Key}\" sorts by \"{sort.Field}\", which is not in its " +
                            "known fields.");
                }
            }
        }

        private void ValidateBands(ReportDefinition definition, ValidationResult result)
        {
            var byKind = definition.Bands.GroupBy(b => b.Kind);
            foreach (var duplicate in byKind.Where(g => g.Count() > 1))
                result.Error("bands", $"There can only be one {duplicate.Key} band.");

            for (var i = 0; i < definition.Bands.Count; i++)
            {
                var band = definition.Bands[i];
                var path = $"bands[{i}]";

                if (band.HeightMm < 0) result.Error(path, "Band height cannot be negative.");

                ValidateStyle(band.Style, path, result);
                ValidateRules(band.Rules, path, result, allowFields: false);

                foreach (var element in band.Elements)
                    ValidateElement(definition, band, element, $"{path}.elements[{element.Id}]", result);
            }

            if (definition.AllTables().Any() && definition.FindBand(BandKind.Detail) is null)
                result.Error("bands", "A table has to live on a Detail band.");
        }

        private void ValidateElement(
            ReportDefinition definition, BandDef band, ReportElement element, string path, ValidationResult result)
        {
            if (element.WidthMm <= 0 || element.HeightMm <= 0)
                result.Error(path, "An element needs a positive width and height.");

            if (element.XMm < 0 || element.YMm < 0)
                result.Error(path, "An element cannot sit outside its band.");

            var contentWidth = definition.Page.ContentWidthMm;
            if (element.XMm + element.WidthMm > contentWidth + 0.5)
                result.Warn(path,
                    $"This element runs {element.XMm + element.WidthMm - contentWidth:0.#}mm past the " +
                    "printable width and will be clipped.");

            ValidateStyle(element.Style, path, result);
            ValidateRules(element.Rules, path, result, allowFields: true);

            switch (element)
            {
                case PageNumberElement when !band.IsPageBand:
                    // Chromium only substitutes page numbers inside its header and footer templates,
                    // so this would otherwise print the literal text "{PageNumber}".
                    result.Error(path,
                        "Page numbers only work on a Page Header or Page Footer band. Move this element, " +
                        "or the page number will print as literal text.");
                    break;

                case TextElement text when TokenInterpolator.ContainsPageToken(text.Text) && !band.IsPageBand:
                    result.Error(path,
                        "{PageNumber} and {TotalPages} only resolve on a Page Header or Page Footer band.");
                    break;

                case FieldElement field:
                    ValidateFieldReference(definition, field.DataSetKey, field.Field, path, result);
                    if (string.IsNullOrWhiteSpace(field.Field) && string.IsNullOrWhiteSpace(field.Expression))
                        result.Error(path, "A field element needs either a field or an expression.");
                    ValidateExpression(field.Expression, $"{path}.expression", result);
                    break;

                case ImageElement image
                    when string.IsNullOrWhiteSpace(image.Source) && string.IsNullOrWhiteSpace(image.Field):
                    result.Error(path, "An image element needs either a source or a bound field.");
                    break;

                case TableElement table:
                    ValidateTable(definition, table, path, result);
                    break;
            }
        }

        private void ValidateTable(
            ReportDefinition definition, TableElement table, string path, ValidationResult result)
        {
            var dataSet = definition.FindDataSet(table.DataSetKey);
            if (dataSet is null)
            {
                result.Error(path, string.IsNullOrWhiteSpace(table.DataSetKey)
                    ? "A table needs a data set."
                    : $"Table is bound to data set \"{table.DataSetKey}\", which does not exist.");
                return;
            }

            if (table.Columns.Count == 0)
                result.Error(path, "A table needs at least one column.");

            // A table carries several styles of its own beyond ReportElement.Style, and every one of
            // them ends up in a CSS declaration, so all of them need checking.
            ValidateStyle(table.HeaderStyle, $"{path}.headerStyle", result);
            ValidateStyle(table.RowStyle, $"{path}.rowStyle", result);
            ValidateStyle(table.AlternateRowStyle, $"{path}.alternateRowStyle", result);
            ValidateStyle(table.GrandTotalStyle, $"{path}.grandTotalStyle", result);

            if (table.VisibleColumns.Count() == 0 && table.Columns.Count > 0)
                result.Warn(path, "Every column on this table is hidden.");

            var totalPercent = table.Columns.Where(c => c.Visible).Sum(c => c.WidthPercent ?? 0);
            if (totalPercent > 100.5)
                result.Warn(path,
                    $"Column widths add up to {totalPercent:0.#}%, so the table will overflow its width.");

            for (var i = 0; i < table.Columns.Count; i++)
            {
                var column = table.Columns[i];
                var columnPath = $"{path}.columns[{i}]";

                if (string.IsNullOrWhiteSpace(column.Field) && string.IsNullOrWhiteSpace(column.Expression))
                    result.Error(columnPath, "A column needs either a field or an expression.");

                ValidateFieldReference(definition, table.DataSetKey, column.Field, columnPath, result);
                ValidateExpression(column.Expression, $"{columnPath}.expression", result);
                ValidateStyle(column.Style, columnPath, result);
                ValidateStyle(column.HeaderStyle, $"{columnPath}.headerStyle", result);
                ValidateRules(column.Rules, columnPath, result, allowFields: true);
            }

            ValidateRules(table.RowRules, $"{path}.rowRules", result, allowFields: true);

            var keepTogetherLevels = table.Groups
                .Select((g, index) => (g, index))
                .Where(x => x.g.KeepTogether)
                .Select(x => x.index)
                .ToList();

            if (keepTogetherLevels.Count > 1)
                result.Warn(path,
                    "Keep-together is set on more than one group level. Only the outermost " +
                    $"(level {keepTogetherLevels[0] + 1}) can be honoured, because the renderer implements " +
                    "it with a table row group and those cannot nest.");

            for (var level = 0; level < table.Groups.Count; level++)
            {
                var group = table.Groups[level];
                var groupPath = $"{path}.groups[{level}]";

                if (string.IsNullOrWhiteSpace(group.Field) && string.IsNullOrWhiteSpace(group.Expression))
                    result.Error(groupPath, $"Group level {level + 1} needs either a field or an expression.");

                ValidateFieldReference(definition, table.DataSetKey, group.Field, groupPath, result);
                ValidateExpression(group.Expression, $"{groupPath}.expression", result);
                ValidateRules(group.Rules, groupPath, result, allowFields: true);
                ValidateStyle(group.HeaderStyle, $"{groupPath}.headerStyle", result);
                ValidateStyle(group.FooterStyle, $"{groupPath}.footerStyle", result);

                foreach (var aggregate in group.Aggregates.Concat(group.HeaderAggregates))
                    ValidateAggregate(definition, table, aggregate, groupPath, result);
            }

            foreach (var aggregate in table.GrandTotals)
                ValidateAggregate(definition, table, aggregate, $"{path}.grandTotals", result);
        }

        private static void ValidateAggregate(
            ReportDefinition definition, TableElement table, AggregateDef aggregate, string path, ValidationResult result)
        {
            ValidateStyle(aggregate.Style, $"{path}.aggregate", result);

            if (aggregate.Function != AggregateFunction.Count && string.IsNullOrWhiteSpace(aggregate.Field))
            {
                result.Error(path, $"{aggregate.Function} needs a field to aggregate.");
                return;
            }

            var dataSet = definition.FindDataSet(table.DataSetKey);
            if (dataSet is null || dataSet.Fields.Count == 0 || string.IsNullOrWhiteSpace(aggregate.Field)) return;

            var field = dataSet.FindField(aggregate.Field);
            if (field is null)
            {
                result.Warn(path,
                    $"{aggregate.Function} is over \"{aggregate.Field}\", which is not a known field of " +
                    $"data set \"{dataSet.Key}\".");
                return;
            }

            // Summing text would silently produce nothing at render time; say so at save time instead.
            if (aggregate.Function is AggregateFunction.Sum or AggregateFunction.Avg && !field.IsNumeric)
                result.Error(path,
                    $"{aggregate.Function} needs a numeric field, but \"{field.Name}\" is {field.DataType}.");

            if (aggregate.TargetColumn is { Length: > 0 }
                && table.Columns.All(c => !string.Equals(c.ColumnKey, aggregate.TargetColumn, StringComparison.OrdinalIgnoreCase)))
                result.Warn(path,
                    $"This total targets column \"{aggregate.TargetColumn}\", which is not on the table, " +
                    "so it will not line up under anything.");
        }

        private static void ValidateFieldReference(
            ReportDefinition definition, string? dataSetKey, string? field, string path, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(field)) return;

            var dataSet = definition.FindDataSet(dataSetKey);
            // No cached metadata means the catalogue has not been consulted yet, which is not an error.
            if (dataSet is null || dataSet.Fields.Count == 0) return;

            if (dataSet.FindField(field) is null)
                result.Warn(path,
                    $"\"{field}\" is not a known field of data set \"{dataSet.Key}\". " +
                    "It will render empty unless the data source actually returns it.");
        }

        private void ValidateRules(
            List<ConditionalRuleDef> rules, string path, ValidationResult result, bool allowFields)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var rulePath = $"{path}.rules[{i}]";

                if (string.IsNullOrWhiteSpace(rule.Expression))
                {
                    result.Error(rulePath, "A conditional rule needs an expression.");
                    continue;
                }

                if (!_evaluator.TryValidateSyntax(rule.Expression, out var error))
                    result.Error(rulePath, $"Rule expression does not compile: {error}");

                // A band is evaluated once for the whole report, so it has no current row. A rule
                // reading Fields there would always see nulls, which looks like a data problem.
                if (!allowFields && rule.Expression.Contains("Fields.", StringComparison.OrdinalIgnoreCase))
                    result.Error(rulePath,
                        "This rule is on a band, which is evaluated once per report and has no current " +
                        "row, so Fields is always empty here. Use Params or Report instead.");

                ValidateStyle(rule.Actions.ToStyle(), rulePath, result);
            }
        }

        private void ValidateExpression(string? expression, string path, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(expression)) return;
            if (!_evaluator.TryValidateSyntax(expression, out var error))
                result.Error(path, $"Expression does not compile: {error}");
        }

        private static void ValidateStyle(StyleDef? style, string path, ValidationResult result)
        {
            if (style is null) return;

            // Colours go straight into a CSS declaration, so anything unrecognised is refused rather
            // than emitted, which also closes off style-based CSS injection.
            CheckColor(style.Color, $"{path}.style.color", result);
            CheckColor(style.BackColor, $"{path}.style.backColor", result);

            if (style.FontSizePt is <= 0 or > 400)
                result.Error($"{path}.style.fontSizePt", "Font size must be between 0 and 400pt.");

            foreach (var side in new[] { style.Border?.All, style.Border?.Top, style.Border?.Right,
                                         style.Border?.Bottom, style.Border?.Left })
            {
                if (side is null) continue;
                CheckColor(side.Color, $"{path}.style.border.color", result);
                if (side.WidthPt < 0) result.Error($"{path}.style.border.widthPt", "Border width cannot be negative.");
            }
        }

        private static void CheckColor(string? color, string path, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(color)) return;
            if (!ColorPattern.IsMatch(color.Trim()))
                result.Error(path, $"\"{color}\" is not a valid colour. Use #RGB, #RRGGBB, rgb(...) or a colour name.");
        }
    }
}
