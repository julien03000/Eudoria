namespace Eudoria.Core.Identity.Passwords;

/// <summary>Hashes and verifies passwords. Callers pass the NFKC-normalized password.</summary>
internal interface IPasswordHasher
{
    /// <summary>Returns a self-describing hash (PHC string format, parameters included).</summary>
    string Hash(string password);

    /// <summary>Checks a password against a hash produced by <see cref="Hash"/>, in constant time.</summary>
    bool Verify(string password, string hash);

    /// <summary>
    /// True when a valid hash was made with other cost parameters than the current ones: after a
    /// successful login, the password should then be hashed again (it is only known at that moment).
    /// </summary>
    bool NeedsRehash(string hash);
}
