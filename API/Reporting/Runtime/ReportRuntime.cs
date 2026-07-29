#nullable enable
using API.Reporting.Configuration;
using API.Reporting.Data;
using API.Reporting.Model;
using API.Reporting.Rendering;
using Microsoft.Extensions.Options;

namespace API.Reporting.Runtime
{
    /// <summary>What the caller asked for on a generate or preview request.</summary>
    public sealed class ReportRequest
    {
        public IReadOnlyDictionary<string, object?>? Parameters { get; init; }

        /// <summary>Rows supplied inline. Any key here overrides that data set's bound source.</summary>
        public PushedDataProvider.Payload PushedData { get; init; } = PushedDataProvider.Payload.Empty;

        /// <summary>Per-call override of the report's own direction, for one template serving two languages.</summary>
        public TextDirection? Direction { get; init; }

        public string? Culture { get; init; }

        public string? UserName { get; init; }
        public IReadOnlyList<string> UserRoles { get; init; } = Array.Empty<string>();
    }

    public sealed class ReportRenderResult
    {
        public required byte[] Pdf { get; init; }
        public int PageCount { get; init; }
        public long ElapsedMs { get; init; }
        public IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; } = Array.Empty<RenderDiagnostic>();
        public bool AnyDataTruncated { get; init; }
    }

    /// <summary>Thrown when a request cannot be honoured for a reason the caller can fix.</summary>
    public sealed class ReportRequestException : Exception
    {
        public ReportRequestException(IEnumerable<ParameterError> errors)
            : base("The report request was not valid.") => Errors = errors.ToList();

        public ReportRequestException(string parameter, string message)
            : base(message) => Errors = new List<ParameterError> { new(parameter, message) };

        public IReadOnlyList<ParameterError> Errors { get; }
    }

    /// <summary>
    /// Runs a report: validate parameters, resolve each data set through its provider, group and
    /// aggregate, render HTML, print to PDF.
    ///
    /// The order matters. Parameters are validated before any provider is touched, so a bad request
    /// costs nothing — no Oracle call, no Chromium page.
    /// </summary>
    public sealed class ReportRuntime
    {
        private readonly IServiceProvider _services;
        private readonly ChromiumPdfService _chromium;
        private readonly FontProvider _fonts;
        private readonly ReportDefinitionValidator _validator;
        private readonly ReportingOptions _options;
        private readonly ILogger<ReportRuntime> _logger;

        public ReportRuntime(
            IServiceProvider services,
            ChromiumPdfService chromium,
            FontProvider fonts,
            ReportDefinitionValidator validator,
            IOptions<ReportingOptions> options,
            ILogger<ReportRuntime> logger)
        {
            _services = services;
            _chromium = chromium;
            _fonts = fonts;
            _validator = validator;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<ReportRenderResult> RenderAsync(
            ReportDefinition definition,
            ReportRequest request,
            CancellationToken cancellationToken)
        {
            var validation = _validator.Validate(definition);
            if (!validation.IsValid)
                throw new ReportRequestException(validation.Errors
                    .Select(e => new ParameterError(e.Path, e.Message)));

            var effective = ApplyOverrides(definition, request);
            var diagnostics = new RenderDiagnostics();

            // A single budget covering data fetch and rendering together, so a slow query plus a slow
            // render cannot add up past what the caller is willing to wait.
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(_options.Limits.TotalTimeoutSeconds));

            var parameters = BindParameters(effective, request);
            var scope = BuildScope(effective, parameters, request);

            var data = await ResolveDataSetsAsync(effective, scope, request, diagnostics, budget.Token);

            // Row count is only knowable once the data is in, so the report context is rebuilt with it.
            var totalRows = data.Values.Sum(d => d.Data.Rows.Count);
            scope = new ExpressionScope
            {
                Params = scope.Params,
                Fields = scope.Fields,
                Group = scope.Group,
                DeclaredTypes = scope.DeclaredTypes,
                Report = new ReportContext
                {
                    Name = effective.Name,
                    Title = effective.EffectiveTitle,
                    RunAt = DateTime.Now,
                    RowCount = totalRows,
                    User = new UserContext { Name = request.UserName ?? "", Roles = request.UserRoles }
                }
            };

            var renderer = new HtmlReportRenderer(_fonts);
            var html = renderer.Render(effective, data, scope, diagnostics);

            var pdf = await _chromium.RenderAsync(html, effective.Page, budget.Token);

            if (diagnostics.HasAny)
                _logger.LogInformation("Report {Name} rendered with diagnostics: {Diagnostics}",
                    effective.Name, diagnostics.ToString());

            return new ReportRenderResult
            {
                Pdf = pdf.Bytes,
                PageCount = pdf.PageCount,
                ElapsedMs = pdf.ElapsedMs,
                Diagnostics = diagnostics.Items,
                AnyDataTruncated = data.Values.Any(d => d.Data.Truncated)
            };
        }

        /// <summary>
        /// Renders the HTML without printing it. Used by tests, and useful for diagnosing a layout
        /// problem without a browser in the loop.
        /// </summary>
        public async Task<RenderedHtml> RenderHtmlAsync(
            ReportDefinition definition,
            ReportRequest request,
            RenderDiagnostics diagnostics,
            CancellationToken cancellationToken)
        {
            var effective = ApplyOverrides(definition, request);
            var parameters = BindParameters(effective, request);
            var scope = BuildScope(effective, parameters, request);
            var data = await ResolveDataSetsAsync(effective, scope, request, diagnostics, cancellationToken);

            return new HtmlReportRenderer(_fonts).Render(effective, data, scope, diagnostics);
        }

        /// <summary>
        /// Applies per-call direction and culture. The definition is copied rather than mutated: it may
        /// be a cached instance shared across concurrent requests, and two callers rendering the same
        /// template in different languages must not interfere with each other.
        /// </summary>
        private static ReportDefinition ApplyOverrides(ReportDefinition definition, ReportRequest request)
        {
            if (request.Direction is null && string.IsNullOrWhiteSpace(request.Culture)) return definition;

            var copy = ReportJson.Deserialize(ReportJson.Serialize(definition));
            if (request.Direction is not null) copy.Page.Direction = request.Direction.Value;
            if (!string.IsNullOrWhiteSpace(request.Culture)) copy.Page.Culture = request.Culture!;
            return copy;
        }

        private static Dictionary<string, object?> BindParameters(ReportDefinition definition, ReportRequest request)
        {
            var binder = new ParameterBinder();
            var bound = binder.Bind(definition.Parameters, request.Parameters, request.UserName);

            if (!bound.IsValid) throw new ReportRequestException(bound.Errors);
            return bound.Values;
        }

        private static ExpressionScope BuildScope(
            ReportDefinition definition,
            Dictionary<string, object?> parameters,
            ReportRequest request)
        {
            var types = TypeMap.Create().AddParameters(definition.Parameters);

            // Field types from every data set share one map. Keys are scoped by name, so two data sets
            // declaring the same field name with different types would collide — worth knowing, and
            // harmless in practice because a table only ever reads its own data set.
            foreach (var dataSet in definition.DataSets) types.AddFields(dataSet.Fields);

            foreach (var table in definition.AllTables())
            {
                var dataSet = definition.FindDataSet(table.DataSetKey);
                FieldMeta? Resolve(string name) => dataSet?.FindField(name);

                types.AddAggregates(table.GrandTotals, Resolve);
                foreach (var group in table.Groups)
                    types.AddAggregates(group.Aggregates.Concat(group.HeaderAggregates), Resolve);
            }

            return new ExpressionScope
            {
                Params = parameters,
                DeclaredTypes = types,
                Report = new ReportContext
                {
                    Name = definition.Name,
                    Title = definition.EffectiveTitle,
                    RunAt = DateTime.Now,
                    User = new UserContext { Name = request.UserName ?? "", Roles = request.UserRoles }
                }
            };
        }

        private async Task<Dictionary<string, RenderedDataSet>> ResolveDataSetsAsync(
            ReportDefinition definition,
            ExpressionScope scope,
            ReportRequest request,
            RenderDiagnostics diagnostics,
            CancellationToken cancellationToken)
        {
            var evaluator = new ExpressionEvaluator();
            var formatter = new ValueFormatter(definition.Page.Culture);
            var builder = new GroupTreeBuilder(evaluator, formatter);

            var result = new Dictionary<string, RenderedDataSet>(StringComparer.OrdinalIgnoreCase);

            // Pushed keys that no data set declares are rejected: a caller who misspells a key would
            // otherwise get an empty report with no explanation.
            foreach (var key in request.PushedData.Keys)
            {
                if (definition.FindDataSet(key) is null)
                    throw new ReportRequestException($"data.{key}",
                        $"This report has no data set called \"{key}\".");
            }

            foreach (var dataSet in definition.DataSets)
            {
                var pushed = request.PushedData.Has(dataSet.Key);
                var provider = ResolveProvider(dataSet, pushed, request);

                var inputs = EvaluateInputs(dataSet, scope, evaluator, diagnostics);
                var maxRows = dataSet.MaxRows ?? _options.Limits.MaxRowsPerDataSet;

                var data = await provider.GetDataAsync(dataSet, inputs, maxRows, cancellationToken);

                if (data.Truncated)
                    diagnostics.Add($"dataSets.{dataSet.Key}",
                        $"Only the first {maxRows} rows were used; the data source returned more.");

                result[dataSet.Key] = new RenderedDataSet(data, GroupFor(definition, dataSet, data, builder, scope, diagnostics));
            }

            return result;
        }

        /// <summary>
        /// Groups a data set according to the first table bound to it. Grouping belongs to the table
        /// rather than the data set, so two tables over the same data could group differently — this
        /// resolves the common case and leaves that refinement for when a template needs it.
        /// </summary>
        private static GroupedRows GroupFor(
            ReportDefinition definition,
            DataSetDef dataSet,
            ReportDataSet data,
            GroupTreeBuilder builder,
            ExpressionScope scope,
            RenderDiagnostics diagnostics)
        {
            var table = definition.AllTables()
                .FirstOrDefault(t => string.Equals(t.DataSetKey, dataSet.Key, StringComparison.OrdinalIgnoreCase));

            if (table is null)
                return new GroupedRows { AllRows = data.Rows.ToList() };

            return builder.Build(
                data.Rows,
                table.Groups,
                dataSet.Sort,
                table.GrandTotals,
                scope,
                diagnostics,
                $"dataSets.{dataSet.Key}");
        }

        /// <summary>
        /// Pushed data always wins over the data set's declared source. That precedence is the whole
        /// mechanism behind "same template, either mode".
        /// </summary>
        private IReportDataProvider ResolveProvider(DataSetDef dataSet, bool hasPushedData, ReportRequest request)
        {
            if (hasPushedData) return new PushedDataProvider(request.PushedData);

            var providers = _services.GetServices<IReportDataProvider>().ToList();
            var match = providers.FirstOrDefault(p => p.Kind == dataSet.SourceKind);

            if (match is not null) return match;

            throw new ReportRequestException($"dataSets.{dataSet.Key}",
                $"Data set \"{dataSet.Key}\" needs a {dataSet.SourceKind} data source, which is not " +
                "configured on this server.");
        }

        /// <summary>
        /// Turns declared inputs into concrete values. Providers receive plain values only, so no
        /// implementation has to know about parameters or expressions.
        /// </summary>
        private static Dictionary<string, object?> EvaluateInputs(
            DataSetDef dataSet,
            ExpressionScope scope,
            ExpressionEvaluator evaluator,
            RenderDiagnostics diagnostics)
        {
            var inputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var input in dataSet.Inputs)
            {
                inputs[input.Name] = input.Kind switch
                {
                    InputKind.Parameter => scope.Params.TryGetValue(input.Value ?? "", out var p) ? p : null,
                    InputKind.Expression => evaluator.EvaluateValue(input.Value, scope, diagnostics,
                        $"dataSets.{dataSet.Key}.inputs.{input.Name}"),
                    _ => input.Value
                };
            }

            return inputs;
        }
    }
}
