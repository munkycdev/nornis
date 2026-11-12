using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.KeyArt;

/// <summary>
/// The Key art card's state (feature 42): the art, the working brief, the latest generation's
/// options, this month's images against the cap, and whether Generate can run and, if not, why.
/// </summary>
public sealed record KeyArtState(
    WorldKeyArt? KeyArt,
    string? Brief,
    IReadOnlyList<Guid> Options,
    Guid? ChosenOptionId,
    int ImagesThisMonth,
    int ImagesCap,
    bool CanGenerate,
    string? GenerateBlocker,
    string? Warning = null);

/// <summary>An image to send to the browser.</summary>
public sealed record KeyArtImage(byte[] Bytes, string ContentType);

public record SaveKeyArtBriefCommand(Guid WorldId, Guid ActingUserId, WorldRole ActingUserRole, string? Brief);

public record GenerateKeyArtCommand(Guid WorldId, Guid ActingUserId, WorldRole ActingUserRole, string Brief);

public record ChooseKeyArtCommand(Guid WorldId, Guid ActingUserId, WorldRole ActingUserRole, Guid OptionId);

/// <summary>Refine one option on offer (feature 45): the result is a new option beside it.</summary>
public record RefineKeyArtCommand(Guid WorldId, Guid ActingUserId, WorldRole ActingUserRole, Guid OptionId);

public record UploadKeyArtCommand(Guid WorldId, Guid ActingUserId, WorldRole ActingUserRole, byte[] Bytes);

public record SetKeyArtAnchorCommand(Guid WorldId, Guid ActingUserId, WorldRole ActingUserRole, KeyArtAnchor Anchor);

public record RemoveKeyArtCommand(Guid WorldId, Guid ActingUserId, WorldRole ActingUserRole);
