using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Eudoria.ThirdParty.WorldVerifier.Tests;

/// <summary>Every rule of the third-party verifier (plan 1.6, section 12), against tickets built by hand.</summary>
public sealed class WorldTicketVerifierTests : IDisposable
{
    private readonly TicketMinter _minter = new();
    private readonly ManualTimeProvider _clock = new(TicketMinter.Now);
    private readonly FakeKeysServer _core = new();
    private readonly HttpClient _http;
    private readonly EntryTicketKeyStore _keys;

    public WorldTicketVerifierTests()
    {
        _core.JwkSet = _minter.JwkSet();
        _http = new HttpClient(_core);
        _keys = new EntryTicketKeyStore(_http, FakeKeysServer.KeysUrl, _clock);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ---------- Accepted, and refused for the right reason ----------

    [Fact]
    public async Task ValidTicket_IsAccepted_WithTheAccountId()
    {
        TicketVerdict verdict = await VerifyAsync(_minter.Mint());

        Assert.True(verdict.IsAccepted, verdict.ToString());
        Assert.Equal(_minter.AccountId, verdict.AccountId);
    }

    public static TheoryData<string, string> Forgeries() => new()
    {
        { "other-world", TicketRejections.AudienceInvalid },
        { "aud-as-list", TicketRejections.AudienceInvalid },
        { "aud-missing", TicketRejections.AudienceInvalid },
        { "other-issuer", TicketRejections.IssuerInvalid },
        { "access-token", TicketRejections.TypeNotAllowed },
        { "typ-missing", TicketRejections.TypeNotAllowed },
        { "typ-generic-jwt", TicketRejections.TypeNotAllowed },
        { "typ-other-case", TicketRejections.TypeNotAllowed },
        { "alg-none", TicketRejections.AlgorithmNotAllowed },
        { "alg-hs256", TicketRejections.AlgorithmNotAllowed },
        { "alg-es384", TicketRejections.AlgorithmNotAllowed },
        { "tampered-payload", TicketRejections.SignatureInvalid },
        { "tampered-signature", TicketRejections.SignatureInvalid },
        { "signature-der", TicketRejections.SignatureInvalid },
        { "signature-empty", TicketRejections.SignatureInvalid },
        { "signed-by-another-key-same-kid", TicketRejections.SignatureInvalid },
        { "header-extra-member", TicketRejections.Malformed },
        { "header-jwk-member", TicketRejections.Malformed },
        { "kid-missing", TicketRejections.Malformed },
        { "duplicate-claim", TicketRejections.Malformed },
        { "two-parts", TicketRejections.Malformed },
        { "base64-padding", TicketRejections.Malformed },
        { "not-json", TicketRejections.Malformed },
        { "sub-not-uuid", TicketRejections.ClaimsInvalid },
        { "sub-upper-case", TicketRejections.ClaimsInvalid },
        { "jti-missing", TicketRejections.ClaimsInvalid },
        { "exp-as-text", TicketRejections.ClaimsInvalid },
        { "exp-fractional", TicketRejections.ClaimsInvalid },
        { "exp-before-iat", TicketRejections.ClaimsInvalid },
    };

    [Theory]
    [MemberData(nameof(Forgeries))]
    public async Task ForgedOrForeignTicket_IsRefused_ForTheRightReason(string kind, string expected)
    {
        using TicketMinter impostor = new(_minter.KeyId);
        string valid = _minter.Mint();
        string ticket = kind switch
        {
            "other-world" => _minter.Mint(claims: c => c["aud"] = "eudoria-world:other"),
            "aud-as-list" => _minter.Mint(claims: c => c["aud"] = new JsonArray("eudoria-world:sandbox")),
            "aud-missing" => _minter.Mint(claims: c => c.Remove("aud")),
            "other-issuer" => _minter.Mint(claims: c => c["iss"] = "someone-else"),
            "access-token" => _minter.Mint(header: h => h["typ"] = "eudoria-access+jwt", claims: c => c["aud"] = "eudoria-core"),
            "typ-missing" => _minter.Mint(header: h => h.Remove("typ")),
            "typ-generic-jwt" => _minter.Mint(header: h => h["typ"] = "JWT"),
            "typ-other-case" => _minter.Mint(header: h => h["typ"] = "Eudoria-Entry+JWT"),
            "alg-none" => $"{TicketMinter.Encode($$"""{"alg":"none","typ":"eudoria-entry+jwt","kid":"{{_minter.KeyId}}"}""")}.{valid.Split('.')[1]}.",
            "alg-hs256" => HmacWithThePublicKey(valid),
            "alg-es384" => _minter.Mint(header: h => h["alg"] = "ES384"),
            "tampered-payload" => ReplacePart(valid, 1, TicketMinter.Encode(_minter.Claims().ToJsonString())),
            "tampered-signature" => FlipSignatureBit(valid),
            "signature-der" => ReplacePart(valid, 2, Base64Url.EncodeToString(DerSignature(valid))),
            "signature-empty" => ReplacePart(valid, 2, string.Empty),
            "signed-by-another-key-same-kid" => impostor.Mint(),
            "header-extra-member" => _minter.Mint(header: h => h["cty"] = "JWT"),
            "header-jwk-member" => _minter.Mint(header: h => h["jku"] = "https://attacker.example/keys"),
            "kid-missing" => _minter.Mint(header: h => h.Remove("kid")),
            "duplicate-claim" => _minter.Sign(_minter.Header().ToJsonString(),
                _minter.Claims().ToJsonString().Replace("{\"iss\"", "{\"sub\":\"" + Guid.NewGuid() + "\",\"iss\"", StringComparison.Ordinal)),
            "two-parts" => string.Join('.', valid.Split('.')[..2]),
            "base64-padding" => ReplacePart(valid, 2, Convert.ToBase64String(Base64Url.DecodeFromChars(valid.Split('.')[2])).Replace('+', '-').Replace('/', '_') + "=="),
            "not-json" => _minter.Sign(_minter.Header().ToJsonString(), "not json"),
            "sub-not-uuid" => _minter.Mint(claims: c => c["sub"] = "ana"),
            "sub-upper-case" => _minter.Mint(claims: c => c["sub"] = _minter.AccountId.ToString().ToUpperInvariant()),
            "jti-missing" => _minter.Mint(claims: c => c.Remove("jti")),
            "exp-as-text" => _minter.Mint(claims: c => c["exp"] = c["exp"]!.GetValue<long>().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "exp-fractional" => _minter.Mint(claims: c => c["exp"] = c["exp"]!.GetValue<long>() + 0.5),
            "exp-before-iat" => _minter.Mint(claims: c => c["exp"] = c["iat"]!.GetValue<long>()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        TicketVerdict verdict = await VerifyAsync(ticket);

        Assert.Equal(expected, verdict.Rejection);
    }

    // ---------- Time ----------

    [Theory]
    [InlineData(75, "accepted")] // exp (60 s) + 15 s of tolerance: still accepted
    [InlineData(76, TicketRejections.Expired)]
    [InlineData(-15, "accepted")] // issued 15 s "in the future" (clock difference): accepted
    [InlineData(-16, TicketRejections.IssuedInTheFuture)]
    public async Task ClockTolerance_IsFifteenSeconds(int secondsAfterIssue, string expected)
    {
        string ticket = _minter.Mint();
        _clock.Now = TicketMinter.Now.AddSeconds(secondsAfterIssue);

        TicketVerdict verdict = await VerifyAsync(ticket);

        Assert.Equal(expected, verdict.ToString());
    }

    // ---------- Single use ----------

    [Fact]
    public async Task Ticket_IsAcceptedOnce_ThenRefusedAsAReplay()
    {
        WorldTicketVerifier verifier = NewVerifier();
        string ticket = _minter.Mint();

        TicketVerdict first = await verifier.VerifyAsync(ticket, Cancellation);
        TicketVerdict second = await verifier.VerifyAsync(ticket, Cancellation);

        Assert.True(first.IsAccepted);
        Assert.Equal(TicketRejections.Replayed, second.Rejection);
    }

    [Fact]
    public async Task RefusedTicket_DoesNotUseUpItsJti()
    {
        ReplayCache replays = new();
        WorldTicketVerifier sandbox = NewVerifier(replays: replays);
        string forOtherWorld = _minter.Mint(claims: c => c["aud"] = "eudoria-world:other");

        TicketVerdict refused = await sandbox.VerifyAsync(forOtherWorld, Cancellation);

        Assert.Equal(TicketRejections.AudienceInvalid, refused.Rejection);
        Assert.Equal(0, replays.Count);
    }

    [Fact]
    public void ReplayMemory_ForgetsATicketOnceItCanNoLongerBeAccepted()
    {
        ReplayCache replays = new();
        long now = TicketMinter.Now.ToUnixTimeSeconds();

        Assert.Equal(ReplayOutcome.FirstUse, replays.TryRemember("a", forgetAfterUnixSeconds: now + 75, now));
        Assert.Equal(ReplayOutcome.Replayed, replays.TryRemember("a", now + 75, now + 75));
        Assert.Equal(ReplayOutcome.FirstUse, replays.TryRemember("b", now + 151, now + 76)); // "a" forgotten here
        Assert.Equal(1, replays.Count);
    }

    [Fact]
    public async Task FullReplayMemory_RefusesNewTickets_RatherThanRiskingAReplay()
    {
        WorldTicketVerifier verifier = NewVerifier(replays: new ReplayCache(capacity: 2));

        TicketVerdict first = await verifier.VerifyAsync(_minter.Mint(), Cancellation);
        TicketVerdict second = await verifier.VerifyAsync(_minter.Mint(), Cancellation);
        TicketVerdict third = await verifier.VerifyAsync(_minter.Mint(), Cancellation);
        _clock.Advance(TimeSpan.FromSeconds(76)); // the first two can be forgotten now
        TicketVerdict later = await verifier.VerifyAsync(_minter.Mint(issuedAt: _clock.Now), Cancellation);

        Assert.True(first.IsAccepted && second.IsAccepted);
        Assert.Equal(TicketRejections.ReplayCacheFull, third.Rejection);
        Assert.True(later.IsAccepted, later.ToString());
    }

    // ---------- Keys: download, refresh, unknown kid, core unreachable ----------

    [Fact]
    public async Task UnknownKid_TriggersOneDownload_ThenNoMoreThanOneAMinute()
    {
        WorldTicketVerifier verifier = NewVerifier();
        Assert.True(await _keys.RefreshAsync(Cancellation)); // start-up
        using TicketMinter stranger = new();
        TicketVerdict rightAfterStartUp = await verifier.VerifyAsync(stranger.Mint(), Cancellation);
        int afterStartUp = _core.Requests;
        _clock.Advance(TimeSpan.FromMinutes(1));
        using TicketMinter rotated = new();

        TicketVerdict first = await verifier.VerifyAsync(rotated.Mint(issuedAt: _clock.Now), Cancellation);
        TicketVerdict second = await verifier.VerifyAsync(rotated.Mint(issuedAt: _clock.Now), Cancellation);
        _clock.Advance(TimeSpan.FromMinutes(1));
        _core.JwkSet = new JsonObject { ["keys"] = new JsonArray(_minter.Jwk(), rotated.Jwk()) }.ToJsonString(); // the core publishes the new key
        TicketVerdict afterAMinute = await verifier.VerifyAsync(rotated.Mint(issuedAt: _clock.Now), Cancellation);

        // The keys were downloaded less than a minute before: no new download for an unknown kid.
        Assert.Equal(TicketRejections.KeyUnknown, rightAfterStartUp.Rejection);
        Assert.Equal(1, afterStartUp);
        Assert.Equal(TicketRejections.KeyUnknown, first.Rejection);
        Assert.Equal(TicketRejections.KeyUnknown, second.Rejection);
        Assert.True(afterAMinute.IsAccepted, afterAMinute.ToString());
        Assert.Equal(afterStartUp + 2, _core.Requests); // one for "first", none for "second", one a minute later
    }

    [Fact]
    public async Task KnownKeys_AreUsedOffline_AndRefreshedAfterFiveMinutes()
    {
        WorldTicketVerifier verifier = NewVerifier();
        await _keys.RefreshAsync(Cancellation);

        for (int i = 0; i < 5; i++)
        {
            Assert.True((await verifier.VerifyAsync(_minter.Mint(), Cancellation)).IsAccepted);
        }

        int offline = _core.Requests;
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await verifier.VerifyAsync(_minter.Mint(issuedAt: _clock.Now), Cancellation)).IsAccepted);

        Assert.Equal(1, offline);
        Assert.Equal(2, _core.Requests);
    }

    [Fact]
    public async Task CoreUnreachable_TheKeysAlreadyKnownKeepWorking()
    {
        WorldTicketVerifier verifier = NewVerifier();
        await _keys.RefreshAsync(Cancellation);
        _core.Unreachable = true;
        _clock.Advance(TimeSpan.FromMinutes(10)); // keys are old: a refresh is tried, and fails

        TicketVerdict verdict = await verifier.VerifyAsync(_minter.Mint(issuedAt: _clock.Now), Cancellation);

        Assert.True(verdict.IsAccepted, verdict.ToString());
        Assert.Equal(2, _core.Requests);
    }

    [Fact]
    public async Task NoKeyEverDownloaded_EveryTicketIsRefused()
    {
        _core.Unreachable = true;
        WorldTicketVerifier verifier = NewVerifier();

        Assert.False(await _keys.RefreshAsync(Cancellation));
        TicketVerdict verdict = await verifier.VerifyAsync(_minter.Mint(), Cancellation);

        Assert.Equal(TicketRejections.KeysUnavailable, verdict.Rejection);
    }

    [Theory]
    [InlineData("private-part")]
    [InlineData("other-curve")]
    [InlineData("other-alg")]
    [InlineData("encryption-key")]
    [InlineData("short-x")]
    [InlineData("point-off-curve")]
    public void JwkSet_KeepsOnlyUsableEs256PublicKeys(string kind)
    {
        JsonObject bad = _minter.Jwk();
        bad["kid"] = "bad";
        switch (kind)
        {
            case "private-part": bad["d"] = Base64Url.EncodeToString(_minter.Key.ExportParameters(true).D); break;
            case "other-curve": bad["crv"] = "P-384"; break;
            case "other-alg": bad["alg"] = "RS256"; break;
            case "encryption-key": bad["use"] = "enc"; break;
            case "short-x": bad["x"] = Base64Url.EncodeToString(new byte[31]); break;
            case "point-off-curve": bad["y"] = Base64Url.EncodeToString(new byte[32]); break;
        }

        string json = new JsonObject { ["keys"] = new JsonArray(_minter.Jwk(), bad) }.ToJsonString();
        Dictionary<string, ECParameters>? keys = EntryTicketKeyStore.ParseJwkSet(Encoding.UTF8.GetBytes(json));

        Assert.Equal([_minter.KeyId], keys!.Keys);
    }

    // ---------- Independence from the core ----------

    [Fact]
    public void Verifier_ReferencesNeitherTheCoreNorAJwtLibrary()
    {
        string[] references = [.. typeof(WorldTicketVerifier).Assembly.GetReferencedAssemblies().Select(name => name.Name!)];

        Assert.DoesNotContain(references, name => name.StartsWith("Eudoria.Core", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.Contains("IdentityModel", StringComparison.Ordinal) || name.Contains("Jwt", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _keys.Dispose();
        _http.Dispose();
        _core.Dispose();
        _minter.Dispose();
    }

    // ---------- Helpers ----------

    private WorldTicketVerifier NewVerifier(ReplayCache? replays = null) =>
        new(TicketMinter.World, _keys, replays ?? new ReplayCache(), _clock);

    private Task<TicketVerdict> VerifyAsync(string ticket) => NewVerifier().VerifyAsync(ticket, Cancellation);

    private static string ReplacePart(string ticket, int index, string part)
    {
        string[] parts = ticket.Split('.');
        parts[index] = part;
        return string.Join('.', parts);
    }

    private static string FlipSignatureBit(string ticket)
    {
        byte[] signature = Base64Url.DecodeFromChars(ticket.Split('.')[2]);
        signature[10] ^= 0x01;
        return ReplacePart(ticket, 2, Base64Url.EncodeToString(signature));
    }

    /// <summary>The same signature in DER format (what some libraries produce): must not pass as r||s.</summary>
    private byte[] DerSignature(string ticket)
    {
        string signingInput = ticket[..ticket.LastIndexOf('.')];
        return _minter.Key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    /// <summary>The classic "algorithm confusion": HS256 with the public key as the HMAC secret.</summary>
    private string HmacWithThePublicKey(string ticket)
    {
        string header = TicketMinter.Encode($$"""{"alg":"HS256","typ":"eudoria-entry+jwt","kid":"{{_minter.KeyId}}"}""");
        string signingInput = header + "." + ticket.Split('.')[1];
        byte[] secret = Encoding.ASCII.GetBytes(_minter.JwkSet());
        return signingInput + "." + Base64Url.EncodeToString(HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(signingInput)));
    }
}
