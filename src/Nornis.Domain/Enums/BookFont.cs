namespace Nornis.Domain.Enums;

/// <summary>
/// The faces a booklet's text can be set in: open-licence serifs that ship with the printer, so
/// the PDF never depends on fonts on anyone's machine. Adding one takes its four WOFFs (400 and
/// 600, roman and italic) in <c>Nornis.Infrastructure/Printing/Fonts</c>, its family name in the
/// renderer and its entry in the Web's Typography card, which a test pins to this list.
/// </summary>
public enum BookFont
{
    /// <summary>The face every booklet was set in before the GM could choose.</summary>
    Newsreader,

    EbGaramond,

    Literata,

    CrimsonPro,

    SourceSerif,

    Lora,

    Alegreya,
}
