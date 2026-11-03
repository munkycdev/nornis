namespace Nornis.Domain.Enums;

/// <summary>
/// What pays for an AI call (feature 39). <see cref="Nornis"/> is Nornis's own key spending for
/// itself (embeddings, and every call until user keys exist); <see cref="Trial"/> is Nornis's key
/// spending a user's trial allowance; <see cref="Key"/> is the key holder's own key.
/// </summary>
public enum AiFundedBy
{
    Nornis,
    Trial,
    Key
}
