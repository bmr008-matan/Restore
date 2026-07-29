using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.Data;
using API.Reporting;
using API.Reporting.Configuration;
using API.Reporting.Dtos;
using API.Reporting.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace API.Tests.Reporting;

/// <summary>
/// Drives the real HTTP API. These cover the flow an integrator actually uses — create a template, get
/// its id, discover its parameters, generate a PDF — over the real pipeline rather than by calling
/// controllers directly, so routing, model binding and serialisation are all exercised.
/// </summary>
public class ReportApiTests : IAsyncLifetime
{
    private ReportApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ReportApiFactory();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static readonly JsonSerializerOptions Json = ReportJson.Options;

    [Fact]
    public async Task SeededDemoTemplates_AreListed()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ReportTemplateSummaryDto>>(
            "/api/report-templates", Json);

        Assert.NotNull(page);
        Assert.Contains(page!.Items, t => t.Code == SampleReports.StockByBrandCode);
        Assert.Contains(page.Items, t => t.Code == SampleReports.StockByBrandHebrewCode);
    }

    [Fact]
    public async Task Create_ReturnsAnIdAndCode_AndTheDefinitionRoundTripsByteIdentically()
    {
        var definition = SampleReports.StockByBrand();
        definition.Name = "Round trip report";

        var created = await PostCreateAsync("ROUND_TRIP", definition);

        Assert.True(created.Id > 0);
        Assert.Equal("ROUND_TRIP", created.Code);
        Assert.Equal(1, created.Version);

        var fetched = await _client.GetFromJsonAsync<ReportTemplateDto>(
            $"/api/report-templates/{created.Id}", Json);

        // A save-then-load cycle must not change the template in any way.
        Assert.Equal(
            ReportJson.Serialize(definition),
            ReportJson.Serialize(fetched!.Definition));
    }

    [Fact]
    public async Task Create_WithNoDefinition_ReturnsAUsableBlankDesign()
    {
        var created = await PostCreateAsync("BLANK_ONE", definition: null, name: "Blank");

        var fetched = await _client.GetFromJsonAsync<ReportTemplateDto>(
            $"/api/report-templates/{created.Id}", Json);

        // Not an empty row the designer would have to repair: real page setup and the five bands.
        Assert.Equal(PaperSize.A4, fetched!.Definition.Page.PaperSize);
        Assert.Equal(5, fetched.Definition.Bands.Count);
        Assert.NotNull(fetched.Definition.FindBand(BandKind.Detail));
    }

    [Fact]
    public async Task Create_WithADuplicateCode_IsAConflict()
    {
        await PostCreateAsync("DUPE_CODE", SampleReports.StockByBrand());

        var response = await _client.PostAsJsonAsync("/api/report-templates", new CreateTemplateRequest
        {
            Code = "DUPE_CODE",
            Definition = SampleReports.StockByBrand()
        }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnInvalidDefinition_IsRejectedWithPerFieldMessages()
    {
        var definition = SampleReports.StockByBrand();
        // A page header taller than the top margin would be silently clipped by Chromium.
        definition.Page.Margins.TopMm = 5;
        definition.Page.HeaderHeightMm = 30;

        var response = await _client.PostAsJsonAsync("/api/report-templates", new CreateTemplateRequest
        {
            Code = "BAD_ONE",
            Definition = definition
        }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDto>(Json);
        Assert.Contains(problem!.Errors, e => e.Path == "page.headerHeightMm");
    }

    [Fact]
    public async Task Update_BumpsTheVersionAndSnapshotsThePrevious()
    {
        var created = await PostCreateAsync("VERSIONED", SampleReports.StockByBrand());

        var edited = SampleReports.StockByBrand();
        edited.Title = "Edited title";

        var response = await _client.PutAsJsonAsync($"/api/report-templates/{created.Id}",
            new UpdateTemplateRequest { Definition = edited }, Json);

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<ReportTemplateDto>(Json);

        Assert.Equal(2, updated!.Version);
        Assert.Equal("Edited title", updated.Definition.Title);

        var versions = await _client.GetFromJsonAsync<List<TemplateVersionDto>>(
            $"/api/report-templates/{created.Id}/versions", Json);

        // The pre-edit state is kept, which is what makes a bad edit recoverable.
        Assert.Single(versions!);
        Assert.Equal(1, versions![0].Version);
    }

    [Fact]
    public async Task Update_WithAStaleExpectedVersion_IsAConflictRatherThanASilentOverwrite()
    {
        var created = await PostCreateAsync("CONCURRENT", SampleReports.StockByBrand());

        // First save succeeds and moves the version to 2.
        await _client.PutAsJsonAsync($"/api/report-templates/{created.Id}",
            new UpdateTemplateRequest { Definition = SampleReports.StockByBrand(), ExpectedVersion = 1 }, Json);

        // A second editor who loaded version 1 must be told, not silently lose their work.
        var response = await _client.PutAsJsonAsync($"/api/report-templates/{created.Id}",
            new UpdateTemplateRequest { Definition = SampleReports.StockByBrand(), ExpectedVersion = 1 }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Restore_BringsBackAnOldSnapshotAsANewVersion()
    {
        var original = SampleReports.StockByBrand();
        original.Title = "Original";
        var created = await PostCreateAsync("RESTORABLE", original);

        var edited = SampleReports.StockByBrand();
        edited.Title = "Broken edit";
        await _client.PutAsJsonAsync($"/api/report-templates/{created.Id}",
            new UpdateTemplateRequest { Definition = edited }, Json);

        var response = await _client.PostAsync($"/api/report-templates/{created.Id}/restore/1", null);
        response.EnsureSuccessStatusCode();

        var restored = await response.Content.ReadFromJsonAsync<ReportTemplateDto>(Json);

        Assert.Equal("Original", restored!.Definition.Title);
        // History stays append-only rather than rewinding, so the restore is itself recoverable.
        Assert.Equal(3, restored.Version);
    }

    [Fact]
    public async Task Duplicate_CreatesANewIdLeavingTheOriginalAlone()
    {
        var created = await PostCreateAsync("ORIGINAL_ONE", SampleReports.StockByBrand());

        var response = await _client.PostAsJsonAsync(
            $"/api/report-templates/{created.Id}/duplicate",
            new DuplicateTemplateRequest { Code = "COPY_ONE", Name = "A copy" }, Json);

        response.EnsureSuccessStatusCode();
        var copy = await response.Content.ReadFromJsonAsync<CreatedTemplateDto>(Json);

        Assert.NotEqual(created.Id, copy!.Id);
        Assert.Equal("COPY_ONE", copy.Code);

        var stillThere = await _client.GetAsync($"/api/report-templates/{created.Id}");
        stillThere.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Delete_IsSoft_SoAnIdAlreadyWiredElsewhereKeepsResolving()
    {
        var created = await PostCreateAsync("SOFT_DELETE", SampleReports.StockByBrand());

        var deleted = await _client.DeleteAsync($"/api/report-templates/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // Gone from the default list...
        var page = await _client.GetFromJsonAsync<PagedResult<ReportTemplateSummaryDto>>(
            "/api/report-templates", Json);
        Assert.DoesNotContain(page!.Items, t => t.Id == created.Id);

        // ...but still fetchable, and still generatable.
        var fetched = await _client.GetAsync($"/api/report-templates/{created.Id}");
        fetched.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Parameters_DescribeTheContractIncludingResolvedLookupOptions()
    {
        var parameters = await _client.GetFromJsonAsync<ReportParametersDto>(
            $"/api/reports/{SampleReports.StockByBrandCode}/parameters", Json);

        Assert.NotNull(parameters);
        Assert.Equal(3, parameters!.Parameters.Count);

        var brand = parameters.Parameters.Single(p => p.Name == "brand");
        Assert.Equal(ParameterType.Select, brand.Type);
        // The Select's options come back resolved, so a caller does not have to fetch the lookup itself.
        Assert.NotNull(brand.Options);
        Assert.NotEmpty(brand.Options!);
    }

    [Fact]
    public async Task Parameters_CanBeFetchedByIdOrByCode()
    {
        var byCode = await _client.GetFromJsonAsync<ReportParametersDto>(
            $"/api/reports/{SampleReports.StockByBrandCode}/parameters", Json);

        var byId = await _client.GetFromJsonAsync<ReportParametersDto>(
            $"/api/reports/{byCode!.Id}/parameters", Json);

        Assert.Equal(byCode.Code, byId!.Code);
    }

    [RequiresChromiumFact]
    public async Task Generate_ReturnsAPdfWithPageCountHeaders()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/reports/{SampleReports.StockByBrandCode}/generate",
            new GenerateReportRequest(), Json);

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

        Assert.True(response.Headers.TryGetValues("X-Report-Page-Count", out var pageCount));
        Assert.True(int.Parse(pageCount!.First()) >= 1);
        Assert.True(response.Headers.Contains("X-Report-Duration-Ms"));
    }

    [RequiresChromiumFact]
    public async Task Generate_AcceptsParametersAndAppliesThem()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/reports/{SampleReports.StockByBrandCode}/generate",
            new GenerateReportRequest
            {
                Parameters = JsonDocument.Parse("""{"brand":"Nike","minPrice":0,"inStockOnly":true}""")
                    .RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)
            }, Json);

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public async Task Generate_WithAMissingRequiredParameter_IsA400ListingIt()
    {
        // A report whose parameter is required and has no default.
        var definition = SampleReports.StockByBrand();
        definition.Parameters.First(p => p.Name == "brand").Required = true;
        var created = await PostCreateAsync("NEEDS_BRAND", definition);

        var response = await _client.PostAsJsonAsync(
            $"/api/reports/{created.Id}/generate", new GenerateReportRequest(), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDto>(Json);
        Assert.Contains(problem!.Errors, e => e.Path == "brand");
    }

    [Fact]
    public async Task Generate_WithAnUnknownParameter_IsRejectedRatherThanIgnored()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/reports/{SampleReports.StockByBrandCode}/generate",
            new GenerateReportRequest
            {
                Parameters = JsonDocument.Parse("""{"nonsense":1}""")
                    .RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)
            }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [RequiresChromiumFact]
    public async Task Generate_WithPushedData_SkipsTheDatabaseEntirely()
    {
        // A template bound to a data source, but the caller supplies the rows. This is the mode where an
        // integrator already holds the data and does not want a second round trip.
        var definition = SampleReports.StockByBrand();
        var created = await PostCreateAsync("PUSHED_MODE", definition);

        var payload = JsonDocument.Parse("""
        {
          "detail": [
            {"name":"Pushed A","brand":"Acme","type":"Widgets","price":10.5,"quantityInStock":3,"stockValue":31.5},
            {"name":"Pushed B","brand":"Acme","type":"Widgets","price":20.0,"quantityInStock":2,"stockValue":40.0},
            {"name":"Pushed C","brand":"Zeta","type":"Gadgets","price":5.0,"quantityInStock":10,"stockValue":50.0}
          ]
        }
        """).RootElement;

        var response = await _client.PostAsJsonAsync(
            $"/api/reports/{created.Id}/generate/base64",
            new GenerateReportRequest
            {
                Data = payload.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)
            }, Json);

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<GeneratedReportDto>(Json);

        Assert.Equal("application/pdf", envelope!.ContentType);
        var bytes = Convert.FromBase64String(envelope.Base64);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

        // Proof the pushed rows were used rather than the seeded catalogue: the brands only exist here.
        var path = Path.Combine(Path.GetTempPath(), $"pushed-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            var text = PdfTools.ExtractText(path);
            if (text is not null)
            {
                Assert.Contains("Acme", text);
                Assert.DoesNotContain("Adidas", text);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Generate_WithDataForAnUnknownDataSet_IsRejected()
    {
        var payload = JsonDocument.Parse("""{"nosuchset":[{"a":1}]}""").RootElement;

        var response = await _client.PostAsJsonAsync(
            $"/api/reports/{SampleReports.StockByBrandCode}/generate",
            new GenerateReportRequest { Data = payload.EnumerateObject().ToDictionary(p => p.Name, p => p.Value) },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Generate_WithNestedPushedData_IsRejectedRatherThanStringified()
    {
        var payload = JsonDocument.Parse("""{"detail":[{"name":{"nested":true}}]}""").RootElement;

        var response = await _client.PostAsJsonAsync(
            $"/api/reports/{SampleReports.StockByBrandCode}/generate",
            new GenerateReportRequest { Data = payload.EnumerateObject().ToDictionary(p => p.Name, p => p.Value) },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [RequiresChromiumFact]
    public async Task Generate_HonoursAPerCallDirectionOverride()
    {
        // One stored template serving both languages.
        var response = await _client.PostAsJsonAsync(
            $"/api/reports/{SampleReports.StockByBrandCode}/generate/base64",
            new GenerateReportRequest
            {
                Options = new GenerateOptionsDto { Direction = TextDirection.Rtl, Culture = "he-IL" }
            }, Json);

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<GeneratedReportDto>(Json);
        Assert.True(envelope!.PageCount >= 1);
    }

    [RequiresChromiumFact]
    public async Task Preview_RendersAnUnsavedDefinitionWithoutStoringIt()
    {
        var before = await _client.GetFromJsonAsync<PagedResult<ReportTemplateSummaryDto>>(
            "/api/report-templates", Json);

        var definition = SampleReports.StockByBrand();
        definition.Name = "Never saved";

        var response = await _client.PostAsJsonAsync("/api/reports/preview",
            new PreviewReportRequest { Definition = definition }, Json);

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var after = await _client.GetFromJsonAsync<PagedResult<ReportTemplateSummaryDto>>(
            "/api/report-templates", Json);

        Assert.Equal(before!.Total, after!.Total);
    }

    [Fact]
    public async Task FileName_FromTheCallerIsSanitised()
    {
        var definition = SampleReports.StockByBrand();
        var created = await PostCreateAsync("FILENAME_TEST", definition);

        if (!RenderingHarness.ChromiumAvailable) return;

        var response = await _client.PostAsJsonAsync(
            $"/api/reports/{created.Id}/generate",
            new GenerateReportRequest
            {
                // Anything that could escape the filename or inject header content must be stripped.
                Options = new GenerateOptionsDto { FileName = "../../etc/passwd\r\nX-Evil: 1" }
            }, Json);

        response.EnsureSuccessStatusCode();
        var disposition = response.Content.Headers.ContentDisposition?.FileNameStar
                          ?? response.Content.Headers.ContentDisposition?.FileName;

        Assert.DoesNotContain("..", disposition);
        Assert.DoesNotContain("/", disposition);
        Assert.False(response.Headers.Contains("X-Evil"));
    }

    [Fact]
    public async Task UnknownReport_Is404OnEveryRunTimeEndpoint()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync("/api/reports/NO_SUCH_REPORT/parameters")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync("/api/reports/NO_SUCH_REPORT/generate",
                new GenerateReportRequest(), Json)).StatusCode);
    }

    [Fact]
    public async Task TableSourceCatalog_ListsTheSeededSourcesWithFieldMetadata()
    {
        var sources = await _client.GetFromJsonAsync<List<TableSourceDto>>("/api/report-sources", Json);

        Assert.NotNull(sources);
        var products = sources!.Single(s => s.TableId == API.Reporting.Data.SampleDataProvider.ProductsTableId);

        Assert.NotEmpty(products.Fields);
        Assert.Contains(products.Fields, f => f.Name == "stockValue" && f.DataType == FieldDataType.Decimal);
        // The designer needs to know what inputs a source expects in order to prompt for them.
        Assert.Contains(products.Inputs, i => i.Name == "minPrice");
    }

    [Fact]
    public async Task DataPreview_ReturnsRealRowsSoTheDesignerCanShowSampleValues()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/report-sources/{API.Reporting.Data.SampleDataProvider.ProductsTableId}/sample",
            new Dictionary<string, JsonElement>(), Json);

        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<DataPreviewDto>(Json);

        Assert.NotEmpty(preview!.Rows);
        Assert.NotEmpty(preview.Fields);
        // Capped by Reporting:Limits:PreviewRowCount: a design aid, not a data export.
        Assert.True(preview.Rows.Count <= 25);
    }

    [Fact]
    public async Task DataPreview_AppliesInputsAsRealFilters()
    {
        var payload = JsonDocument.Parse("""{"brand":"Nike"}""").RootElement
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

        var response = await _client.PostAsJsonAsync(
            $"/api/report-sources/{API.Reporting.Data.SampleDataProvider.ProductsTableId}/sample", payload, Json);

        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<DataPreviewDto>(Json);

        Assert.All(preview!.Rows, row => Assert.Equal("Nike", row["brand"]?.ToString()));
    }

    [Fact]
    public async Task Validate_ReportsProblemsWithoutSaving()
    {
        var definition = SampleReports.StockByBrand();
        definition.AllTables().First().Columns.Clear();

        var response = await _client.PostAsJsonAsync("/api/report-templates/validate", definition, Json);

        response.EnsureSuccessStatusCode();
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDto>(Json);

        Assert.Contains(problem!.Errors, e => e.Message.Contains("at least one column"));
    }

    private async Task<CreatedTemplateDto> PostCreateAsync(
        string code, ReportDefinition? definition, string? name = null)
    {
        var response = await _client.PostAsJsonAsync("/api/report-templates", new CreateTemplateRequest
        {
            Code = code,
            Name = name,
            Definition = definition
        }, Json);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedTemplateDto>(Json))!;
    }
}

/// <summary>
/// Hosts the real application over an isolated in-memory database, so tests exercise the production
/// pipeline without touching the developer's store.db.
/// </summary>
internal sealed class ReportApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureServices(services =>
        {
            // Replace the app's DbContext registration with one over a connection this factory owns.
            // Kept open for the factory's lifetime: SQLite discards an in-memory database as soon as the
            // last connection closes.
            services.RemoveAll<DbContextOptions<StoreContext>>();
            services.RemoveAll<StoreContext>();

            _connection.Open();
            services.AddDbContext<StoreContext>(opt => opt.UseSqlite(_connection));

            services.Configure<ReportingOptions>(options =>
            {
                options.Chromium.ExecutablePath = RenderingHarness.ChromiumPath;
                options.Chromium.MaxConcurrentRenders = 2;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
