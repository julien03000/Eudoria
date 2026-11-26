using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Eudoria.ThirdParty.WorldVerifier.Tests;

/// <summary>Builds tickets by hand (no JWT library), signed by a key of the test, to try every rule.</summary>
internal sealed class TicketMinter : IDisposable
{
    public const string World = "sandbox";

    public static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    public TicketMinter(string? keyId = null)
    {
        Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = Key.ExportParameters(includePrivateParameters: false);
        X = Base64Url.EncodeToString(parameters.Q.X);
        Y = Base64Url.EncodeToString(parameters.Q.Y);
        KeyId = keyId ?? "test-key-" + X[..8];
    }

    public ECDsa Key { get; }

    public string KeyId { get; }

    public string X { get; }

    public string Y { get; }

    public Guid AccountId { get; } = Guid.NewGuid();

    public JsonObject Jwk() => new()
    {
        ["kty"] = "EC",
        ["crv"] = "P-256",
        ["x"] = X,
        ["y"] = Y,
        ["kid"] = KeyId,
        ["alg"] = "ES256",
        ["use"] = "sig",
    };

    public string JwkSet() => new JsonObject { ["keys"] = new JsonArray(Jwk()) }.ToJsonString();

    public JsonObject Header() => new() { ["alg"] = "ES256", ["typ"] = "eudoria-entry+jwt", ["kid"] = KeyId };

    public JsonObject Claims(DateTimeOffset? issuedAt = null)
    {
        long iat = (issuedAt ?? Now).ToUnixTimeSeconds();
        return new JsonObject
        {
            ["iss"] = "eudoria-core",
            ["sub"] = AccountId.ToString(),
            ["aud"] = "eudoria-world:" + World,
            ["iat"] = iat,
            ["exp"] = iat + 60,
            ["jti"] = Guid.NewGuid().ToString(),
        };
    }

    /// <summary>A valid ticket, after optional changes to its header and claims.</summary>
    public string Mint(Action<JsonObject>? header = null, Action<JsonObject>? claims = null, DateTimeOffset? issuedAt = null)
    {
        JsonObject headerJson = Header();
        JsonObject claimsJson = Claims(issuedAt);
        header?.Invoke(headerJson);
        claims?.Invoke(claimsJson);
        return Sign(headerJson.ToJsonString(), claimsJson.ToJsonString());
    }

    /// <summary>Signs raw header and payload JSON texts (to write duplicates or odd forms).</summary>
    public string Sign(string headerJson, string payloadJson)
    {
        string signingInput = Encode(headerJson) + "." + Encode(payloadJson);
        byte[] signature = Key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return signingInput + "." + Base64Url.EncodeToString(signature);
    }

    /// <summary>Signs raw header and payload bytes (to write invalid UTF-8).</summary>
    public string SignBytes(byte[] header, byte[] payload)
    {
        string signingInput = Base64Url.EncodeToString(header) + "." + Base64Url.EncodeToString(payload);
        byte[] signature = Key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return signingInput + "." + Base64Url.EncodeToString(signature);
    }

    public static string Encode(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

    public IReadOnlyDictionary<string, ECParameters> Keys() =>
        new Dictionary<string, ECParameters> { [KeyId] = Key.ExportParameters(includePrivateParameters: false) };

    public void Dispose() => Key.Dispose();
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan delta) => Now += delta;
}

/// <summary>Plays the core's key route: serves a JWK Set, or fails, and counts the requests.</summary>
internal sealed class FakeKeysServer : HttpMessageHandler
{
    public string? JwkSet { get; set; }

    public bool Unreachable { get; set; }

    public int Requests { get; private set; }

    public static readonly Uri KeysUrl = new("https://core.test/v1/entry-tickets/keys");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (Unreachable)
        {
            throw new HttpRequestException("connection refused (test)");
        }

        return Task.FromResult(JwkSet is null
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JwkSet, Encoding.UTF8, "application/jwk-set+json") });
    }
}
