namespace Nornis.Application.KeyArt;

/// <summary>
/// Where a world's key art lives (feature 42): one folder per world, inside the world's own
/// <c>worlds/{worldId}/</c>, so deleting the world deletes its art with the rest.
/// </summary>
public static class KeyArtBlobs
{
    public static string Folder(Guid worldId) => $"worlds/{worldId}/key-art/";

    /// <summary>A generated option: always a PNG.</summary>
    public static string OptionPath(Guid worldId, Guid optionId) => $"{Folder(worldId)}options/{optionId}.png";

    /// <summary>An upload. A new name each time, so a cached copy of the old art is never served for the new.</summary>
    public static string UploadPath(Guid worldId, Guid uploadId, string extension) => $"{Folder(worldId)}uploads/{uploadId}{extension}";
}
