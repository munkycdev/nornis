namespace Nornis.Api.Contracts.Requests;

/// <summary>The Adventure an upload belongs to, or null for the Shelf (feature 36).</summary>
public record SetLibraryAdventureLinkRequest(Guid? AdventureId);
