namespace Nornis.Api.Contracts.Requests;

/// <summary>The data types to include in the export, as <c>WorldExportCategory</c> names.</summary>
public record ExportWorldRequest(IReadOnlyList<string> Categories);
