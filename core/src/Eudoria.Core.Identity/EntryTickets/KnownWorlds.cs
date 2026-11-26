using Microsoft.Extensions.Options;

namespace Eudoria.Core.Identity.EntryTickets;

/// <summary>
/// "Does this world exist?" The only question the entry tickets ask about worlds. Answered from the
/// configuration today; the world registry (step 6) will replace this implementation, nothing else.
/// </summary>
internal interface IKnownWorlds
{
    bool Exists(string worldId);
}

/// <summary>The closed list <c>Identity:EntryTickets:KnownWorlds</c> of appsettings.json.</summary>
internal sealed class ConfiguredKnownWorlds(IOptions<EntryTicketOptions> options) : IKnownWorlds
{
    private readonly HashSet<string> _worlds = new(options.Value.KnownWorlds, StringComparer.Ordinal);

    public bool Exists(string worldId) => _worlds.Contains(worldId);
}
