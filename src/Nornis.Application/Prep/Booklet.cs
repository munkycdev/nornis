using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>
/// The booklet's structure (feature 34): what <see cref="BookletModel"/> builds from an Adventure
/// and what <see cref="IBookletPdfRenderer"/> lays out. It carries no fonts, sizes or pages, so
/// every markup convention is decided before layout and every layout rule after it.
/// </summary>
public sealed record Booklet(BookletCover Cover, IReadOnlyList<BookletChapter> Chapters);

/// <summary>The cover's lines, each already worded; a null line is left off (feature 32).</summary>
public sealed record BookletCover(
    string Title,
    string? SystemLine,
    string? LevelsLine,
    string AuthorLine,
    string? Pitch,
    string CreditsLine);

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
}

/// <summary>One of <see cref="PrepMarkup"/>'s three blockquote conventions, marker removed.</summary>
public sealed record BookletBox(BookletBoxKind Kind, IReadOnlyList<BookletBlock> Blocks) : BookletBlock;

/// <summary>A <c>statblock</c> fence: label and value per line; a line with no label is <see cref="Label"/> null.</summary>
public sealed record BookletStatEntry(string? Label, string Value);

public sealed record BookletStatBlock(IReadOnlyList<BookletStatEntry> Entries) : BookletBlock;

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
