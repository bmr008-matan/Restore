#nullable enable
using API.Reporting.Configuration;
using API.Reporting.Model;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace API.Reporting.Rendering
{
    public sealed record PdfResult(byte[] Bytes, int PageCount, long ElapsedMs);

    /// <summary>
    /// Owns the headless Chromium instance and turns rendered HTML into a PDF.
    ///
    /// Registered as a singleton and holding one browser for the process lifetime: launching Chromium
    /// costs hundreds of milliseconds, so doing it per request would dominate the cost of every report.
    /// A semaphore caps how many pages exist at once, because each open page is real browser memory and
    /// an unbounded burst is the fastest way to get the process killed.
    /// </summary>
    public sealed class ChromiumPdfService : IAsyncDisposable
    {
        private readonly ReportingOptions.ChromiumOptions _options;
        private readonly ILogger<ChromiumPdfService> _logger;
        private readonly SemaphoreSlim _pageLimit;
        private readonly SemaphoreSlim _launchLock = new(1, 1);

        private IBrowser? _browser;
        private bool _disposed;

        public ChromiumPdfService(IOptions<ReportingOptions> options, ILogger<ChromiumPdfService> logger)
        {
            _options = options.Value.Chromium;
            _logger = logger;
            _pageLimit = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRenders));
        }

        public async Task<PdfResult> RenderAsync(
            RenderedHtml html,
            PageSetupDef page,
            CancellationToken cancellationToken)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var browser = await GetBrowserAsync(cancellationToken);

            await _pageLimit.WaitAsync(cancellationToken);
            try
            {
                await using var tab = await browser.NewPageAsync();

                // Chromium's print path only takes effect for print media; without this the PDF picks up
                // screen styles and @page is ignored.
                await tab.EmulateMediaTypeAsync(PuppeteerSharp.Media.MediaType.Print);

                await tab.SetContentAsync(html.Document, new NavigationOptions
                {
                    // Data-URI images and inlined fonts resolve synchronously, so there is nothing to
                    // wait on beyond the document itself. Waiting for network idle here would add a
                    // fixed delay to every render for no benefit.
                    WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                    Timeout = _options.RenderTimeoutSeconds * 1000
                });

                var bytes = await tab.PdfDataAsync(BuildOptions(html, page));

                stopwatch.Stop();
                return new PdfResult(bytes, CountPages(bytes), stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                _pageLimit.Release();
            }
        }

        private PdfOptions BuildOptions(RenderedHtml html, PageSetupDef page)
        {
            var hasHeader = html.HeaderTemplate is { Length: > 0 };
            var hasFooter = html.FooterTemplate is { Length: > 0 };

            return new PdfOptions
            {
                // The @page rule in the document is the single source of truth for paper size,
                // orientation and margins. Letting Chromium's own Format and Landscape options compete
                // with it is how page setup ends up disagreeing with what the designer configured.
                PreferCSSPageSize = true,

                PrintBackground = true,

                // Chromium renders a default header of its own when this is on but a template is empty,
                // so it is only enabled when there is genuinely something to draw.
                DisplayHeaderFooter = hasHeader || hasFooter,
                HeaderTemplate = html.HeaderTemplate ?? "<span></span>",
                FooterTemplate = html.FooterTemplate ?? "<span></span>",

                Timeout = _options.RenderTimeoutSeconds * 1000
            };
        }

        /// <summary>
        /// Launches on first use and reuses thereafter, re-launching if the browser has died. The lock
        /// keeps a burst of first requests from starting several browsers.
        /// </summary>
        private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
        {
            if (_browser is { IsClosed: false }) return _browser;

            await _launchLock.WaitAsync(cancellationToken);
            try
            {
                if (_browser is { IsClosed: false }) return _browser;

                if (_browser is not null)
                {
                    _logger.LogWarning("Chromium had exited; relaunching.");
                    await SafeDisposeBrowserAsync();
                }

                _browser = await LaunchAsync();
                return _browser;
            }
            finally
            {
                _launchLock.Release();
            }
        }

        private async Task<IBrowser> LaunchAsync()
        {
            var launchOptions = new LaunchOptions
            {
                Headless = true,
                Args = _options.LaunchArguments
            };

            var executable = ResolveExecutablePath();
            if (executable is not null)
            {
                launchOptions.ExecutablePath = executable;
                _logger.LogInformation("Launching Chromium from {Path}.", executable);
            }
            else
            {
                // No browser configured or found, so fetch one. This writes to disk and needs network,
                // which is fine for a developer but should never be the path in a deployment.
                _logger.LogWarning(
                    "No Chromium executable configured or found; downloading a browser. Set " +
                    "Reporting:Chromium:ExecutablePath to avoid this.");

                var fetcher = new BrowserFetcher();
                await fetcher.DownloadAsync();
            }

            return await Puppeteer.LaunchAsync(launchOptions);
        }

        /// <summary>
        /// Prefers the configured path, then the well-known locations a container image is likely to
        /// have already. Returns null when nothing exists, so the caller can decide to download.
        /// </summary>
        private string? ResolveExecutablePath()
        {
            if (_options.ExecutablePath is { Length: > 0 } configured)
            {
                if (File.Exists(configured)) return configured;

                _logger.LogWarning(
                    "Reporting:Chromium:ExecutablePath points at {Path}, which does not exist.", configured);
            }

            var candidates = new[]
            {
                "/opt/pw-browsers/chromium",
                "/usr/bin/chromium",
                "/usr/bin/chromium-browser",
                "/usr/bin/google-chrome"
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>
        /// Counts pages by scanning the PDF for page objects. Chromium does not report a page count, and
        /// callers legitimately want one — it goes out as a response header. Deliberately a cheap scan
        /// rather than a full parse: this must not become a reason to take on a PDF-parsing dependency.
        /// Returns 0 when the structure is not recognised, which callers treat as "unknown".
        /// </summary>
        internal static int CountPages(byte[] pdf)
        {
            // "/Type /Page" appears once per page; "/Type /Pages" marks the tree nodes and must not be
            // counted, which is what the trailing-character check rules out.
            var needle = "/Type"u8.ToArray();
            var count = 0;

            for (var i = 0; i + needle.Length < pdf.Length; i++)
            {
                if (!MatchesAt(pdf, i, needle)) continue;

                var j = i + needle.Length;
                while (j < pdf.Length && (pdf[j] == ' ' || pdf[j] == '\n' || pdf[j] == '\r')) j++;
                if (j >= pdf.Length || pdf[j] != '/') continue;

                j++;
                if (!MatchesAt(pdf, j, "Page"u8.ToArray())) continue;

                var after = j + 4;
                if (after < pdf.Length && (pdf[after] == 's' || pdf[after] == 'S')) continue;

                count++;
            }

            return count;
        }

        private static bool MatchesAt(byte[] haystack, int offset, byte[] needle)
        {
            if (offset + needle.Length > haystack.Length) return false;
            for (var k = 0; k < needle.Length; k++)
                if (haystack[offset + k] != needle[k]) return false;
            return true;
        }

        private async Task SafeDisposeBrowserAsync()
        {
            try
            {
                if (_browser is not null) await _browser.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ignoring failure while disposing a dead Chromium instance.");
            }
            finally
            {
                _browser = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await SafeDisposeBrowserAsync();
            _pageLimit.Dispose();
            _launchLock.Dispose();
        }
    }
}
