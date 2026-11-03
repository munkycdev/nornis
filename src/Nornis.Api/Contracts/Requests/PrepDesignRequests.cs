namespace Nornis.Api.Contracts.Requests;

/// <summary>The GM's edit of the design draft (feature 37); null <c>BasedOnUpdatedAt</c> starts one.</summary>
public record SaveAdventureDraftRequest(string Markdown, DateTimeOffset? BasedOnUpdatedAt);

/// <summary>The table brief (feature 37); null or blank clears it.</summary>
public record SetTableBriefRequest(string? Brief);

/// <summary>The Library books the Adventure reads (feature 37); empty means every book.</summary>
public record SetSourceBooksRequest(IReadOnlyList<Guid>? DocumentIds);
