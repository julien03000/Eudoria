using System.Collections.Frozen;
using System.Text;

namespace Eudoria.Core.Identity.Passwords;

/// <summary>
/// Refuses commonly used passwords (NIST SP 800-63B-4): a list of leaked passwords embedded in the
/// module (common-passwords.txt, built by tools/dev/build-password-blocklist.ps1), plus words tied to
/// the context: the pseudo, the part of the e-mail before '@', and the name of the service.
/// </summary>
internal sealed class CommonPasswords
{
    /// <summary>Context words shorter than this are ignored (too many false refusals: "ana" in "banana").</summary>
    public const int MinContextWordLength = 4;

    internal const string ResourceName = "Eudoria.Core.Identity.common-passwords.txt";

    private const string ServiceName = "eudoria";

    private readonly FrozenSet<string> _list;

    public CommonPasswords()
        : this(LoadEmbeddedList())
    {
    }

    internal CommonPasswords(IEnumerable<string> entries) =>
        _list = entries.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Number of entries in the list.</summary>
    public int Count => _list.Count;

    /// <summary>
    /// True when an NFKC-normalized password is in the list (letter case ignored), or contains the
    /// pseudo, the local part of the e-mail or the service name (each of 4 characters or more).
    /// </summary>
    public bool IsRefused(string normalizedPassword, string? pseudo, string? email)
    {
        string lower = normalizedPassword.ToLowerInvariant();
        if (_list.Contains(lower))
        {
            return true;
        }

        string? localPart = email is null ? null : email[..Math.Max(0, email.IndexOf('@', StringComparison.Ordinal))];
        return ContainsWord(lower, ServiceName) || ContainsWord(lower, pseudo) || ContainsWord(lower, localPart);
    }

    private static bool ContainsWord(string lowerPassword, string? word) =>
        word is { Length: >= MinContextWordLength }
        && lowerPassword.Contains(word.ToLowerInvariant(), StringComparison.Ordinal);

    private static List<string> LoadEmbeddedList()
    {
        using Stream stream = typeof(CommonPasswords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        List<string> entries = [];
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                entries.Add(line);
            }
        }

        return entries;
    }
}
