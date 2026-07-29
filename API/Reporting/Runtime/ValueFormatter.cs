#nullable enable
using System.Globalization;

namespace API.Reporting.Runtime
{
    /// <summary>
    /// Turns a raw data value into display text using the report's culture and an optional .NET format
    /// string. One place so that a number formatted in a cell, in a subtotal and in a header token all
    /// come out identically.
    /// </summary>
    public sealed class ValueFormatter
    {
        private readonly CultureInfo _culture;

        public ValueFormatter(string? cultureName)
        {
            _culture = ResolveCulture(cultureName);
        }

        public CultureInfo Culture => _culture;

        /// <summary>
        /// Falls back to invariant rather than throwing: a template carrying a culture name the server
        /// does not have installed should still render, just with neutral formatting.
        ///
        /// predefinedOnly matters here. Without it ICU happily invents a culture for any well-formed
        /// name, so a typo like "en-USA" would silently produce a culture with no group separator
        /// instead of falling back — which looks like a formatting bug rather than a bad culture name.
        /// </summary>
        private static CultureInfo ResolveCulture(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return CultureInfo.InvariantCulture;
            try
            {
                return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        /// <summary>
        /// Formats a value. An unrecognised format string is ignored rather than fatal — the raw value
        /// is still shown, which is far more useful in a printed report than an exception.
        /// </summary>
        public string Format(object? value, string? format = null, string? nullText = null)
        {
            if (value is null or DBNull) return nullText ?? string.Empty;

            if (string.IsNullOrWhiteSpace(format))
                return DefaultFormat(value);

            try
            {
                return value switch
                {
                    IFormattable formattable => formattable.ToString(format, _culture),
                    _ => value.ToString() ?? string.Empty
                };
            }
            catch (FormatException)
            {
                return DefaultFormat(value);
            }
        }

        private string DefaultFormat(object value) => value switch
        {
            // Dates default to the culture's short date; a report that wants a time says so via format.
            DateTime date => date.ToString("d", _culture),
            DateOnly date => date.ToString("d", _culture),
            TimeOnly time => time.ToString("t", _culture),
            decimal or double or float => ((IFormattable)value).ToString("#,##0.##", _culture),
            bool flag => flag ? "Yes" : "No",
            IFormattable formattable => formattable.ToString(null, _culture),
            _ => value.ToString() ?? string.Empty
        };

        /// <summary>
        /// Converts a value to decimal for aggregation, returning null when it is not numeric.
        /// Strings are attempted too, because a provider may hand back a NUMBER column as text.
        /// </summary>
        public static decimal? ToDecimal(object? value)
        {
            switch (value)
            {
                case null or DBNull: return null;
                case decimal d: return d;
                case int i: return i;
                case long l: return l;
                case short s: return s;
                case byte b: return b;
                case double db: return (decimal)db;
                case float f: return (decimal)f;
                case bool flag: return flag ? 1m : 0m;
                case string text:
                    return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : null;
                default:
                    try
                    {
                        return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                    {
                        return null;
                    }
            }
        }
    }
}
