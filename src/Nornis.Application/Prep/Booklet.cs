using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Prep;

/// <summary>
/// The booklet's structure (feature 34): what <see cref="BookletModel"/> builds from an Adventure
/// and what <see cref="IBookletPdfRenderer"/> lays out. It carries no fonts, sizes or pages, so
/// every markup convention is decided before layout and every layout rule after it. The one
/// exception is the GM's <see cref="Typography"/>, which names a face, size and spacing but
/// leaves every point size and column to the renderer; null is the default type.
/// </summary>
public sealed record Booklet(
    BookletCover Cover, BookletRights Rights, IReadOnlyList<BookletChapter> Chapters, IReadOnlyList<BookletHandout>? Handouts = null,
    BookTypography? Typography = null);

/// <summary>
/// One entry of the "Handouts and maps" appendix (feature 38), in the GM's order. The printer
/// reads the files; the model never touches storage.
/// </summary>
public abstract record BookletHandout(string Title);

/// <summary>An image, drawn on its own page, landscape when it is wider than tall.</summary>
public sealed record BookletHandoutImage(string Title, byte[] Image) : BookletHandout(Title);

/// <summary>A PDF, whose pages are appended as they are.</summary>
public sealed record BookletHandoutPdf(string Title, byte[] Pdf) : BookletHandout(Title);

/// <summary>A handout the booklet can't carry, printed as a page that names it and says why.</summary>
public sealed record BookletHandoutMissing(string Title, string Reason) : BookletHandout(Title);

/// <summary>The props to cut out and hand over (feature 38): every <c>[!PROP]</c> box, each on its own page.</summary>
public sealed record BookletProps(string Title, IReadOnlyList<BookletBox> Props, BookTypography? Typography = null);

/// <summary>
/// The cover's lines, each already worded; a null line is left off (feature 32). The credits
/// moved to the rights page in feature 38, which also gave the cover its art. No art keeps the
/// typeset cover.
/// </summary>
public sealed record BookletCover(
    string Title,
    string? SystemLine,
    string? LevelsLine,
    string AuthorLine,
    string? Pitch,
    BookletArt? Art = null);

/// <summary>The cover art's bytes (PNG or JPEG) and the layout that places it (feature 38).</summary>
public sealed record BookletArt(byte[] Image, CoverLayout Layout);

/// <summary>
/// The rights page (feature 38), straight after the cover: credits, then the notices. Each line is
/// already worded; a null line is left off.
/// </summary>
/// <param name="Credits">The publisher and the year: "Nornis Press · 2026". The type isn't printed.</param>
/// <param name="Copyright">"© 2026 kelda".</param>
/// <param name="Printed">"Printed 3 November 2026".</param>
/// <param name="MadeWith">"Made with Nornis 3.9".</param>
/// <param name="AiStatement">What the Adventure recorded about AI, from <see cref="RightsStatement.For"/>.</param>
/// <param name="WebResearchStatement"><see cref="RightsStatement.WebResearch"/> when the Adventure drew on the web (feature 40); null otherwise.</param>
public sealed record BookletRights(
    string Title,
    string Publisher,
    string Credits,
    string Copyright,
    string? SystemLine,
    string? LevelsLine,
    string Printed,
    string MadeWith,
    string AiStatement,
    string Notice,
    string? WebResearchStatement = null);

/// <summary>When a booklet was printed and by which release (feature 38): what the rights page dates it by.</summary>
public sealed record BookletImprint(DateOnly Printed, string Version);

/// <summary>A Chapter, which starts a page. <see cref="Title"/> is null for the sections before the first Chapter.</summary>
public sealed record BookletChapter(string? Title, IReadOnlyList<BookletBlock> Blocks);

public abstract record BookletBlock;

/// <summary>A non-Chapter section's title (a Scene, Notes and the others), set in the column.</summary>
public sealed record BookletSectionHeading(string Text, AdventureSectionKind Kind) : BookletBlock;

/// <summary>A heading written inside a section's body, level 1 to 6.</summary>
public sealed record BookletHeading(int Level, IReadOnlyList<BookletInline> Text) : BookletBlock;

public sealed record BookletParagraph(IReadOnlyList<BookletInline> Text) : BookletBlock;

public enum BookletListStyle
{
    Bullet,
    Numbered,
}

public sealed record BookletList(BookletListStyle Style, int Start, IReadOnlyList<BookletListItem> Items) : BookletBlock;

public enum BookletTask
{
    None,
    Open,
    Done,
}

public sealed record BookletListItem(BookletTask Task, IReadOnlyList<BookletBlock> Blocks);

public sealed record BookletTable(
    IReadOnlyList<IReadOnlyList<BookletInline>> Header,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<BookletInline>>> Rows) : BookletBlock;

public enum BookletBoxKind
{
    ReadAloud,
    Prop,
    Ask,
    Gm,
}

/// <summary>One of <see cref="PrepMarkup"/>'s blockquote conventions, marker removed.</summary>
public sealed record BookletBox(BookletBoxKind Kind, IReadOnlyList<BookletBlock> Blocks) : BookletBlock;

/// <summary>A stat block or an item block, as <see cref="GameBlocks"/> parsed it (feature 35); the renderer decides its look.</summary>
public sealed record BookletGameBlock(GameBlock Block) : BookletBlock;

public sealed record BookletCode(string Text) : BookletBlock;

public sealed record BookletRule : BookletBlock;

[Flags]
public enum BookletRunStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strike = 4,
    Code = 8,
}

public abstract record BookletInline;

public sealed record BookletRun(string Text, BookletRunStyle Style = BookletRunStyle.None) : BookletInline;

/// <summary>A line break inside a paragraph: every newline the GM typed (Requirement 3.1).</summary>
public sealed record BookletBreak : BookletInline;
