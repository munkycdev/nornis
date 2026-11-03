namespace Nornis.Application.Ai;

/// <summary>
/// Encrypts users' AI keys at rest (feature 39) with a Data Protection ring the Web can't read.
/// </summary>
public interface IAiKeyProtector
{
    string Protect(string key);

    /// <summary>The key, or null when it can't be read (a lost ring or tampered ciphertext), which
    /// callers treat as a rejected key.</summary>
    string? Unprotect(string protectedKey);
}
