namespace Nornis.Application.Ai;

/// <summary>
/// Encrypts users' AI keys at rest (feature 39) with a Data Protection ring the Web can't read.
/// </summary>
public interface IAiKeyProtector
{
    /// <summary>The key's ciphertext.</summary>
    /// <exception cref="AiKeyStorageException">The ring couldn't be reached or written, so the key
    /// can't be stored safely; nothing was saved.</exception>
    string Protect(string key);

    /// <summary>The key, or null when it can't be read (a lost ring or tampered ciphertext), which
    /// callers treat as a rejected key.</summary>
    string? Unprotect(string protectedKey);
}

/// <summary>
/// The key ring couldn't be used to encrypt a key: its blob refused the write (a role not yet in
/// effect) or couldn't be reached. Says nothing about the key itself.
/// </summary>
public sealed class AiKeyStorageException(Exception innerException)
    : Exception("The AI key ring couldn't be used to encrypt a key.", innerException);
