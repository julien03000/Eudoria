using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>
/// Minimal JSON Schema check of a response body against the contract. It supports only the
/// keywords the contract uses today (type object/string, properties, required,
/// additionalProperties: false, enum) and reports anything else as an error, so a richer
/// contract can never be silently half-checked. Planned replacement: a full JSON Schema
/// library once the schemas grow (step 1.2).
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
            case JsonSchemaType.String:
                ValidateString(value, schema, path, errors);
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
    }
}
