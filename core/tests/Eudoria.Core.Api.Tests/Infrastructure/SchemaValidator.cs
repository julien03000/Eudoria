using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>
/// Minimal JSON Schema check of a JSON value against the contract. It supports only the keywords
/// the contract uses (type object/array/string/integer, properties, required,
/// additionalProperties: false, items, enum, minLength, maxLength, pattern) and reports any other
/// type as an error, so a richer contract can never be silently half-checked.
/// Decision of step 1.2: kept in-house rather than adding a JSON Schema library.
/// </summary>
internal static class SchemaValidator
{
    public static IReadOnlyList<string> Validate(JsonNode? value, IOpenApiSchema schema)
    {
        List<string> errors = [];
        Validate(value, schema, "$", errors);
        return errors;
    }

    private static void Validate(JsonNode? value, IOpenApiSchema schema, string path, List<string> errors)
    {
        switch (schema.Type)
        {
            case JsonSchemaType.Object:
                ValidateObject(value, schema, path, errors);
                break;
            case JsonSchemaType.Array:
                ValidateArray(value, schema, path, errors);
                break;
            case JsonSchemaType.String:
                ValidateString(value, schema, path, errors);
                break;
            case JsonSchemaType.Integer:
                ValidateInteger(value, path, errors);
                break;
            default:
                errors.Add($"{path}: schema type '{schema.Type}' is not supported by the test validator.");
                break;
        }
    }

    private static void ValidateObject(JsonNode? value, IOpenApiSchema schema, string path, List<string> errors)
    {
        if (value is not JsonObject json)
        {
            errors.Add($"{path}: expected an object.");
            return;
        }

        IDictionary<string, IOpenApiSchema> properties = schema.Properties ?? new Dictionary<string, IOpenApiSchema>();
        foreach (string required in schema.Required ?? new HashSet<string>())
        {
            if (!json.ContainsKey(required))
            {
                errors.Add($"{path}: missing required property '{required}'.");
            }
        }

        foreach (KeyValuePair<string, JsonNode?> property in json)
        {
            if (properties.TryGetValue(property.Key, out IOpenApiSchema? propertySchema))
            {
                Validate(property.Value, propertySchema, $"{path}.{property.Key}", errors);
            }
            else if (!schema.AdditionalPropertiesAllowed)
            {
                errors.Add($"{path}: undocumented property '{property.Key}'.");
            }
        }
    }

    private static void ValidateArray(JsonNode? value, IOpenApiSchema schema, string path, List<string> errors)
    {
        if (value is not JsonArray json)
        {
            errors.Add($"{path}: expected an array.");
            return;
        }

        if (schema.Items is null)
        {
            errors.Add($"{path}: array schema without 'items' is not supported by the test validator.");
            return;
        }

        for (int index = 0; index < json.Count; index++)
        {
            Validate(json[index], schema.Items, $"{path}[{index}]", errors);
        }
    }

    private static void ValidateString(JsonNode? value, IOpenApiSchema schema, string path, List<string> errors)
    {
        if (value is not JsonValue json || json.GetValueKind() != JsonValueKind.String)
        {
            errors.Add($"{path}: expected a string.");
            return;
        }

        string text = json.GetValue<string>();
        if (schema.Enum is { Count: > 0 } allowed && !allowed.Any(candidate => candidate?.GetValue<string>() == text))
        {
            errors.Add($"{path}: '{text}' is not one of the documented values.");
        }

        // JSON Schema counts lengths in Unicode code points.
        int length = text.EnumerateRunes().Count();
        if (length < schema.MinLength)
        {
            errors.Add($"{path}: shorter than {schema.MinLength} characters.");
        }

        if (length > schema.MaxLength)
        {
            errors.Add($"{path}: longer than {schema.MaxLength} characters.");
        }

        if (schema.Pattern is not null && !Regex.IsMatch(text, schema.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            errors.Add($"{path}: does not match the pattern '{schema.Pattern}'.");
        }
    }

    private static void ValidateInteger(JsonNode? value, string path, List<string> errors)
    {
        if (value is not JsonValue json || json.GetValueKind() != JsonValueKind.Number || !json.TryGetValue(out long _))
        {
            errors.Add($"{path}: expected an integer.");
        }
    }
}
