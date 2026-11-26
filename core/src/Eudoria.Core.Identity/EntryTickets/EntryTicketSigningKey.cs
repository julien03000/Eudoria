using System.Text;
using System.Text.Json.Nodes;
using Eudoria.Core.Identity.Tokens;

namespace Eudoria.Core.Identity.EntryTickets;

/// <summary>
/// The entry ticket signing key: distinct from the access-token key (owner decision, plan 1.6, point 2),
/// "kid" = RFC 7638 thumbprint. Only its public part is ever published (<see cref="JwkSetJson"/>).
/// </summary>
internal sealed class EntryTicketSigningKey : IDisposable
{
    public EntryTicketSigningKey(TokenSigningKey key)
    {
        Key = key;
        // Built member by member from the public coordinates: the private part cannot slip in.
        JsonObject publicJwk = new()
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = key.PublicX,
            ["y"] = key.PublicY,
            ["kid"] = key.SecurityKey.KeyId,
            ["alg"] = "ES256",
            ["use"] = "sig",
        };
        JwkSetJson = Encoding.UTF8.GetBytes(new JsonObject { ["keys"] = new JsonArray(publicJwk) }.ToJsonString());
    }

    public TokenSigningKey Key { get; }

    /// <summary>RFC 7517 JWK Set (UTF-8) with the public key only (never "d").</summary>
    public byte[] JwkSetJson { get; }

    public void Dispose() => Key.Dispose();
}
