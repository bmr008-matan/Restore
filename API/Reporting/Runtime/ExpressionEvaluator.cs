#nullable enable
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DynamicExpresso;
using DynamicExpresso.Exceptions;

namespace API.Reporting.Runtime
{
    /// <summary>
    /// Evaluates the C#-like expressions used by conditional rules, computed columns and group keys.
    ///
    /// Rather than handing the interpreter a dictionary and hoping member access works, references
    /// like <c>Fields.total</c> are rewritten to plain identifiers and declared with a real CLR type
    /// taken from the definition's own metadata. That buys three things: comparison and arithmetic
    /// operators resolve at parse time with normal C# semantics, nulls behave like C#'s lifted
    /// operators instead of throwing, and the parsed expression can be cached and reused across every
    /// row of a large report.
    /// </summary>
    public sealed class ExpressionEvaluator
    {
        /// <summary>
        /// Matches <c>Fields.name</c> and <c>Fields["name"]</c> for the three dictionary-backed scopes.
        /// <c>Report</c> is deliberately absent: it is a real typed object, so the interpreter resolves
        /// its members itself and things like <c>Report.User.IsInRole("Manager")</c> just work.
        /// </summary>
        private static readonly Regex ReferencePattern = new(
            """\b(?<scope>Fields|Params|Group)\s*(?:\.\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)|\[\s*"(?<quoted>[^"]+)"\s*\])""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ConcurrentDictionary<string, Lambda> _cache = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ParsedExpression> _rewriteCache = new(StringComparer.Ordinal);

        private readonly Interpreter _interpreter;

        /// <summary>
        /// Used only by <see cref="TryValidateSyntax"/>. At save time the data types are not known, so
        /// every reference has to be declared as object — and a strict interpreter rightly refuses
        /// "object &gt; 100". Late binding lets the parse succeed so that validation reports genuine
        /// syntax errors instead of failing every well-formed comparison.
        /// </summary>
        private readonly Interpreter _validationInterpreter;

        public ExpressionEvaluator()
        {
            _interpreter = CreateInterpreter(InterpreterOptions.SystemKeywords
                                             | InterpreterOptions.PrimitiveTypes
                                             | InterpreterOptions.CommonTypes);

            _validationInterpreter = CreateInterpreter(InterpreterOptions.SystemKeywords
                                                       | InterpreterOptions.PrimitiveTypes
                                                       | InterpreterOptions.CommonTypes
                                                       | InterpreterOptions.LateBindObject);
        }

        /// <summary>
        /// SystemKeywords is what registers the true, false and null literals — without it an
        /// expression as ordinary as "Fields.x == null" fails as an unknown identifier.
        ///
        /// Everything beyond primitives and common types is left out, and assignment is disabled
        /// outright: report definitions are authored data, so an expression must not be able to reach
        /// arbitrary types or mutate anything. This is a sandbox, not a scripting host.
        /// </summary>
        private static Interpreter CreateInterpreter(InterpreterOptions options)
        {
            var interpreter = new Interpreter(options).EnableAssignment(AssignmentOperators.None);

            // A small helper vocabulary, so authors do not have to write null-checking gymnastics.
            interpreter.SetFunction("IsNull", (object? v) => v is null);
            interpreter.SetFunction("IsBlank", (object? v) => v is null || string.IsNullOrWhiteSpace(v.ToString()));

            return interpreter;
        }

        /// <summary>
        /// Evaluates an expression expected to yield a boolean. Anything that is not a true boolean —
        /// null, a non-boolean value, a compile error — is reported as a diagnostic and treated as
        /// false, so one bad rule cannot take a whole report down.
        /// </summary>
        public bool EvaluateBool(string? expression, ExpressionScope scope, RenderDiagnostics diagnostics, string path)
        {
            if (string.IsNullOrWhiteSpace(expression)) return false;

            if (!TryEvaluate(expression, scope, diagnostics, path, out var value)) return false;

            return value switch
            {
                bool b => b,
                null => false,
                _ => ReportNonBoolean(value, diagnostics, path)
            };
        }

        private static bool ReportNonBoolean(object value, RenderDiagnostics diagnostics, string path)
        {
            diagnostics.Add(path,
                $"Expression returned {value.GetType().Name} where a true/false value was expected; treated as false.");
            return false;
        }

        /// <summary>Evaluates an expression for its value, e.g. a computed column or a group key.</summary>
        public object? EvaluateValue(string? expression, ExpressionScope scope, RenderDiagnostics diagnostics, string path)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;
            TryEvaluate(expression, scope, diagnostics, path, out var value);
            return value;
        }

        private bool TryEvaluate(
            string expression, ExpressionScope scope, RenderDiagnostics diagnostics, string path, out object? value)
        {
            value = null;
            try
            {
                var parsed = _rewriteCache.GetOrAdd(expression, Rewrite);

                var types = new Type[parsed.References.Count + 1];
                var args = new object?[parsed.References.Count + 1];

                for (var i = 0; i < parsed.References.Count; i++)
                {
                    var reference = parsed.References[i];
                    var raw = scope.Lookup(reference.Scope, reference.Name);
                    types[i] = ResolveType(reference, raw, scope);
                    args[i] = Coerce(raw, types[i]);
                }

                types[^1] = typeof(ReportContext);
                args[^1] = scope.Report;

                var lambda = _cache.GetOrAdd(CacheKey(parsed, types), _ => Parse(parsed, types));
                value = lambda.Invoke(args);
                return true;
            }
            catch (Exception ex) when (ex is ParseException or InvalidOperationException or FormatException
                                          or InvalidCastException or OverflowException or ArgumentException)
            {
                diagnostics.Add(path, $"Could not evaluate \"{expression}\": {ex.Message}");
                return false;
            }
        }

        private Lambda Parse(ParsedExpression parsed, Type[] types)
        {
            var parameters = new Parameter[types.Length];
            for (var i = 0; i < parsed.References.Count; i++)
                parameters[i] = new Parameter(parsed.References[i].Identifier, types[i]);
            parameters[^1] = new Parameter("Report", typeof(ReportContext));

            return _interpreter.Parse(parsed.Rewritten, parameters);
        }

        private static string CacheKey(ParsedExpression parsed, Type[] types)
        {
            // Types are part of the key because the same expression text can legitimately be parsed
            // against differently typed data sets within one report.
            var key = new System.Text.StringBuilder(parsed.Rewritten);
            foreach (var type in types) key.Append('|').Append(type.FullName);
            return key.ToString();
        }

        /// <summary>
        /// Declared type wins; otherwise fall back to the runtime value's type, made nullable so that
        /// a null in a later row cannot break an expression that parsed fine for earlier rows.
        /// </summary>
        private static Type ResolveType(ScopeReference reference, object? raw, ExpressionScope scope)
        {
            if (scope.DeclaredTypes.TryGetValue($"{reference.Scope}.{reference.Name}", out var declared))
                return declared;

            return raw is null ? typeof(object) : MakeNullable(raw.GetType());
        }

        private static Type MakeNullable(Type type) =>
            type.IsValueType && Nullable.GetUnderlyingType(type) is null
                ? typeof(Nullable<>).MakeGenericType(type)
                : type;

        /// <summary>
        /// Brings a raw provider value in line with its declared type. Databases are loose about this —
        /// Oracle NUMBER can arrive as decimal, long or string depending on the column — so coercing
        /// here keeps that variability out of every expression.
        /// </summary>
        private static object? Coerce(object? raw, Type target)
        {
            if (raw is null or DBNull) return null;

            var underlying = Nullable.GetUnderlyingType(target) ?? target;
            if (underlying == typeof(object) || underlying.IsInstanceOfType(raw)) return raw;

            try
            {
                if (underlying == typeof(string)) return Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (underlying.IsEnum) return Enum.Parse(underlying, raw.ToString() ?? "", ignoreCase: true);
                return Convert.ChangeType(raw, underlying, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                // Leave the value alone and let the interpreter complain with a clearer message than
                // a conversion failure buried here would give.
                return raw;
            }
        }

        /// <summary>
        /// Rewrites scope references into plain identifiers, recording each one so the caller knows
        /// what to bind. Two references to the same field collapse onto one identifier.
        /// </summary>
        private static ParsedExpression Rewrite(string expression)
        {
            var references = new List<ScopeReference>();
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var rewritten = ReferencePattern.Replace(expression, match =>
            {
                var scope = match.Groups["scope"].Value;
                var name = match.Groups["name"].Success
                    ? match.Groups["name"].Value
                    : match.Groups["quoted"].Value;

                var key = $"{scope}.{name}";
                if (seen.TryGetValue(key, out var existing)) return existing;

                var identifier = $"__{scope.ToLowerInvariant()}_{Sanitize(name)}_{references.Count}";
                seen[key] = identifier;
                references.Add(new ScopeReference(scope, name, identifier));
                return identifier;
            });

            return new ParsedExpression(rewritten, references);
        }

        /// <summary>Quoted field names may contain anything, so reduce them to identifier-safe text.</summary>
        private static string Sanitize(string name)
        {
            var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            return new string(chars);
        }

        /// <summary>
        /// Parses an expression without evaluating it, to validate a rule at save time. Types are not
        /// known then, so every reference is declared as object — which catches syntax errors and
        /// unknown identifiers, but not type mismatches.
        /// </summary>
        public bool TryValidateSyntax(string? expression, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(expression)) return true;

            try
            {
                var parsed = Rewrite(expression);
                var parameters = parsed.References
                    .Select(r => new Parameter(r.Identifier, typeof(object)))
                    .Append(new Parameter("Report", typeof(ReportContext)))
                    .ToArray();

                _validationInterpreter.Parse(parsed.Rewritten, parameters);
                return true;
            }
            catch (Exception ex) when (ex is ParseException or InvalidOperationException or ArgumentException)
            {
                error = ex.Message;
                return false;
            }
        }

        private sealed record ScopeReference(string Scope, string Name, string Identifier);

        private sealed record ParsedExpression(string Rewritten, List<ScopeReference> References);
    }
}
