using API.Data;
using API.Reporting;
using API.Reporting.Configuration;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Microsoft.OpenApi v2, which Swashbuckle 10 ships, flattened these out of the .Models namespace.
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "ReStore API",
        Version = "v1",
        Description =
            "Catalogue and reporting API.\n\n" +
            "Reporting: create a template with POST /api/report-templates to get its id, discover what " +
            "it needs with GET /api/reports/{idOrCode}/parameters, then POST " +
            "/api/reports/{idOrCode}/generate. That endpoint accepts parameters for the server to " +
            "resolve, or the rows themselves under \"data\", or both. Either an id or a template code " +
            "works wherever {idOrCode} appears."
    });

    // Without this the polymorphic report elements are documented as a bare base type.
    options.AddReportingPolymorphism();
});
builder.Services.AddCors();

// Template storage runs on either provider from the same entities and the same migration code: SQLite
// locally and in CI so the designer works with no Oracle instance, Oracle in production so there is one
// database. Selected by Reporting:Database:Provider.
builder.Services.AddDbContext<StoreContext>(opt =>
{
    var provider = builder.Configuration["Reporting:Database:Provider"] ?? "Sqlite";
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    if (string.Equals(provider, "Oracle", StringComparison.OrdinalIgnoreCase))
        opt.UseOracle(connectionString);
    else
        opt.UseSqlite(connectionString);
});

builder.Services.AddReporting(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// The built React client is served from wwwroot, so one site hosts both and CORS is unnecessary. It is
// only present once the client has been built (the publish target does this, see DEPLOY-IIS.md), and
// the API has to run without it — a fresh clone has no wwwroot, and static file hosting throws rather
// than no-ops when the directory is missing.
var webRootPath = app.Environment.WebRootPath;
var clientIsPresent = !string.IsNullOrEmpty(webRootPath)
                      && File.Exists(Path.Combine(webRootPath, "index.html"));

if (clientIsPresent)
{
    // These must come before UseRouting, and UseRouting has to be called explicitly here rather than
    // left to the minimal-hosting default. StaticFileMiddleware deliberately stands down when routing
    // has already selected an endpoint, and the SPA fallback below is a catch-all matching every asset
    // path. With the automatic UseRouting — inserted ahead of all user middleware — every request for a
    // .js or .css file matched the fallback first and came back as index.html, which loads a blank page
    // with a MIME-type error and no obvious cause.
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
else
{
    app.Logger.LogInformation(
        "No client build found in wwwroot, so this instance serves the API only. Run 'npm run build' in " +
        "client/ and copy build/ into API/wwwroot, or publish with the BuildClient target.");
}

app.UseRouting();

// CORS is only needed when the client is served from a different origin. Leave Cors:AllowedOrigins
// empty for the single-site deployment and the middleware is not added at all.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

if (allowedOrigins.Length > 0)
{
    app.UseCors(opt =>
    {
        opt.AllowAnyHeader().AllowAnyMethod().WithOrigins(allowedOrigins)
           // A browser can only read response headers the server explicitly exposes. Without this the
           // reporting metadata headers are invisible to fetch/XHR, so the client sees a page count of
           // zero and cannot tell that a report was truncated.
           .WithExposedHeaders(
               "X-Report-Page-Count",
               "X-Report-Duration-Ms",
               "X-Report-Truncated",
               "X-Report-Diagnostics",
               "Content-Disposition");
    });
}

// app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Client-side routes such as /reports/design/1 are not files on disk, so they are served the SPA shell
// and React Router takes over.
//
// The regex is the important part. A bare MapFallbackToFile would also answer /api/nonexistent with
// index.html and a 200, turning every mistyped endpoint into a silent HTML response instead of a 404 —
// which is maddening to debug from a client. Excluding api/ and swagger/ keeps those returning what
// they should.
if (clientIsPresent)
{
    app.MapFallbackToFile("{*path:regex(^(?!api/|swagger/).*$)}", "index.html");
}

var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<StoreContext>();
var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

try
{
    context.Database.Migrate();
    DbInitializer.Initialize(context);
    // Seeds the table-id catalogue and the demo templates. Additive and idempotent: an existing row is
    // never overwritten, so a restart cannot discard a design someone has edited.
    await API.Reporting.Data.ReportingSeeder.SeedAsync(context, logger);
}
catch (Exception ex)
{
    logger.LogError(ex,"Error in migration");
}


app.Run();

/// <summary>
/// Declared so integration tests can reference this entry point with WebApplicationFactory. Top-level
/// statements otherwise compile to an internal Program class the test project cannot see.
/// </summary>
public partial class Program { }
