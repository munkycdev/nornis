namespace Nornis.Api.Contracts.Requests;

/// <summary>The GM's edit of the design draft (feature 37); null <c>BasedOnUpdatedAt</c> starts one.</summary>
public record SaveAdventureDraftRequest(string Markdown, DateTimeOffset? BasedOnUpdatedAt);

/// <summary>The table brief (feature 37); null or blank clears it.</summary>
/// <summary>Applies the <paramref name="Index"/>th suggested edit of a saved answer, based on the draft's stamp.</summary>
public record ApplyDraftEditRequest(Guid MessageId, int Index, DateTimeOffset? BasedOnUpdatedAt);

/// <summary>Puts a whole saved answer into the draft: Replace or Append.</summary>
public record PutInDraftRequest(Guid MessageId, string? Mode, DateTimeOffset? BasedOnUpdatedAt);

/// <summary>Asks for a split plan: from the draft's headings, or (<paramref name="UseLoremaster"/>) the Loremaster's.</summary>
public record PlanDraftRequest(bool UseLoremaster);

/// <summary>One start of a split: line (1-based), title and kind.</summary>
public record DraftSplitPointRequest(int StartLine, string? Title, string? Kind);

/// <summary>Cuts the draft at the plan's lines. Mode is Replace or Append.</summary>
public record SplitDraftRequest(IReadOnlyList<DraftSplitPointRequest>? Plan, string? Mode, DateTimeOffset? BasedOnUpdatedAt);

/// <summary>Gathers the sections into the draft, based on the draft's stamp.</summary>
public record GatherDraftRequest(DateTimeOffset? BasedOnUpdatedAt);

public record SetTableBriefRequest(string? Brief);

/// <summary>The Library books the Adventure reads (feature 37); empty means every book.</summary>
public record SetSourceBooksRequest(IReadOnlyList<Guid>? DocumentIds);

/// <summary>The booklet's handouts in print order (feature 38).</summary>
public record SetHandoutsRequest(IReadOnlyList<Guid>? DocumentIds);
