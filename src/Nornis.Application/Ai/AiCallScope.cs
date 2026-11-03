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

    /// <summary>False for an anonymous public visitor: the creator's trial is theirs to try Nornis
    /// with, not for the public to spend (Requirement 4.5).</summary>
    public bool TrialAllowed { get; }

    private AiCallScope(Guid? worldId, Guid? userId, bool trialAllowed)
    {
        WorldId = worldId;
        UserId = userId;
        TrialAllowed = trialAllowed;
    }

    public static AiCallScope ForWorld(Guid worldId, Guid? actingUserId = null) => new(worldId, actingUserId, true);

    /// <summary>A question from the world's public page, asked by nobody signed in.</summary>
    public static AiCallScope ForPublicVisitor(Guid worldId) => new(worldId, null, false);

    public static AiCallScope ForUser(Guid userId) => new(null, userId, true);
}
