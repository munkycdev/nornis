using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Prep.Covers;

/// <summary>
/// The Cover card's state (feature 38): the art, the brief, the generated options newest first,
/// how many of the cap are spent, and whether Generate can run and, if not, why.
/// </summary>
public sealed record CoverState(
    AdventureCover? Cover,
    string? Brief,
    IReadOnlyList<Guid> Options,
    Guid? ChosenOptionId,
    int ImagesGenerated,
    int ImagesCap,
    bool CanGenerate,
    string? GenerateBlocker,
    string? Warning = null);

/// <summary>An image to send to the browser.</summary>
public sealed record CoverImage(byte[] Bytes, string ContentType);

public record WriteCoverBriefCommand(Guid WorldId, Guid AdventureId, Guid ActingUserId, WorldRole ActingUserRole);

public record GenerateCoverCommand(Guid WorldId, Guid AdventureId, Guid ActingUserId, WorldRole ActingUserRole, string Brief);

public record SaveCoverBriefCommand(Guid WorldId, Guid AdventureId, Guid ActingUserId, WorldRole ActingUserRole, string? Brief);

public record ChooseCoverCommand(Guid WorldId, Guid AdventureId, Guid ActingUserId, WorldRole ActingUserRole, Guid OptionId);

public record UploadCoverCommand(Guid WorldId, Guid AdventureId, Guid ActingUserId, WorldRole ActingUserRole, byte[] Bytes);

public record RemoveCoverCommand(Guid WorldId, Guid AdventureId, Guid ActingUserId, WorldRole ActingUserRole);

public record SetCoverLayoutCommand(Guid WorldId, Guid AdventureId, Guid ActingUserId, WorldRole ActingUserRole, CoverLayout Layout);
