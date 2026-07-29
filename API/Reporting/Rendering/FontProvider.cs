#nullable enable
using System.Collections.Concurrent;
using System.Text;
using API.Reporting.Configuration;
using API.Reporting.Model;
using Microsoft.Extensions.Options;

namespace API.Reporting.Rendering
{
    /// <summary>The font family stack for a document, plus any @font-face rules it needs.</summary>
    public sealed record ResolvedFont(string FamilyStack, string? FontFaceCss);

    /// <summary>
    /// Supplies the fonts a report renders with, and in particular a Hebrew-capable one.
    ///
    /// Two modes, because they trade off against each other:
    ///
    /// By default the document names installed families (DejaVu Sans first, which covers Hebrew letters
    /// and niqqud). That keeps the generated HTML small, which matters when a report has tens of
    /// thousands of rows.
    ///
    /// With <see cref="ReportingOptions.FontOptions.Embed"/> set, any TTF or OTF in the configured font
    /// directory is inlined as a base64 @font-face. That makes output byte-identical regardless of what
    /// is installed on the host — worth it when reports are archived or compared — at the cost of
    /// roughly 1.3x the font file size added to every rendered document. Encoded fonts are cached, so
    /// the encoding work happens once per process, not once per render.
    /// </summary>
    public sealed class FontProvider
    {
        private readonly ReportingOptions.FontOptions _options;
        private readonly ILogger<FontProvider> _logger;
        private readonly Lazy<string?> _fontFaceCss;
        private readonly ConcurrentDictionary<string, string> _familyStacks = new(StringComparer.Ordinal);

        public FontProvider(IOptions<ReportingOptions> options, ILogger<FontProvider> logger)
        {
            _options = options.Value.Fonts;
            _logger = logger;
            _fontFaceCss = new Lazy<string?>(BuildFontFaceCss, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public ResolvedFont Resolve(ReportDefinition definition)
        {
            var requested = definition.DefaultStyle?.FontFamily;
            var stack = _familyStacks.GetOrAdd(requested ?? string.Empty, BuildFamilyStack);
            return new ResolvedFont(stack, _fontFaceCss.Value);
        }

        /// <summary>
        /// A report's chosen family goes first, then the embedded or installed Hebrew-capable families,
        /// then a generic. The fallback chain is what stops a missing glyph from rendering as a box.
        /// </summary>
        private string BuildFamilyStack(string requested)
        {
            var families = new List<string>();

            if (!string.IsNullOrWhiteSpace(requested)) families.Add(requested);
            if (_options.Embed && _fontFaceCss.Value is not null) families.Add(_options.EmbeddedFamilyName);

            families.AddRange(_options.FamilyStack);

            return string.Join(',', families
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(Quote));
        }

        private static string Quote(string family) =>
            family is "sans-serif" or "serif" or "monospace" ? family : $"\"{CssBuilder.CssSafe(family)}\"";

        private string? BuildFontFaceCss()
        {
            if (!_options.Embed) return null;

            var directory = _options.Directory;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                _logger.LogWarning(
                    "Font embedding is enabled but the font directory {Directory} does not exist. " +
                    "Falling back to installed fonts.", directory);
                return null;
            }

            var files = Directory.EnumerateFiles(directory)
                .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            if (files.Count == 0)
            {
                _logger.LogWarning(
                    "Font embedding is enabled but no .ttf or .otf files were found in {Directory}. " +
                    "Falling back to installed fonts.", directory);
                return null;
            }

            var css = new StringBuilder();
            foreach (var file in files)
            {
                try
                {
                    var bytes = File.ReadAllBytes(file);
                    var format = file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ? "opentype" : "truetype";
                    var name = Path.GetFileNameWithoutExtension(file);

                    css.Append("@font-face{font-family:\"").Append(CssBuilder.CssSafe(_options.EmbeddedFamilyName))
                       .Append("\";font-weight:").Append(WeightFor(name))
                       .Append(";font-style:").Append(StyleFor(name))
                       .Append(";src:url(data:font/").Append(format is "opentype" ? "otf" : "ttf")
                       .Append(";base64,").Append(Convert.ToBase64String(bytes))
                       .Append(") format(\"").Append(format).Append("\");}\n");
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not read font file {File}; skipping it.", file);
                }
            }

            return css.Length == 0 ? null : css.ToString();
        }

        /// <summary>
        /// Weight and style come from the filename, following the convention font projects already use
        /// (NotoSansHebrew-Bold.ttf, Rubik-Italic.ttf). Guessing from the file is far less fragile than
        /// asking an operator to describe each face in configuration.
        /// </summary>
        private static string WeightFor(string fileName) =>
            fileName.Contains("bold", StringComparison.OrdinalIgnoreCase) ? "bold" : "normal";

        private static string StyleFor(string fileName) =>
            fileName.Contains("italic", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("oblique", StringComparison.OrdinalIgnoreCase)
                ? "italic"
                : "normal";
    }
}
