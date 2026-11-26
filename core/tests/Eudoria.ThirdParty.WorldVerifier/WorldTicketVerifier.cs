namespace Eudoria.ThirdParty.WorldVerifier;

/// <summary>
/// What a world server does when a player arrives with an entry ticket (ADR-0007): checks it alone,
/// without calling the core except to download its public keys, and accepts each ticket once.
/// Returns the account's identifier, or why the ticket is refused. Never logs the ticket.
/// </summary>
/// <param name="worldId">This world's identifier: the ticket's audience must be "eudoria-world:&lt;worldId&gt;".</param>
public sealed class WorldTicketVerifier(string worldId, EntryTicketKeyStore keys, ReplayCache replays, TimeProvider time)
{
    public async Task<TicketVerdict> VerifyAsync(string ticket, CancellationToken cancellationToken = default)
    {
        TicketCheck header = EntryTicketRules.ReadHeader(ticket);
        if (header.Rejection is not null)
        {
            return TicketVerdict.Refused(header.Rejection);
        }

        await keys.EnsureFreshAsync(header.KeyId!, cancellationToken);
        if (keys.Keys.Count == 0)
        {
            return TicketVerdict.Refused(TicketRejections.KeysUnavailable);
        }

        DateTimeOffset now = time.GetUtcNow();
        TicketCheck check = EntryTicketRules.Check(ticket, worldId, now, keys.Keys);
        if (check.Ticket is not { } accepted)
        {
            return TicketVerdict.Refused(check.Rejection!);
        }

        // Last: only a ticket that passed every other rule uses up its jti.
        return replays.TryRemember(accepted.Jti, accepted.ExpiresAtUnixSeconds + EntryTicketRules.ClockToleranceSeconds, now.ToUnixTimeSeconds()) switch
        {
            ReplayOutcome.FirstUse => TicketVerdict.Accepted(accepted.AccountId),
            ReplayOutcome.Replayed => TicketVerdict.Refused(TicketRejections.Replayed),
            _ => TicketVerdict.Refused(TicketRejections.ReplayCacheFull),
        };
    }
}
