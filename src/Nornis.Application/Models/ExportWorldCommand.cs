using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// Request to package the selected slices of a world into a single downloadable zip.
/// </summary>
public record ExportWorldCommand(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    IReadOnlyCollection<WorldExportCategory> Categories);

/// <summary>
/// A finished export: the short-lived SAS URL the browser downloads from, and what it
/// will be getting.
/// </summary>
public record WorldExportResult(
    string DownloadUrl,
    string FileName,
    long SizeBytes);
