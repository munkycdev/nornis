namespace Nornis.Application.Prep.Covers;

/// <summary>
/// Where an Adventure's cover art lives (feature 38). Everything sits under one folder per
/// Adventure, so deleting the Adventure deletes its art with one prefix delete, and a wiped world
/// takes it with the rest of <c>worlds/{worldId}/</c>.
/// </summary>
public static class CoverBlobs
{
    public static string Folder(Guid worldId, Guid adventureId) => $"worlds/{worldId}/covers/{adventureId}/";

    /// <summary>A generated option: always a PNG.</summary>
    public static string OptionPath(Guid worldId, Guid adventureId, Guid optionId) =>
        $"{Folder(worldId, adventureId)}options/{optionId}.png";

    /// <summary>An uploaded cover. A new name each time, so a cached copy of the old one is never served for the new.</summary>
    public static string UploadPath(Guid worldId, Guid adventureId, Guid uploadId, string extension) =>
        $"{Folder(worldId, adventureId)}uploads/{uploadId}{extension}";

    public static bool IsUpload(string blobPath) => blobPath.Contains("/uploads/", StringComparison.Ordinal);
}
