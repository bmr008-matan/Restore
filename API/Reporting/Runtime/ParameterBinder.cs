#nullable enable
using System.Globalization;
using System.Text.Json;
using API.Reporting.Model;

namespace API.Reporting.Runtime
{
    /// <summary>One rejected parameter, reported back to the caller per field.</summary>
    public sealed record ParameterError(string Parameter, string Message);

    public sealed class ParameterBindingResult
    {
        public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ParameterError> Errors { get; } = new();
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Validates and coerces the parameter values supplied on a generate request against what the
    /// template declares, applying defaults for anything omitted.
    ///
    /// This runs before any data provider is touched, so a bad request costs nothing — no Oracle call,
    /// no Chromium page. Callers get a 400 listing every offending parameter at once rather than
    /// discovering them one round trip at a time.
    /// </summary>
    public sealed class ParameterBinder
    {
        private readonly TimeProvider _time;

        public ParameterBinder(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

        public ParameterBindingResult Bind(
            IReadOnlyList<ReportParameter> declared,
            IReadOnlyDictionary<string, object?>? supplied,
            string? currentUser = null)
        {
            var result = new ParameterBindingResult();
            var now = _time.GetLocalNow().DateTime;

            foreach (var parameter in declared)
            {
                var provided = TryGetSupplied(supplied, parameter.Name, out var raw);

                if (!provided || IsEmpty(raw))
                {
                    if (parameter.DefaultValue is { Length: > 0 })
                    {
                        raw = ResolveDefault(parameter.DefaultValue, now, currentUser);
                    }
                    else if (parameter.Required)
                    {
                        result.Errors.Add(new ParameterError(parameter.Name,
                            $"\"{parameter.EffectiveLabel}\" is required."));
                        continue;
                    }
                    else
                    {
                        result.Values[parameter.Name] = parameter.IsMultiValue
                            ? Array.Empty<object?>()
                            : null;
                        continue;
                    }
                }

                if (TryCoerce(parameter, raw, out var value, out var error))
                    result.Values[parameter.Name] = value;
                else
                    result.Errors.Add(new ParameterError(parameter.Name, error!));
            }

            // Values sent for parameters the template does not declare are rejected rather than
            // ignored: silently dropping one is how a report quietly returns the wrong rows.
            if (supplied is not null)
            {
                foreach (var key in supplied.Keys)
                {
                    if (declared.Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    result.Errors.Add(new ParameterError(key,
                        $"This report has no parameter called \"{key}\"."));
                }
            }

            return result;
        }

        private static bool TryGetSupplied(
            IReadOnlyDictionary<string, object?>? supplied, string name, out object? value)
        {
            value = null;
            if (supplied is null) return false;

            foreach (var pair in supplied)
            {
                if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
                value = pair.Value;
                return true;
            }
            return false;
        }

        private static bool IsEmpty(object? raw) => raw switch
        {
            null => true,
            string s => s.Length == 0,
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => true,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString()!.Length == 0,
            _ => false
        };

        /// <summary>
        /// Resolves the relative-date and current-user tokens a template can use as a default, so a
        /// report can open on "this month" without the caller computing dates.
        /// </summary>
        private static object? ResolveDefault(string spec, DateTime now, string? currentUser) => spec switch
        {
            "@Today" => now.Date,
            "@Yesterday" => now.Date.AddDays(-1),
            "@Tomorrow" => now.Date.AddDays(1),
            "@StartOfMonth" => new DateTime(now.Year, now.Month, 1),
            "@EndOfMonth" => new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)),
            "@StartOfYear" => new DateTime(now.Year, 1, 1),
            "@EndOfYear" => new DateTime(now.Year, 12, 31),
            "@CurrentUser" => currentUser ?? "",
            _ => spec
        };

        private bool TryCoerce(ReportParameter parameter, object? raw, out object? value, out string? error)
        {
            value = null;
            error = null;

            try
            {
                if (parameter.IsMultiValue)
                {
                    var items = Enumerate(raw).ToList();

                    if (parameter.Type == ParameterType.DateRange && items.Count is not (0 or 2))
                    {
                        error = $"\"{parameter.EffectiveLabel}\" needs exactly two dates, got {items.Count}.";
                        return false;
                    }

                    var elementType = parameter.Type == ParameterType.DateRange
                        ? ParameterType.Date
                        : ParameterType.Text;

                    var converted = new List<object?>(items.Count);
                    foreach (var item in items)
                    {
                        // A MultiSelect holds lookup values, whose type the template does not state,
                        // so they are kept as scalars rather than forced into one type.
                        converted.Add(elementType == ParameterType.Date
                            ? ConvertScalar(item, ParameterType.Date)
                            : Unwrap(item));
                    }

                    value = converted;
                    return true;
                }

                value = ConvertScalar(raw, parameter.Type);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException
                                          or ArgumentException or JsonException)
            {
                error = $"\"{parameter.EffectiveLabel}\" is not a valid {parameter.Type}: {ex.Message}";
                return false;
            }
        }

        private static IEnumerable<object?> Enumerate(object? raw)
        {
            switch (raw)
            {
                case null:
                    yield break;
                case JsonElement { ValueKind: JsonValueKind.Array } array:
                    foreach (var item in array.EnumerateArray()) yield return item;
                    break;
                case string single:
                    yield return single;
                    break;
                case System.Collections.IEnumerable sequence:
                    foreach (var item in sequence) yield return item;
                    break;
                default:
                    yield return raw;
                    break;
            }
        }

        private static object? ConvertScalar(object? raw, ParameterType type)
        {
            var scalar = Unwrap(raw);
            if (scalar is null) return null;

            return type switch
            {
                ParameterType.Int => scalar is long l ? l : Convert.ToInt64(scalar, CultureInfo.InvariantCulture),
                ParameterType.Decimal => scalar is decimal d ? d : Convert.ToDecimal(scalar, CultureInfo.InvariantCulture),
                ParameterType.Bool => scalar is bool b ? b : ParseBool(scalar),
                ParameterType.Date => scalar is DateTime dt ? dt : ParseDate(scalar),
                _ => Convert.ToString(scalar, CultureInfo.InvariantCulture)
            };
        }

        private static bool ParseBool(object scalar) => scalar switch
        {
            string s when bool.TryParse(s, out var parsed) => parsed,
            // Accept the 1/0 and yes/no forms integrations tend to send.
            string s when s is "1" or "yes" or "Yes" or "Y" or "y" => true,
            string s when s is "0" or "no" or "No" or "N" or "n" => false,
            _ => Convert.ToBoolean(scalar, CultureInfo.InvariantCulture)
        };

        /// <summary>
        /// Parsed with the invariant culture so that "2026-01-31" means the same thing regardless of
        /// the server's locale. The report's own culture governs display, never input.
        /// </summary>
        private static DateTime ParseDate(object scalar)
        {
            var text = Convert.ToString(scalar, CultureInfo.InvariantCulture) ?? "";
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces);
        }

        /// <summary>
        /// Flattens a JsonElement into a plain CLR scalar. Parameters arrive as JSON on the API, and
        /// letting JsonElement leak into expressions would make every comparison behave oddly.
        /// </summary>
        private static object? Unwrap(object? raw)
        {
            if (raw is not JsonElement element) return raw;

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => element.ToString()
            };
        }
    }
}
