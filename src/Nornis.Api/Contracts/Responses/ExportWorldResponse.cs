namespace Nornis.Api.Contracts.Responses;

/// <summary>A finished world export: a short-lived SAS download URL for the zip.</summary>
public record ExportWorldResponse(string DownloadUrl, string FileName, long SizeBytes);
