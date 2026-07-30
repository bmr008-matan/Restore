using System.Net;
using API.Data;
using API.Reporting.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace API.Tests.Reporting;

/// <summary>
/// The API serves the built React client from its own wwwroot, so one IIS site hosts both. That makes
/// CORS unnecessary and means ASP.NET Core, not the IIS URL Rewrite module, handles SPA deep links.
///
/// The fallback route carries a regex excluding api/ and swagger/, and that exclusion is the part worth
/// testing: without it every mistyped API path would return the SPA shell with a 200 instead of a 404,
/// which is very hard to diagnose from a client. IIS itself cannot be exercised here, but this routing
/// is the same in Kestrel and behind the ASP.NET Core Module, so it is worth pinning.
/// </summary>
public class SpaHostingTests : IAsyncLifetime
{
    private const string ShellMarker = "<!-- spa-shell-marker -->";

    private SpaHostFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new SpaHostFactory(ShellMarker);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // Client-side routes are served the shell directly; no redirect is involved.
            AllowAutoRedirect = false
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/reports")]
    [InlineData("/reports/design/1")]
    [InlineData("/catalog/42")]
    public async Task ClientRoutes_AreServedTheSpaShell(string path)
    {
        var response = await _client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(ShellMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApiRoutes_StillReturnJson_NotTheShell()
    {
        var response = await _client.GetAsync("/api/report-templates");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ShellMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownApiRoutes_Return404_RatherThanTheShellWithA200()
    {
        // The whole point of the regex in the fallback route. A bare MapFallbackToFile would answer this
        // with index.html and a 200, so a client calling a mistyped endpoint would parse HTML as JSON.
        var response = await _client.GetAsync("/api/no-such-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(ShellMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownApiRoutes_UnderAKnownController_AlsoReturn404()
    {
        var response = await _client.GetAsync("/api/reports/STOCK_BY_BRAND/not-a-real-action");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(ShellMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMissingReport_StillReturnsItsOwn404()
    {
        // This one is routed and handled by the controller; the fallback must not intercept it.
        var response = await _client.GetAsync("/api/reports/NO_SUCH_REPORT/parameters");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(ShellMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SwaggerIsNotSwallowedByTheFallback()
    {
        var document = await _client.GetAsync("/swagger/v1/swagger.json");

        document.EnsureSuccessStatusCode();
        Assert.DoesNotContain(ShellMarker, await document.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StaticAssets_AreServedFromWwwroot()
    {
        var response = await _client.GetAsync("/static/app.js");

        response.EnsureSuccessStatusCode();
        Assert.Contains("the-real-bundle", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NoCorsHeaders_WhenNoOriginsAreConfigured()
    {
        // Same-origin hosting: the CORS middleware should not even be registered, so a cross-origin
        // request gets no allow header rather than a permissive one.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/report-templates");
        request.Headers.Add("Origin", "http://evil.example");

        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

/// <summary>
/// Hosts the app with a temporary web root containing a recognisable stand-in for the built client, so
/// SPA routing can be tested without needing an actual `npm run build`.
/// </summary>
internal sealed class SpaHostFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _webRoot;

    public SpaHostFactory(string shellMarker)
    {
        _webRoot = Path.Combine(Path.GetTempPath(), $"spa-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_webRoot, "static"));

        File.WriteAllText(Path.Combine(_webRoot, "index.html"),
            $"<!doctype html><html><head><title>ReStore</title></head><body>{shellMarker}<div id=\"root\"></div></body></html>");

        File.WriteAllText(Path.Combine(_webRoot, "static", "app.js"), "console.log('the-real-bundle');");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseWebRoot(_webRoot);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<StoreContext>>();
            services.RemoveAll<StoreContext>();

            _connection.Open();
            services.AddDbContext<StoreContext>(opt => opt.UseSqlite(_connection));

            services.Configure<ReportingOptions>(options =>
            {
                options.Chromium.ExecutablePath = RenderingHarness.ChromiumPath;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        _connection.Dispose();
        try
        {
            Directory.Delete(_webRoot, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }
}
