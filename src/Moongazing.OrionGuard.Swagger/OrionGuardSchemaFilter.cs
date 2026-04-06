using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;
using Moongazing.OrionGuard.Attributes;

namespace Moongazing.OrionGuard.Swagger;

/// <summary>
/// Swagger schema filter that reads OrionGuard validation attributes
/// and populates OpenAPI schema constraints.
/// </summary>
public sealed class OrionGuardSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type is null) return;

        var properties = context.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var propertyName = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];

            if (!schema.Properties.TryGetValue(propertyName, out var propertySchema))
                continue;

            var attributes = property.GetCustomAttributes<ValidationAttribute>();

            foreach (var attribute in attributes)
            {
                switch (attribute)
                {
                    case NotNullAttribute:
                        schema.Required.Add(propertyName);
                        propertySchema.Nullable = false;
                        break;

                    case NotEmptyAttribute:
                        propertySchema.MinLength = 1;
                        break;

                    case LengthAttribute lengthAttr:
                        propertySchema.MinLength = lengthAttr.MinLength;
                        propertySchema.MaxLength = lengthAttr.MaxLength;
                        break;

                    case RangeAttribute rangeAttr:
                        propertySchema.Minimum = (decimal)rangeAttr.Minimum;
                        propertySchema.Maximum = (decimal)rangeAttr.Maximum;
                        break;

                    case EmailAttribute:
                        propertySchema.Format = "email";
                        break;

                    case RegexAttribute regexAttr:
                        propertySchema.Pattern = regexAttr.Pattern;
                        break;

                    case PositiveAttribute:
                        propertySchema.Minimum = 0;
                        propertySchema.ExclusiveMinimum = true;
                        break;
                }
            }
        }
    }
}
