using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Eudoria.ThirdParty.WorldVerifier;

/// <summary>
/// The core's public ticket keys, as a world server keeps them (plan 1.6, section 6): downloaded at
/// start-up (<see cref="RefreshAsync"/>), downloaded again when they are 5 minutes old or when a
/// ticket names an unknown kid — never more than once a minute, so forged tickets cannot cause a flood
/// of requests. When the core cannot be reached, the keys already known are kept.
/// </summary>
public sealed class EntryTicketKeyStore(HttpClient http, Uri keysUrl, TimeProvider time) : IDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinDownloadInterval = TimeSpan.FromMinutes(1);
    public const int MaxDocumentBytes = 64 * 1024;

    private readonly SemaphoreSlim _download = new(1, 1);
    private IReadOnlyDictionary<string, ECParameters> _keys = new Dictionary<string, ECParameters>();
    private DateTimeOffset? _lastSuccess;
    private DateTimeOffset? _lastAttempt;

    /// <summary>The usable keys, by kid (replaced as a whole on each successful download).</summary>
    public IReadOnlyDictionary<string, ECParameters> Keys => Volatile.Read(ref _keys);

    /// <summary>Number of download attempts so far (successful or not).</summary>
    public int Downloads { get; private set; }

    /// <summary>Downloads the keys now (start-up). False when the core cannot be reached or sends no usable key.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _download.WaitAsync(cancellationToken);
        try
        {
            return await DownloadAsync(cancellationToken);
        }
        finally
        {
            _download.Release();
        }
    }

    /// <summary>Downloads the keys again if they are old, missing, or lack this kid — within the download limit.</summary>
    public async Task EnsureFreshAsync(string keyId, CancellationToken cancellationToken = default)
    {
        if (!NeedsDownload(keyId))
        {
            return;
        }

        await _download.WaitAsync(cancellationToken);
        try
        {
            // Checked again: another verification may have downloaded meanwhile.
            if (NeedsDownload(keyId))
            {
                await DownloadAsync(cancellationToken);
            }
        }
        finally
        {
            _download.Release();
        }
    }

    private bool NeedsDownload(string keyId)
    {
        DateTimeOffset now = time.GetUtcNow();
        bool stale = _lastSuccess is null || now - _lastSuccess >= RefreshInterval;
        bool allowed = _lastAttempt is null || now - _lastAttempt >= MinDownloadInterval;
        return allowed && (stale || !Keys.ContainsKey(keyId));
    }

    private async Task<bool> DownloadAsync(CancellationToken cancellationToken)
    {
        _lastAttempt = time.GetUtcNow();
        Downloads++;
        try
        {
            using HttpResponseMessage response = await http.GetAsync(keysUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaxDocumentBytes)
            {
                return false;
            }

            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Dictionary<string, ECParameters>? keys = body.Length <= MaxDocumentBytes ? ParseJwkSet(body) : null;
            if (keys is null || keys.Count == 0)
            {
                return false;
            }

            Volatile.Write(ref _keys, keys);
            _lastSuccess = time.GetUtcNow();
            return true;
        }
        catch (HttpRequestException)
        {
            return false; // core unreachable: keep the keys we have
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false; // HTTP timeout
        }
    }

    /// <summary>
    /// Reads a JWK Set (RFC 7517) and keeps only ES256 public keys: kty "EC", crv "P-256", alg "ES256"
    /// and use "sig" when present, a kid, x and y of 32 bytes on the curve, and no private part "d".
    /// Anything else is ignored. Null when the document itself is unreadable.
    /// </summary>
    public static Dictionary<string, ECParameters>? ParseJwkSet(byte[] json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("keys", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            Dictionary<string, ECParameters> keys = new(StringComparer.Ordinal);
            foreach (JsonElement jwk in list.EnumerateArray())
            {
                if (TryReadKey(jwk, out string? keyId, out ECParameters key))
                {
                    keys.TryAdd(keyId!, key); // a repeated kid keeps its first key
                }
            }

            return keys;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadKey(JsonElement jwk, out string? keyId, out ECParameters key)
    {
        keyId = null;
        key = default;
        if (jwk.ValueKind != JsonValueKind.Object || jwk.TryGetProperty("d", out _)
            || Text(jwk, "kty") != "EC" || Text(jwk, "crv") != "P-256"
            || (jwk.TryGetProperty("alg", out _) && Text(jwk, "alg") != "ES256")
            || (jwk.TryGetProperty("use", out _) && Text(jwk, "use") != "sig"))
        {
            return false;
        }

        keyId = Text(jwk, "kid");
        if (string.IsNullOrEmpty(keyId)
            || Text(jwk, "x") is not { } x || !EntryTicketRules.TryDecode(x, out byte[] xBytes) || xBytes.Length != 32
            || Text(jwk, "y") is not { } y || !EntryTicketRules.TryDecode(y, out byte[] yBytes) || yBytes.Length != 32)
        {
            return false;
        }

        key = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = xBytes, Y = yBytes } };
        try
        {
            // Refuses a point that is not on the curve (Windows reports it as PlatformNotSupportedException).
            using ECDsa check = ECDsa.Create(key);
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public void Dispose() => _download.Dispose();

    private static string? Text(JsonElement jwk, string name) =>
        jwk.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
