namespace Nornis.Domain.Enums;

/// <summary>How a booklet's cover arranges its art and its type (feature 38). Nornis sets every word itself.</summary>
public enum CoverLayout
{
    /// <summary>The art fills the page under a solid title band.</summary>
    FullBleed,

    /// <summary>The title above, the art in a frame below it.</summary>
    Framed,

    /// <summary>The title at the top, the art in the middle, the details below.</summary>
    ClassicModule,
}
