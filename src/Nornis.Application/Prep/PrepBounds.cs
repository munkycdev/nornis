using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Domain.Entities;

namespace Nornis.Application.Prep;

/// <summary>
/// The bounds of Requirement 2, checked on every write: a GM's save, an import, and the accept
/// of a draft. Each refusal names the size found and the bound.
/// </summary>
public static class PrepBounds
{
    public static AppError? Validate(IReadOnlyList<AdventureSection> sections, PrepOptions options)
    {
        if (sections.Count > options.MaxSections)
        {
            return TooLarge($"That's {sections.Count:N0} sections; an adventure holds at most {options.MaxSections:N0}.");
        }

        var oversized = sections.FirstOrDefault(s => s.BodyMarkdown.Length > options.MaxSectionChars);
        if (oversized is not null)
        {
            return TooLarge($"\"{oversized.Title}\" is {oversized.BodyMarkdown.Length:N0} characters; "
                + $"a section holds at most {options.MaxSectionChars:N0}.");
        }

        var size = AdventureSize.Measure(sections);
        if (size > options.MaxAdventureChars)
        {
            return TooLarge($"The adventure would be {size:N0} characters; an adventure holds at most "
                + $"{options.MaxAdventureChars:N0}, so that it can always be exported whole.");
        }

        return null;
    }

    public static AppError TooLarge(string message) => new(400, "prep_too_large", message);
}
