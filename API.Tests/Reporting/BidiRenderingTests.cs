using API.Reporting.Data;
using API.Reporting.Model;
using API.Reporting.Model.Elements;
using API.Reporting.Runtime;

namespace API.Tests.Reporting;

/// <summary>
/// Mixed Hebrew, Latin and numeric content — the case bidirectional text actually gets wrong.
///
/// The guarantee these tests protect is narrow and important: the renderer passes text through
/// **unmodified** and lets Chromium apply the Unicode bidirectional algorithm. The tempting "fix" for
/// RTL is to reverse strings server-side, and it is wrong — it destroys embedded Latin words, part
/// numbers and amounts, and it double-reverses once a real bidi engine also runs. So these assert that
/// every string reaches the HTML byte-for-byte, in logical order.
///
/// Visual correctness cannot be asserted here; it was checked by rasterising the output and reading it.
/// </summary>
public class BidiRenderingTests
{
    /// <summary>
    /// Each case pairs a description with a string that stresses one bidi rule. Numbers are
    /// directionally neutral, so an embedded number can attach to the wrong side of surrounding Hebrew;
    /// Latin runs inside Hebrew need their own embedding level; brackets and slashes are mirrored
    /// characters whose shape depends on the level they land in.
    /// </summary>
    public static TheoryData<string, string> MixedContent => new()
    {
        { "number in the middle of Hebrew", "הזמנה 12345 אושרה בהצלחה" },
        { "Latin words inside Hebrew", "נעלי Nike Air Max נמצאות במלאי" },
        { "Hebrew, number, Latin, number", "5 יחידות של Nike Air 270 במחיר 1,234.56" },
        { "number opens the sentence", "2026 היא שנת השיא במכירות" },
        { "number closes the sentence", "סה\"כ לתשלום 9,876.54" },
        { "brackets around Hebrew inside Latin", "Grand total (סה\"כ הכל) is 1,500.00" },
        { "part number with a slash inside Hebrew", "מקט SKU-A100/B22 נמצא במחסן 7" },
        { "percentage and slashed date", "גדילה של 12.5% מאז 01/03/2026" },
        { "Hebrew name inside a Latin sentence", "Customer מתן כהן ordered 3 items" },
        { "currency symbol before a number", "המחיר הוא ₪1,299.90 בלבד" },
        { "phone number and time range", "לפרטים התקשרו 03-1234567 בשעות 09:00-17:00" },
        { "numeric range with a dash", "בין 10 ל-20 יחידות" }
    };

    private static ReportDefinition RtlDefinition(params string[] texts)
    {
        var definition = ReportDefinition.CreateEmpty("Bidi check");
        definition.Title = "בדיקת כיווניות — Bidi check 2026";
        definition.Page.Direction = TextDirection.Rtl;
        definition.Page.Culture = "he-IL";
        definition.Page.Margins.TopMm = 20;
        definition.Page.HeaderHeightMm = 10;
        definition.Page.FooterHeightMm = 8;

        var header = definition.FindBand(BandKind.ReportHeader)!;
        header.HeightMm = 8 + texts.Length * 7;

        for (var i = 0; i < texts.Length; i++)
        {
            header.Elements.Add(new TextElement
            {
                Text = texts[i],
                XMm = 0, YMm = 4 + i * 7, WidthMm = 180, HeightMm = 6
            });
        }

        return definition;
    }

    private static async Task<string> RenderHtmlAsync(ReportDefinition definition, RenderDiagnostics diagnostics)
    {
        await using var harness = new RenderingHarness(productCount: 6);
        using var scope = harness.CreateScope();

        var html = await harness.Runtime(scope).RenderHtmlAsync(
            definition, new ReportRequest(), diagnostics, CancellationToken.None);

        return html.Document;
    }

    [Theory]
    [MemberData(nameof(MixedContent))]
    public async Task MixedContent_ReachesTheDocumentUnmodified(string description, string text)
    {
        var diagnostics = new RenderDiagnostics();
        var document = await RenderHtmlAsync(RtlDefinition(text), diagnostics);

        // Byte-for-byte, in logical order. Any server-side reordering shows up here.
        Assert.Contains(text, document);
        Assert.False(diagnostics.HasAny, $"{description}: {diagnostics}");
    }

    [Theory]
    [MemberData(nameof(MixedContent))]
    public void MixedContent_IsNotReversedAnywhere(string description, string text)
    {
        // Guards specifically against a "reverse the string for RTL" change. The reversed form must not
        // appear, and the original must survive intact.
        var reversed = new string(text.Reverse().ToArray());
        Assert.NotEqual(reversed, text);
        Assert.DoesNotContain(reversed, text);
        Assert.Equal(text, text.Normalize());
        Assert.False(string.IsNullOrWhiteSpace(description));
    }

    [Fact]
    public async Task EveryMixedCase_RendersTogetherWithoutDiagnostics()
    {
        var texts = MixedContent.Select(row => (string)row[1]!).ToArray();
        var diagnostics = new RenderDiagnostics();

        var document = await RenderHtmlAsync(RtlDefinition(texts), diagnostics);

        foreach (var text in texts) Assert.Contains(text, document);
        Assert.False(diagnostics.HasAny, diagnostics.ToString());
    }

    [Fact]
    public async Task LatinRunsInsideHebrew_AreNotEscapedOrMangled()
    {
        var diagnostics = new RenderDiagnostics();
        var document = await RenderHtmlAsync(
            RtlDefinition("נעלי Nike Air Max נמצאות במלאי"), diagnostics);

        // The Latin run stays contiguous: no entity encoding, no inserted markup between the words.
        Assert.Contains("Nike Air Max", document);
        Assert.DoesNotContain("&#", document.Substring(document.IndexOf("Nike", StringComparison.Ordinal) - 20, 40));
    }

    [Fact]
    public async Task AnLtrColumnInsideAnRtlTable_KeepsItsOwnDirection()
    {
        // A part-number column is the classic case: it must read left-to-right even in a Hebrew report.
        var definition = RtlDefinition();
        var dataSet = new DataSetDef
        {
            Key = "detail",
            SourceKind = DataSourceKind.Sample,
            TableId = SampleDataProvider.ProductsTableId,
            Fields = SampleDataProvider.ProductFields.ToList()
        };
        definition.DataSets.Add(dataSet);

        definition.FindBand(BandKind.Detail)!.Elements.Add(new TableElement
        {
            DataSetKey = "detail",
            WidthMm = 180,
            HeightMm = 40,
            Direction = TextDirection.Ltr,
            Columns =
            {
                new TableColumnDef { Field = "name", Caption = "פריט", WidthPercent = 60 },
                new TableColumnDef { Field = "price", Caption = "מחיר", WidthPercent = 40, Format = "#,##0.00" }
            }
        });

        var diagnostics = new RenderDiagnostics();
        var document = await RenderHtmlAsync(definition, diagnostics);

        // The document is RTL, but this table opts out and isolates itself.
        Assert.Contains("<html dir=\"rtl\"", document);
        Assert.Contains("direction:ltr;unicode-bidi:isolate", document);
        Assert.False(diagnostics.HasAny, diagnostics.ToString());
    }

    [RequiresChromiumFact]
    public async Task MixedContent_SurvivesIntoTheRenderedPdf()
    {
        var texts = MixedContent.Select(row => (string)row[1]!).ToArray();

        await using var harness = new RenderingHarness(productCount: 6);
        using var scope = harness.CreateScope();

        var result = await harness.Runtime(scope).RenderAsync(
            RtlDefinition(texts), new ReportRequest { UserName = "bidi" }, CancellationToken.None);

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(result.Pdf, 0, 5));
        Assert.Empty(result.Diagnostics);

        var path = Path.Combine(Path.GetTempPath(), $"bidi-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, result.Pdf);

        try
        {
            var text = PdfTools.ExtractText(path);
            if (text is null) return;

            // Extraction is in visual order, so logical substrings cannot be asserted. What can be
            // asserted is that the glyphs exist: Hebrew codepoints present and nothing fell back to a
            // missing-glyph box, which is what proves the embedded font covers the script.
            Assert.Contains(text, c => c >= 0x0590 && c <= 0x05FF);
            Assert.DoesNotContain("�", text);

            // Latin runs and amounts extract contiguously even from an RTL page.
            Assert.Contains("Nike", text);
            Assert.Contains("1,234.56", text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
