using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// The type an Adventure's booklet is set in: the face, size and line spacing of its body text.
/// Read-aloud boxes and props follow it; headings, stat blocks and tables keep their own faces
/// and scale with the size. Every field is optional, and a null is today's default, so an
/// Adventure the GM never touched prints as it always has. A print already filed keeps the
/// type it was printed in.
/// </summary>
public sealed record BookTypography
{
    public const BookFont DefaultFont = BookFont.Newsreader;

    public const decimal DefaultSize = 10m;

    public const decimal MinSize = 8m;

    public const decimal MaxSize = 14m;

    /// <summary>Sizes go in half points.</summary>
    public const decimal SizeStep = 0.5m;

    public const decimal DefaultLineSpacing = 1.3m;

    public const decimal MinLineSpacing = 1.0m;

    public const decimal MaxLineSpacing = 2.0m;

    /// <summary>Line spacing goes in twentieths: 1.3, 1.35, 1.4.</summary>
    public const decimal LineSpacingStep = 0.05m;

    /// <summary>The body face. Null is <see cref="DefaultFont"/>.</summary>
    public BookFont? Font { get; init; }

    /// <summary>The body size in points. Null is <see cref="DefaultSize"/>.</summary>
    public decimal? Size { get; init; }

    /// <summary>The body's line height as a multiple of its size. Null is <see cref="DefaultLineSpacing"/>.</summary>
    public decimal? LineSpacing { get; init; }

    public BookFont FontOrDefault => Font ?? DefaultFont;

    public decimal SizeOrDefault => Size ?? DefaultSize;

    public decimal LineSpacingOrDefault => LineSpacing ?? DefaultLineSpacing;

    /// <summary>
    /// The face's name, as a GM reads it. It is also the family name its WOFFs register under,
    /// which is how the renderer finds them.
    /// </summary>
    public static string NameOf(BookFont font) => font switch
    {
        BookFont.Newsreader => "Newsreader",
        BookFont.EbGaramond => "EB Garamond",
        BookFont.Literata => "Literata",
        BookFont.CrimsonPro => "Crimson Pro",
        BookFont.SourceSerif => "Source Serif 4",
        BookFont.Lora => "Lora",
        BookFont.Alegreya => "Alegreya",
        _ => throw new ArgumentOutOfRangeException(nameof(font), font, "Not one of the booklet's fonts."),
    };

    /// <summary>The name of the first field outside its bound, or null when every field is within.</summary>
    public string? Validate()
    {
        if (Font is { } font && !Enum.IsDefined(font))
        {
            return nameof(Font);
        }

        if (Size is { } size && (size < MinSize || size > MaxSize || size % SizeStep != 0))
        {
            return nameof(Size);
        }

        if (LineSpacing is { } spacing && (spacing < MinLineSpacing || spacing > MaxLineSpacing || spacing % LineSpacingStep != 0))
        {
            return nameof(LineSpacing);
        }

        return null;
    }
}
