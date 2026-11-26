namespace Eudoria.Core.Identity.Tokens;

/// <summary>
/// Explicit JWT types (header "typ", RFC 8725 section 3.11): each kind of token the core signs has its
/// own type, checked exactly, so one can never be accepted in place of the other (ADR-0007).
/// </summary>
internal static class TokenTypes
{
    /// <summary>Access token: accepted by the core API only. The generic "JWT" type is refused.</summary>
    public const string Access = "eudoria-access+jwt";

    /// <summary>World entry ticket: accepted by one world server only, never by the core.</summary>
    public const string EntryTicket = "eudoria-entry+jwt";
}
