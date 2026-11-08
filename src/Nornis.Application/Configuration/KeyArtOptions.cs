namespace Nornis.Application.Configuration;

/// <summary>
/// Key art's bounds (feature 42). <c>KeyArtOptionsTests</c> pins the defaults; the API refuses to
/// start on a bound of zero or less, since none of them has an "unbounded" value.
/// </summary>
public class KeyArtOptions
{
    public const string SectionName = "KeyArt";

    /// <summary>Images a world may generate in a calendar month (UTC), counted before each call.</summary>
    public int MaxImagesPerMonth { get; set; } = 8;

    /// <summary>Options one Generate makes for the GM to choose between.</summary>
    public int OptionsPerGeneration { get; set; } = 2;

    /// <summary>How long one generation may take, in seconds, as for a cover.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>The largest upload, in bytes.</summary>
    public int MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Art narrower than this, in pixels, is taken with a warning that it may look soft across the hero.</summary>
    public int SharpWidth { get; set; } = 1_600;

    public string? FirstNonPositiveBound() =>
        new (string Name, int Value)[]
        {
            (nameof(MaxImagesPerMonth), MaxImagesPerMonth),
            (nameof(OptionsPerGeneration), OptionsPerGeneration),
            (nameof(TimeoutSeconds), TimeoutSeconds),
            (nameof(MaxUploadBytes), MaxUploadBytes),
            (nameof(SharpWidth), SharpWidth),
        }.FirstOrDefault(b => b.Value <= 0).Name;
}
