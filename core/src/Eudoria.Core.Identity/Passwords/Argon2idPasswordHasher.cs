using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Eudoria.Core.Identity.Passwords;

/// <summary>
/// Argon2id cost parameters. Defaults: 64 MiB, 3 passes, 1 lane — RFC 9106 second recommended option
/// (memory-constrained), with p = 1 imposed by libsodium. Owner decision of 2026-11-24 (plan 1.2, section 12).
/// </summary>
internal sealed record Argon2idOptions(int MemoryKib = 64 * 1024, int Passes = 3)
{
    public static readonly Argon2idOptions Default = new();
}

/// <summary>
/// Argon2id through NSec (libsodium). Output: <c>$argon2id$v=19$m=…,t=…,p=1$salt$hash</c>
/// (unpadded Base64), so the cost can be raised later without breaking existing hashes.
/// libsodium only supports one lane (p = 1) and 16-byte salts.
/// </summary>
internal sealed class Argon2idPasswordHasher(Argon2idOptions options) : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Prefix = "$argon2id$v=19$";

    private readonly Argon2id _algorithm = CreateAlgorithm(options.MemoryKib, options.Passes);

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = _algorithm.DeriveBytes(Encoding.UTF8.GetBytes(password), salt, HashBytes);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}m={options.MemoryKib},t={options.Passes},p=1${ToBase64(salt)}${ToBase64(hash)}");
    }

    public bool Verify(string password, string hash)
    {
        if (!TryParse(hash, out int memoryKib, out int passes, out byte[] salt, out byte[] expected))
        {
            return false;
        }

        Argon2id algorithm = memoryKib == options.MemoryKib && passes == options.Passes
            ? _algorithm
            : CreateAlgorithm(memoryKib, passes);
        byte[] actual = algorithm.DeriveBytes(Encoding.UTF8.GetBytes(password), salt, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static Argon2id CreateAlgorithm(int memoryKib, int passes) =>
        PasswordBasedKeyDerivationAlgorithm.Argon2id(new Argon2Parameters
        {
            DegreeOfParallelism = 1,
            MemorySize = memoryKib,
            NumberOfPasses = passes,
        });

    // $argon2id$v=19$m=65536,t=3,p=1$<salt>$<hash>
    private static bool TryParse(string phc, out int memoryKib, out int passes, out byte[] salt, out byte[] hash)
    {
        memoryKib = passes = 0;
        salt = hash = [];
        if (!phc.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = phc[Prefix.Length..].Split('$');
        if (parts.Length != 3)
        {
            return false;
        }

        string[] parameters = parts[0].Split(',');
        if (parameters.Length != 3 || parameters[2] != "p=1"
            || !TryParseParameter(parameters[0], "m=", out memoryKib)
            || !TryParseParameter(parameters[1], "t=", out passes))
        {
            return false;
        }

        return TryFromBase64(parts[1], out salt) && salt.Length == SaltBytes
            && TryFromBase64(parts[2], out hash) && hash.Length >= 16;
    }

    private static bool TryParseParameter(string text, string name, out int value)
    {
        value = 0;
        return text.StartsWith(name, StringComparison.Ordinal)
            && int.TryParse(text.AsSpan(name.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value > 0;
    }

    private static string ToBase64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryFromBase64(string text, out byte[] bytes)
    {
        string padded = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');
        byte[] buffer = new byte[padded.Length];
        if (Convert.TryFromBase64String(padded, buffer, out int written))
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = [];
        return false;
    }
}
