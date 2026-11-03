namespace Nornis.Api.Contracts.Requests;

/// <summary>What an upload is: Sourcebook, Map, Handout or Other (feature 36).</summary>
public record SetLibraryKindRequest(string Kind);
