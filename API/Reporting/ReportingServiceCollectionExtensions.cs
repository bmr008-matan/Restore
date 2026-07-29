#nullable enable
using API.Reporting.Configuration;
using API.Reporting.Data;
using API.Reporting.Rendering;
using API.Reporting.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace API.Reporting
{
    /// <summary>
    /// Wires up the reporting subsystem. Kept out of Program.cs so the feature can be added or removed
    /// as a unit, and so the lifetime choices are all visible together.
    /// </summary>
    public static class ReportingServiceCollectionExtensions
    {
        public static IServiceCollection AddReporting(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection(ReportingOptions.SectionName);
            services.Configure<ReportingOptions>(section);

            var options = section.Get<ReportingOptions>() ?? new ReportingOptions();

            // Chromium is a singleton because launching a browser costs hundreds of milliseconds; doing
            // it per request would dominate the cost of every report.
            services.AddSingleton<ChromiumPdfService>();

            // Fonts are read from disk and base64-encoded once, then cached for the process lifetime.
            services.AddSingleton<FontProvider>();

            // The evaluator caches compiled expressions across requests, which is most of its value, and
            // its caches are concurrent collections.
            services.AddSingleton<ExpressionEvaluator>();
            services.AddSingleton<ReportDefinitionValidator>();

            // Scoped: the runtime resolves data providers, which need the request's DbContext.
            services.AddScoped<ReportRuntime>();

            services.AddScoped<SampleDataProvider>();
            services.AddScoped<IReportDataProvider>(sp => sp.GetRequiredService<SampleDataProvider>());

            // Registered only when a connection string exists. Without one, a report bound to an Oracle
            // table id fails with "this data source is not configured on this server" — a clear message —
            // rather than a connection error from deep inside the provider.
            if (OracleRefCursorDataProvider.IsConfigured(options))
            {
                services.AddScoped<OracleRefCursorDataProvider>();
                services.AddScoped<IReportDataProvider>(sp => sp.GetRequiredService<OracleRefCursorDataProvider>());
            }

            // Both take a DbContext, so both follow the request scope.
            services.AddScoped<ReportTemplateStore>();
            services.AddScoped<ReportTableSourceCatalog>();

            // Injected rather than read statically, so tests can pin the clock.
            services.TryAddSingleton(TimeProvider.System);

            return services;
        }
    }
}
