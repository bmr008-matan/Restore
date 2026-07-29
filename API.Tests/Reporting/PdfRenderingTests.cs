using API.Reporting;
using API.Reporting.Model;
using API.Reporting.Runtime;

namespace API.Tests.Reporting;

/// <summary>
/// End-to-end tests that actually drive Chromium and produce a PDF. They assert the things only a real
/// render can prove: that the file is a valid PDF, that it paginates, and that page numbers and Hebrew
/// text survive into the output.
///
/// Rendered PDFs are written to an output directory so the layout can be looked at by eye — no automated
/// assertion can judge whether a report actually looks right.
/// </summary>
public class PdfRenderingTests
{
    /// <summary>Where rendered samples land for visual inspection.</summary>
    private static string OutputDirectory
    {
        get
        {
            var directory = Environment.GetEnvironmentVariable("REPORT_TEST_OUTPUT")
                            ?? Path.Combine(Path.GetTempPath(), "report-samples");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    private static async Task<ReportRenderResult> RenderAsync(
        ReportDefinition definition, string fileName, ReportRequest? request = null, int productCount = 60)
    {
        await using var harness = new RenderingHarness(productCount);
        using var scope = harness.CreateScope();

        var result = await harness.Runtime(scope).RenderAsync(
            definition, request ?? new ReportRequest { UserName = "verification" }, CancellationToken.None);

        await File.WriteAllBytesAsync(Path.Combine(OutputDirectory, fileName), result.Pdf);
        return result;
    }

    [RequiresChromiumFact]
    public async Task PortraitReport_RendersAValidMultiPagePdf()
    {
        var result = await RenderAsync(SampleReports.StockByBrand(), "stock-a4-portrait-ltr.pdf");

        // A real PDF, not an error page or an empty buffer.
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(result.Pdf, 0, 5));
        Assert.True(result.Pdf.Length > 5_000, $"PDF was only {result.Pdf.Length} bytes.");

        // 60 products across three brands and three types, with group headers and subtotals, cannot fit
        // on one A4 page — so this also proves pagination works.
        Assert.True(result.PageCount > 1, $"Expected more than one page, got {result.PageCount}.");
        Assert.Empty(result.Diagnostics);
        Assert.False(result.AnyDataTruncated);
    }

    [RequiresChromiumFact]
    public async Task LandscapeReport_RendersWiderThanTall()
    {
        var definition = SampleReports.StockByBrand();
        definition.Page.Orientation = PageOrientation.Landscape;

        var result = await RenderAsync(definition, "stock-a4-landscape-ltr.pdf");

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(result.Pdf, 0, 5));
        Assert.True(result.PageCount >= 1);
    }

    [RequiresChromiumFact]
    public async Task HebrewReport_RendersRightToLeft()
    {
        var result = await RenderAsync(SampleReports.StockByBrand(TextDirection.Rtl), "stock-a4-portrait-hebrew-rtl.pdf");

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(result.Pdf, 0, 5));
        Assert.True(result.PageCount > 1);
        Assert.Empty(result.Diagnostics);
    }

    [RequiresChromiumFact]
    public async Task PageCount_IsReportedAndMatchesWhatIsInTheFile()
    {
        var result = await RenderAsync(SampleReports.StockByBrand(), "stock-pagecount-check.pdf");

        // The scan-based count must agree with a real PDF tool when one is available; otherwise the
        // X-Report-Page-Count header would be quietly wrong.
        var reported = PdfTools.PageCount(Path.Combine(OutputDirectory, "stock-pagecount-check.pdf"));
        if (reported is null) return;

        Assert.Equal(reported.Value, result.PageCount);
    }

    [RequiresChromiumFact]
    public async Task PageNumbers_AreSubstitutedByChromiumIntoEveryPage()
    {
        await RenderAsync(SampleReports.StockByBrand(), "stock-pagenumbers.pdf");

        var text = PdfTools.ExtractText(Path.Combine(OutputDirectory, "stock-pagenumbers.pdf"));
        if (text is null) return;

        // The literal token must be gone, replaced by a real number.
        Assert.DoesNotContain("{PageNumber}", text);
        Assert.Matches(@"Page\s+1\s+of\s+\d+", text);
        Assert.Matches(@"Page\s+2\s+of\s+\d+", text);
    }

    [RequiresChromiumFact]
    public async Task RepeatedColumnHeaders_AppearOnEveryPage()
    {
        await RenderAsync(SampleReports.StockByBrand(), "stock-repeated-headers.pdf");

        var path = Path.Combine(OutputDirectory, "stock-repeated-headers.pdf");
        var pageCount = PdfTools.PageCount(path);
        if (pageCount is null or < 2) return;

        // "Stock value" is a column caption, so it should occur at least once per page.
        var occurrences = 0;
        for (var page = 1; page <= pageCount.Value; page++)
        {
            var pageText = PdfTools.ExtractText(path, page);
            if (pageText?.Contains("Stock value", StringComparison.Ordinal) == true) occurrences++;
        }

        Assert.Equal(pageCount.Value, occurrences);
    }

    [RequiresChromiumFact]
    public async Task HebrewText_SurvivesIntoThePdfAsRealHebrew()
    {
        await RenderAsync(SampleReports.StockByBrand(TextDirection.Rtl), "hebrew-text-check.pdf");

        var text = PdfTools.ExtractText(Path.Combine(OutputDirectory, "hebrew-text-check.pdf"));
        if (text is null) return;

        // Hebrew letters must be present as Hebrew codepoints, meaning the font carried the glyphs and
        // Chromium shaped them rather than emitting boxes.
        Assert.Contains(text, c => c >= 'א' && c <= 'ת');
        Assert.DoesNotContain("�", text);
    }

    [RequiresChromiumFact]
    public async Task GrandTotal_AppearsOnceRatherThanOnEveryPage()
    {
        await RenderAsync(SampleReports.StockByBrand(), "grand-total-once.pdf");

        var path = Path.Combine(OutputDirectory, "grand-total-once.pdf");
        var pageCount = PdfTools.PageCount(path);
        if (pageCount is null or < 2) return;

        var pagesWithGrandTotal = 0;
        for (var page = 1; page <= pageCount.Value; page++)
        {
            var pageText = PdfTools.ExtractText(path, page);
            if (pageText?.Contains("Grand total", StringComparison.Ordinal) == true) pagesWithGrandTotal++;
        }

        // This is the tfoot trap: a table-footer-group would repeat on every page.
        Assert.Equal(1, pagesWithGrandTotal);
    }

    [RequiresChromiumFact]
    public async Task SubtotalsAddUpToTheGrandTotal()
    {
        await using var harness = new RenderingHarness(30);
        using var scope = harness.CreateScope();

        var diagnostics = new RenderDiagnostics();
        var html = await harness.Runtime(scope).RenderHtmlAsync(
            SampleReports.StockByBrand(), new ReportRequest(), diagnostics, CancellationToken.None);

        // Pull the brand subtotal rows and the grand total row out of the document and check the
        // arithmetic the reader will do by eye.
        var brandTotals = System.Text.RegularExpressions.Regex
            .Matches(html.Document, @"rpt-grp-f rpt-grp-f-0.*?</tr>",
                System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => LastNumberIn(m.Value))
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();

        var grand = System.Text.RegularExpressions.Regex
            .Matches(html.Document, @"rpt-grand.*?</tr>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => LastNumberIn(m.Value))
            .FirstOrDefault(v => v is not null);

        Assert.NotNull(grand);
        Assert.NotEmpty(brandTotals);
        Assert.Equal(grand!.Value, brandTotals.Sum(), precision: 2);
    }

    [RequiresChromiumFact]
    public async Task EmptyReport_StillProducesAValidOnePagePdf()
    {
        var definition = SampleReports.StockByBrand();
        definition.Parameters.First(p => p.Name == "minPrice").DefaultValue = "999999";

        var result = await RenderAsync(definition, "stock-empty.pdf");

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(result.Pdf, 0, 5));
        Assert.Equal(1, result.PageCount);
    }

    [RequiresChromiumFact]
    public async Task RowCap_TruncatesAndSaysSoRatherThanRenderingEverything()
    {
        var definition = SampleReports.StockByBrand();
        definition.DataSets[0].MaxRows = 10;

        var result = await RenderAsync(definition, "stock-truncated.pdf", productCount: 50);

        Assert.True(result.AnyDataTruncated);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("first 10 rows"));
    }

    /// <summary>Grabs the last formatted number in a table row, which is the stock-value column.</summary>
    private static decimal? LastNumberIn(string rowHtml)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(rowHtml, @"[\d,]+\.\d{2}");
        if (matches.Count == 0) return null;

        var text = matches[^1].Value.Replace(",", string.Empty);
        return decimal.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}

/// <summary>
/// Thin wrapper over poppler's command-line tools. Used only for verification, and every method returns
/// null when the tool is absent so the suite degrades to weaker assertions instead of failing.
/// </summary>
internal static class PdfTools
{
    public static int? PageCount(string path)
    {
        var output = Run("pdfinfo", $"\"{path}\"");
        if (output is null) return null;

        var line = output.Split('\n').FirstOrDefault(l => l.StartsWith("Pages:", StringComparison.Ordinal));
        if (line is null) return null;

        return int.TryParse(line["Pages:".Length..].Trim(), out var pages) ? pages : null;
    }

    public static string? ExtractText(string path, int? page = null)
    {
        var range = page is null ? "" : $"-f {page} -l {page} ";
        // -layout keeps columns roughly in place, which makes the extracted text far easier to assert on.
        return Run("pdftotext", $"-layout {range}\"{path}\" -");
    }

    private static string? Run(string fileName, string arguments)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(20_000);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // Tool not installed; caller falls back to weaker assertions.
            return null;
        }
    }
}
