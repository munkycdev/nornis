namespace Nornis.Application.Storage;

public static class BlobStorageExtensions
{
    /// <summary>
    /// A blob's bytes, or null when it's gone. For prints (feature 38): a missing cover or handout
    /// prints without it rather than failing the booklet.
    /// </summary>
    public static async Task<byte[]?> ReadAllBytesOrNullAsync(this IBlobStorageService blobs, string blobPath, CancellationToken ct)
    {
        try
        {
            await using var stream = await blobs.OpenReadAsync(blobPath, ct);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct);
            return copy.ToArray();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
