using System.Reflection;
using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>
/// The rights page's fixed wording (feature 38). <see cref="For"/> is the one place the AI
/// sentence is chosen, so the booklet says exactly what the Adventure recorded.
/// </summary>
public static class RightsStatement
{
    /// <summary>The non-affiliation line every booklet carries (Requirement 2.4).</summary>
    public const string Notice =
        "Game system names are trademarks of their owners. This adventure is not affiliated with or endorsed by them.";

    /// <summary>The sentence for what was recorded or declared (Requirement 2.3).</summary>
    public static string For(AiUseKind aiUse) => aiUse switch
    {
        AiUseKind.TextAndArt => "Text written and cover art generated with AI assistance.",
        AiUseKind.Text => "Text written with AI assistance.",
        AiUseKind.Art => "Cover art generated with AI.",
        AiUseKind.Declared => "AI was used in making this adventure.",
        AiUseKind.None => "No AI was used to write this adventure's text or art.",
        _ => throw new ArgumentOutOfRangeException(nameof(aiUse), aiUse, "An AI use with no sentence."),
    };

    /// <summary>"3.9": the deployed release, read from the build's version, without the build number.</summary>
    public static string CurrentVersion { get; } = VersionOf(typeof(RightsStatement).Assembly);

    internal static string VersionOf(Assembly assembly) =>
        assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "0.0";
}
