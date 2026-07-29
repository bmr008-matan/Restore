#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace API.Reporting.Model
{
    /// <summary>
    /// The one set of serialiser options used for report definitions, everywhere. Persisted JSON and
    /// API payloads must agree exactly, otherwise a save-then-load round trip silently changes the
    /// template — so both go through here rather than through per-call options.
    /// </summary>
    public static class ReportJson
    {
        public static readonly JsonSerializerOptions Options = Create(indented: false);

        /// <summary>Indented variant, for storing readable definitions and for test snapshots.</summary>
        public static readonly JsonSerializerOptions Indented = Create(indented: true);

        private static JsonSerializerOptions Create(bool indented) => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            // Keeps stored definitions small and lets a later model version add a property with a
            // default without every existing template growing a null for it.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            // Definitions are hand-edited often enough that trailing commas and comments are worth
            // tolerating on the way in. They are never produced on the way out.
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static string Serialize(ReportDefinition definition, bool indented = false) =>
            JsonSerializer.Serialize(definition, indented ? Indented : Options);

        /// <summary>
        /// Throws <see cref="JsonException"/> on malformed JSON — callers turn that into a 400 rather
        /// than letting a broken definition reach the renderer.
        /// </summary>
        public static ReportDefinition Deserialize(string json) =>
            JsonSerializer.Deserialize<ReportDefinition>(json, Options)
            ?? throw new JsonException("Report definition deserialised to null.");

        public static bool TryDeserialize(string json, out ReportDefinition? definition, out string? error)
        {
            try
            {
                definition = Deserialize(json);
                error = null;
                return true;
            }
            catch (JsonException ex)
            {
                definition = null;
                error = ex.Message;
                return false;
            }
        }
    }
}
