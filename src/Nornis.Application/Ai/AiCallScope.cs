namespace Nornis.Application.Ai;

/// <summary>
/// Whose AI call this is (feature 39). A call that belongs to a world spends the world creator's
/// keys, whoever caused it; a call with no world yet (naming a new demo world) spends the keys of
/// the user who caused it. Carried by every AI request so the binder can decide who pays at the
/// moment of the call, never earlier.
/// </summary>
public sealed record AiCallScope
{
    public Guid? WorldId { get; }

    /// <summary>Who caused the call, when anyone did. Null for an anonymous public visitor or a
    /// background job.</summary>
    public Guid? UserId { get; }

    private AiCallScope(Guid? worldId, Guid? userId)
    {
        WorldId = worldId;
        UserId = userId;
    }

    public static AiCallScope ForWorld(Guid worldId, Guid? actingUserId = null) => new(worldId, actingUserId);

    public static AiCallScope ForUser(Guid userId) => new(null, userId);
}
