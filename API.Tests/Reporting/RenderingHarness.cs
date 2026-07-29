using API.Data;
using API.Entities;
using API.Reporting.Configuration;
using API.Reporting.Data;
using API.Reporting.Rendering;
using API.Reporting.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace API.Tests.Reporting;

/// <summary>
/// Builds a real service graph over an in-memory SQLite database so rendering tests exercise the
/// production code paths — the actual runtime, the actual sample provider, the actual renderer —
/// rather than mocks standing in for them.
/// </summary>
internal sealed class RenderingHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    public RenderingHarness(int productCount = 40, Action<ReportingOptions>? configure = null)
    {
        // A shared in-memory connection kept open for the harness's lifetime: SQLite drops an in-memory
        // database as soon as the last connection closes.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<StoreContext>(opt => opt.UseSqlite(_connection));

        var options = new ReportingOptions
        {
            Chromium = new ReportingOptions.ChromiumOptions
            {
                ExecutablePath = ChromiumPath,
                MaxConcurrentRenders = 2
            }
        };
        configure?.Invoke(options);

        services.AddSingleton<IOptions<ReportingOptions>>(new OptionsWrapper<ReportingOptions>(options));
        services.AddSingleton<ChromiumPdfService>();
        services.AddSingleton<FontProvider>();
        services.AddSingleton<ExpressionEvaluator>();
        services.AddSingleton<ReportDefinitionValidator>();
        services.AddScoped<ReportRuntime>();
        services.AddScoped<SampleDataProvider>();
        services.AddScoped<IReportDataProvider>(sp => sp.GetRequiredService<SampleDataProvider>());

        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StoreContext>();
        context.Database.EnsureCreated();
        Seed(context, productCount);
    }

    /// <summary>
    /// The browser this environment already provides. Tests that need Chromium skip themselves rather
    /// than fail when it is absent, so the suite stays runnable on a machine without one.
    /// </summary>
    public static string? ChromiumPath => new[]
    {
        "/opt/pw-browsers/chromium",
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser",
        "/usr/bin/google-chrome"
    }.FirstOrDefault(File.Exists);

    public static bool ChromiumAvailable => ChromiumPath is not null;

    public IServiceScope CreateScope() => _services.CreateScope();

    public ReportRuntime Runtime(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ReportRuntime>();

    /// <summary>
    /// Enough products across several brands and types that grouping has real work to do and the report
    /// spills onto more than one page.
    /// </summary>
    private static void Seed(StoreContext context, int count)
    {
        if (context.Products.Any()) return;

        var brands = new[] { "Adidas", "Nike", "Puma" };
        var types = new[] { "Boots", "Gloves", "Hats" };

        var products = Enumerable.Range(1, count).Select(i => new Product
        {
            Name = $"Product {i:D3}",
            Description = $"Description for product {i}",
            // Minor units, matching the catalogue convention; every third item is expensive enough to
            // trip the high-value conditional rule.
            Price = i % 3 == 0 ? 90000 + i * 100 : 1500 + i * 25,
            PictureUrl = "/images/placeholder.png",
            Brand = brands[i % brands.Length],
            Type = types[(i / brands.Length) % types.Length],
            QuantityInStock = i % 7 == 0 ? 0 : 10 + i
        }).ToList();

        context.Products.AddRange(products);
        context.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>Skips a test when no Chromium binary is present, instead of failing it.</summary>
public sealed class RequiresChromiumFactAttribute : FactAttribute
{
    public RequiresChromiumFactAttribute()
    {
        if (!RenderingHarness.ChromiumAvailable)
            Skip = "No Chromium binary found; set Reporting:Chromium:ExecutablePath to run this test.";
    }
}
