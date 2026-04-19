using System.Text.Json;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Guards for validating API responses and data contracts.
/// Ensures objects meet expected structural requirements.
/// </summary>
public static class ApiContractGuards
{
    /// <summary>
    /// Validates that an object has all required properties set (non-null).
    /// Usage: response.AgainstMissingRequiredFields("Id", "Name", "Email")
    /// </summary>
    public static void AgainstMissingRequiredFields<T>(this T instance, string parameterName, params string[] requiredFieldNames) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = typeof(T);
        var missing = new List<string>();

        foreach (var fieldName in requiredFieldNames)
        {
            var property = type.GetProperty(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property is null)
            {
                missing.Add(fieldName);
                continue;
            }

            var value = property.GetValue(instance);
            if (value is null || (value is string s && string.IsNullOrWhiteSpace(s)))
                missing.Add(fieldName);
        }

        if (missing.Count > 0)
            throw new ArgumentException($"{parameterName} is missing required fields: {string.Join(", ", missing)}.", parameterName);
    }

    /// <summary>
    /// Validates that specified properties are NOT null on the object.
    /// </summary>
    public static void AgainstUnexpectedNullFields<T>(this T instance, string parameterName, params string[] fieldNames) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = typeof(T);
        var nullFields = new List<string>();

        foreach (var fieldName in fieldNames)
        {
            var property = type.GetProperty(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property is null) continue;

            if (property.GetValue(instance) is null)
                nullFields.Add(fieldName);
        }

        if (nullFields.Count > 0)
            throw new ArgumentException($"{parameterName} has unexpected null fields: {string.Join(", ", nullFields)}.", parameterName);
    }

    /// <summary>
    /// Validates a JSON string against required top-level properties.
    /// Useful for validating API responses before deserialization.
    /// </summary>
    public static void AgainstMissingJsonFields(this string json, string parameterName, params string[] requiredFields)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException($"{parameterName} JSON cannot be empty.", parameterName);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"{parameterName} must be a JSON object.", parameterName);

            var missing = new List<string>();
            foreach (var field in requiredFields)
            {
                if (!doc.RootElement.TryGetProperty(field, out var prop) ||
                    prop.ValueKind == JsonValueKind.Null ||
                    prop.ValueKind == JsonValueKind.Undefined)
                {
                    missing.Add(field);
                }
            }

            if (missing.Count > 0)
                throw new ArgumentException($"{parameterName} JSON is missing required fields: {string.Join(", ", missing)}.", parameterName);
        }
    }

    /// <summary>
    /// Validates that a JSON response has the expected HTTP-like structure.
    /// Checks for common response envelope patterns (data, errors, status).
    /// </summary>
    public static void AgainstInvalidApiResponse(this string json, string parameterName, bool requireDataField = true)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException($"{parameterName} response cannot be empty.", parameterName);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"{parameterName} response must be a JSON object.", parameterName);

            if (requireDataField &&
                !doc.RootElement.TryGetProperty("data", out _) &&
                !doc.RootElement.TryGetProperty("Data", out _) &&
                !doc.RootElement.TryGetProperty("result", out _) &&
                !doc.RootElement.TryGetProperty("Result", out _))
            {
                throw new ArgumentException($"{parameterName} response must contain a 'data' or 'result' field.", parameterName);
            }
        }
    }

    /// <summary>
    /// Validates that a collection response is not empty and within expected bounds.
    /// </summary>
    public static void AgainstEmptyApiResponse<T>(this IEnumerable<T>? collection, string parameterName, int? maxExpected = null)
    {
        if (collection is null)
            throw new ArgumentException($"{parameterName} response cannot be null.", parameterName);

        var list = collection as ICollection<T> ?? collection.ToList();
        if (list.Count == 0)
            throw new ArgumentException($"{parameterName} response returned no results.", parameterName);

        if (maxExpected.HasValue && list.Count > maxExpected.Value)
            throw new ArgumentException($"{parameterName} response returned {list.Count} items, exceeding the expected maximum of {maxExpected.Value}.", parameterName);
    }

    /// <summary>
    /// Validates response fields match expected types.
    /// Usage: response.AgainstSchemaViolation("response", ("id", typeof(int)), ("name", typeof(string)))
    /// </summary>
    public static void AgainstSchemaViolation<T>(this T instance, string parameterName, params (string FieldName, Type ExpectedType)[] schema) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = typeof(T);
        var violations = new List<string>();

        foreach (var (fieldName, expectedType) in schema)
        {
            var property = type.GetProperty(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property is null)
            {
                violations.Add($"'{fieldName}' not found");
                continue;
            }

            if (!expectedType.IsAssignableFrom(property.PropertyType))
                violations.Add($"'{fieldName}' expected {expectedType.Name} but got {property.PropertyType.Name}");
        }

        if (violations.Count > 0)
            throw new ArgumentException($"{parameterName} schema violations: {string.Join("; ", violations)}.", parameterName);
    }
}
