using System.Text.RegularExpressions;

namespace Eudoria.Core.Identity.EntryTickets;

/// <summary>
/// World identifiers: opaque text, 1 to 64 of a-z, 0-9 and '-', not starting or ending with '-'
/// (plan 1.6, section 7). Fits a readable name today ("sandbox") and a lower-case UUID tomorrow.
/// </summary>
internal static partial class WorldIds
{
    public static bool IsValid(string? worldId) => worldId is not null && Pattern().IsMatch(worldId);

    // \z, not $: "$" would also accept a final line feed.
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
