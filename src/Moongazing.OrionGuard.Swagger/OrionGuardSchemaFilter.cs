using System.Globalization;
using System.Reflection;
using Microsoft.OpenApi;
using Moongazing.OrionGuard.Attributes;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Moongazing.OrionGuard.Swagger;

/// <summary>
/// Swagger schema filter that reads OrionGuard validation attributes
/// and populates OpenAPI schema constraints.
/// </summary>
public sealed class OrionGuardSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        // Microsoft.OpenApi 2.x hands filters the read-only interface; only concrete schemas are mutable.
        if (context.Type is null || schema is not OpenApiSchema target || target.Properties is null) return;

        var properties = context.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var propertyName = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(property.Name);

            if (!target.Properties.TryGetValue(propertyName, out var candidate))
                continue;

            var attributes = property.GetCustomAttributes<ValidationAttribute>();

            foreach (var attribute in attributes)
            {
                // "required" lives on the parent schema, so it applies even when the property is a $ref.
                if (attribute is NotNullAttribute)
                    (target.Required ??= new HashSet<string>()).Add(propertyName);

                // $ref property schemas (OpenApiSchemaReference) point at shared components and must not be mutated.
                if (candidate is not OpenApiSchema propertySchema)
                    continue;

                switch (attribute)
                {
                    case NotNullAttribute:
                        // OpenAPI 3.1 models nullability as a "null" type flag instead of a Nullable property.
                        if (propertySchema.Type is { } type)
                            propertySchema.Type = type & ~JsonSchemaType.Null;
                        break;

                    case NotEmptyAttribute:
                        propertySchema.MinLength = 1;
                        break;

                    case LengthAttribute lengthAttr:
                        propertySchema.MinLength = lengthAttr.MinLength;
                        propertySchema.MaxLength = lengthAttr.MaxLength;
                        break;

                    case RangeAttribute rangeAttr:
                        // Bounds are strings in Microsoft.OpenApi 2.x; "R" keeps the double round-trippable.
                        propertySchema.Minimum = rangeAttr.Minimum.ToString("R", CultureInfo.InvariantCulture);
                        propertySchema.Maximum = rangeAttr.Maximum.ToString("R", CultureInfo.InvariantCulture);
                        break;

                    case EmailAttribute:
                        propertySchema.Format = "email";
                        break;

                    case RegexAttribute regexAttr:
                        propertySchema.Pattern = regexAttr.Pattern;
                        break;

                    case PositiveAttribute:
                        // JSON Schema 2020-12: exclusiveMinimum carries the bound itself, not a boolean flag.
                        propertySchema.ExclusiveMinimum = "0";
                        break;
                }
            }
        }
    }
}
