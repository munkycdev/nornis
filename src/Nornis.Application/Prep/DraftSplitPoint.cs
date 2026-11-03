using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>
/// Where one section of a split starts in the design draft (feature 37), and what it is called.
/// Lines are 1-based, counted with <c>\n</c> line ends.
/// </summary>
public sealed record DraftSplitPoint(int StartLine, string Title, AdventureSectionKind Kind);
