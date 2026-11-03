using System.Buffers.Binary;

namespace Nornis.Application.Prep.Covers;

/// <summary>What an uploaded cover file is, read from its own bytes rather than its name or the browser's word.</summary>
public sealed record CoverImageInfo(string ContentType, string Extension, int Width, int Height);

/// <summary>
/// Reads an image's type and size from its header (feature 38): PNG's IHDR, JPEG's first start of
/// frame. Nothing is decoded, so a hostile file is never handed to an image library here.
/// </summary>
public static class CoverImageFile
{
    /// <summary>A cover this many pixels or more on its short side prints sharp on a page.</summary>
    public const int SharpShortSide = 1_000;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Null when the bytes are neither a PNG nor a JPEG with a readable size.</summary>
    public static CoverImageInfo? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(PngSignature) && bytes[12..16].SequenceEqual("IHDR"u8))
        {
            var width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);
            return width > 0 && height > 0 ? new CoverImageInfo("image/png", ".png", width, height) : null;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return Jpeg(bytes);
        }

        return null;
    }

    private static CoverImageInfo? Jpeg(ReadOnlySpan<byte> bytes)
    {
        var at = 2;
        while (at + 4 <= bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                return null;
            }

            var marker = bytes[at + 1];
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                at += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 2)..(at + 4)]);
            if (length < 2)
            {
                return null;
            }

            // A start of frame (any of SOF0..SOF15 but DHT, JPG and DAC) carries the size.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (at + 9 > bytes.Length)
                {
                    return null;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 5)..(at + 7)]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(at + 7)..(at + 9)]);
                return width > 0 && height > 0 ? new CoverImageInfo("image/jpeg", ".jpg", width, height) : null;
            }

            at += 2 + length;
        }

        return null;
    }
}
