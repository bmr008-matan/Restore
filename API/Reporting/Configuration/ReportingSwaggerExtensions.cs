#nullable enable
using System.Reflection;
using System.Text.Json.Serialization;
using API.Reporting.Model.Elements;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace API.Reporting.Configuration
{
    /// <summary>
    /// Teaches Swashbuckle about the reporting model's polymorphism.
    ///
    /// System.Text.Json handles <see cref="ReportElement"/> through <c>[JsonPolymorphic]</c> and
    /// <c>[JsonDerivedType]</c>, but Swashbuckle does not read those attributes. Without this the
    /// generated OpenAPI document describes a band's elements as a bare base type with no discriminator
    /// and no subtypes — so an integrator reading the docs cannot tell what to send, and Swagger UI's
    /// "Try it out" produces a body the API rejects.
    ///
    /// The subtypes and their discriminator values are read back off the attributes themselves rather
    /// than listed again here, so the documentation cannot drift from the actual wire contract when a new
    /// element type is added.
    /// </summary>
    public static class ReportingSwaggerExtensions
    {
        public static void AddReportingPolymorphism(this SwaggerGenOptions options)
        {
            var map = BuildDiscriminatorMap(typeof(ReportElement));

            options.UseOneOfForPolymorphism();
            options.UseAllOfForInheritance();

            options.SelectSubTypesUsing(baseType =>
                baseType == typeof(ReportElement)
                    ? map.Keys
                    : Enumerable.Empty<Type>());

            options.SelectDiscriminatorNameUsing(baseType =>
                baseType == typeof(ReportElement) ? DiscriminatorPropertyName(baseType) : null);

            options.SelectDiscriminatorValueUsing(subType =>
                map.TryGetValue(subType, out var discriminator) ? discriminator : null);
        }

        /// <summary>Derived type to its JSON discriminator string, taken from the attributes.</summary>
        private static Dictionary<Type, string> BuildDiscriminatorMap(Type baseType)
        {
            var map = new Dictionary<Type, string>();

            foreach (var attribute in baseType.GetCustomAttributes<JsonDerivedTypeAttribute>())
            {
                // Only string discriminators are used by this model; anything else would not round-trip
                // through the API's JSON options, so it is skipped rather than guessed at.
                if (attribute.TypeDiscriminator is string discriminator)
                    map[attribute.DerivedType] = discriminator;
            }

            return map;
        }

        private static string DiscriminatorPropertyName(Type baseType) =>
            baseType.GetCustomAttribute<JsonPolymorphicAttribute>()?.TypeDiscriminatorPropertyName ?? "$type";
    }
}
