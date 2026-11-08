namespace Nornis.Api.Extensions;

/// <summary>Reading an uploaded file sent as the raw request body, the way covers and key art take one.</summary>
public static class RequestBodies
{
    /// <summary>
    /// Reads the body up to one chunk past <paramref name="limit"/> bytes: enough for the service to
    /// see it is too large, without reading the rest.
    /// </summary>
    public static async Task<byte[]> ReadBoundedAsync(Stream body, int limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit)
            {
                break;
            }
        }

        return buffer.ToArray();
    }
}
