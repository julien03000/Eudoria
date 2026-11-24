using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Eudoria.Core.Identity.Http;

/// <summary>
/// Reads a request body strictly: a JSON object of at most <see cref="MaxBodyBytes"/>, whose only
/// properties are the expected ones (case-sensitive), each a string or null, none repeated.
/// Anything else is rejected as a whole (null), without guessing. Absent fields are absent from the result.
/// </summary>
internal static class StrictJsonReader
{
    /// <summary>Far above the largest valid request (a 128-code-point password escaped as \uXXXX pairs).</summary>
    public const int MaxBodyBytes = 8 * 1024;

    public static async Task<IReadOnlyDictionary<string, string?>?> ReadStringFieldsAsync(
        HttpRequest request, IReadOnlySet<string> expectedFields, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType() || request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        byte[]? body = await ReadBodyAsync(request.Body, cancellationToken);
        return body is null ? null : Parse(body, expectedFields);
    }

    private static async Task<byte[]?> ReadBodyAsync(Stream body, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[4096];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Dictionary<string, string?>? Parse(byte[] body, IReadOnlySet<string> expectedFields)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            Dictionary<string, string?> fields = new(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                bool known = expectedFields.Contains(property.Name);
                bool typed = property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null;
                if (!known || !typed || !fields.TryAdd(property.Name, property.Value.GetString()))
                {
                    return null;
                }
            }

            return fields;
        }
        catch (JsonException)
        {
            return null; // not JSON
        }
        catch (InvalidOperationException)
        {
            return null; // a string that is not valid Unicode (e.g. a lone surrogate)
        }
    }
}
