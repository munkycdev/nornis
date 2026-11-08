using Nornis.Application.Ai;
using Nornis.Application.Art;

namespace Nornis.Application.Tests.Fakes;

/// <summary>Makes tiny PNGs on demand, or throws; records every prompt.</summary>
public sealed class FakeImageArtGenerator : IImageArtGenerator
{
    public List<(AiCallScope Scope, string Prompt, int Count)> Calls { get; } = [];

    /// <summary>The shape and operation of every call, in order.</summary>
    public List<(ImageShape Shape, string Operation)> Shapes { get; } = [];

    public bool HasKey { get; set; } = true;

    public Exception? Throw { get; set; }

    /// <summary>How many images it returns, when not the count asked for.</summary>
    public int? Deliver { get; set; }

    public Task<ImageArtGeneration> GenerateAsync(
        AiCallScope scope, string prompt, ImageShape shape, int count, int timeoutSeconds, string operation, CancellationToken ct)
    {
        Calls.Add((scope, prompt, count));
        Shapes.Add((shape, operation));
        if (Throw is not null)
        {
            throw Throw;
        }

        var images = Enumerable.Range(0, Deliver ?? count).Select(i => new GeneratedImage(TestImages.Png(1024, 1536, (byte)i), null)).ToList();
        var usage = new AiUsage { Model = "image-model", InputTokens = 50, OutputTokens = 1_000 * images.Count, TotalTokens = 50 + 1_000 * images.Count, DurationMs = 0 };
        return Task.FromResult(new ImageArtGeneration(images, usage));
    }

    public Task<bool> CanGenerateAsync(AiCallScope scope, CancellationToken ct) => Task.FromResult(HasKey);
}

/// <summary>Image headers that <see cref="ImageFile"/> reads; the pixels are never decoded.</summary>
public static class TestImages
{
    public static byte[] Png(int width, int height, byte salt = 0)
    {
        var bytes = new byte[40];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        bytes[^1] = salt;
        return bytes;
    }

    /// <summary>SOI, an APP0 segment, then a baseline start of frame.</summary>
    public static byte[] Jpeg(int width, int height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];
}
