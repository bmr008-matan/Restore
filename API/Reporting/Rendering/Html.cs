#nullable enable
using System.Text;

namespace API.Reporting.Rendering
{
    /// <summary>HTML escaping helpers. Every value that reaches the document goes through these.</summary>
    public static class Html
    {
        /// <summary>
        /// Escapes text for element content. Report data is not trusted markup — a product name
        /// containing "&lt;script&gt;" or a stray ampersand must print literally, not become structure.
        /// </summary>
        public static string Text(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new StringBuilder(value.Length + 16);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '&': builder.Append("&amp;"); break;
                    case '<': builder.Append("&lt;"); break;
                    case '>': builder.Append("&gt;"); break;
                    default: builder.Append(c); break;
                }
            }
            return builder.ToString();
        }

        /// <summary>Escapes a value destined for a double-quoted attribute.</summary>
        public static string Attr(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new StringBuilder(value.Length + 16);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '&': builder.Append("&amp;"); break;
                    case '<': builder.Append("&lt;"); break;
                    case '>': builder.Append("&gt;"); break;
                    case '"': builder.Append("&quot;"); break;
                    case '\'': builder.Append("&#39;"); break;
                    default: builder.Append(c); break;
                }
            }
            return builder.ToString();
        }

        /// <summary>Writes a style attribute, or nothing at all when the style is empty.</summary>
        public static string StyleAttr(string css) =>
            string.IsNullOrEmpty(css) ? string.Empty : $" style=\"{Attr(css)}\"";
    }
}
