using System.Text.Json.Nodes;
using Eudoria.Core.Api.Tests.Infrastructure;
using Microsoft.OpenApi;

namespace Eudoria.Core.Api.Tests;

/// <summary>The in-house validator must reject what it claims to check, or contract tests prove nothing.</summary>
public sealed class SchemaValidatorTests
{
    private static readonly OpenApiSchema Item = new()
    {
        Type = JsonSchemaType.Object,
        AdditionalPropertiesAllowed = false,
        Required = new HashSet<string> { "code" },
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create("a"), JsonValue.Create("b")] },
        },
    };

    private static readonly OpenApiSchema Schema = new()
    {
        Type = JsonSchemaType.Object,
        AdditionalPropertiesAllowed = false,
        Required = new HashSet<string> { "name", "count", "items" },
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["name"] = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 3, MaxLength = 5, Pattern = "^[a-z]+$" },
            ["count"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
            ["items"] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = Item },
        },
    };

    [Fact]
    public void ValidDocument_HasNoError() =>
        Assert.Empty(Validate("""{"name":"abcd","count":2,"items":[{"code":"a"},{"code":"b"}]}"""));

    [Theory]
    [InlineData("""{"count":2,"items":[]}""", "missing required property 'name'")]
    [InlineData("""{"name":"abcd","count":2,"items":[],"extra":1}""", "undocumented property 'extra'")]
    [InlineData("""{"name":"ab","count":2,"items":[]}""", "shorter than 3")]
    [InlineData("""{"name":"abcdef","count":2,"items":[]}""", "longer than 5")]
    [InlineData("""{"name":"ABCD","count":2,"items":[]}""", "does not match the pattern")]
    [InlineData("""{"name":"abcd","count":2.5,"items":[]}""", "expected an integer")]
    [InlineData("""{"name":"abcd","count":"2","items":[]}""", "expected an integer")]
    [InlineData("""{"name":"abcd","count":2,"items":{}}""", "expected an array")]
    [InlineData("""{"name":"abcd","count":2,"items":[{"code":"c"}]}""", "$.items[0].code: 'c' is not one of the documented values")]
    [InlineData("""{"name":"abcd","count":2,"items":[{}]}""", "$.items[0]: missing required property 'code'")]
    public void InvalidDocument_IsReported(string json, string expectedError) =>
        Assert.Contains(Validate(json), error => error.Contains(expectedError, StringComparison.Ordinal));

    [Fact]
    public void UnsupportedType_IsReported_NotSilentlyAccepted() =>
        Assert.Contains(SchemaValidator.Validate(JsonValue.Create(true), new OpenApiSchema { Type = JsonSchemaType.Boolean }),
            error => error.Contains("not supported", StringComparison.Ordinal));

    private static IReadOnlyList<string> Validate(string json) => SchemaValidator.Validate(JsonNode.Parse(json), Schema);
}
