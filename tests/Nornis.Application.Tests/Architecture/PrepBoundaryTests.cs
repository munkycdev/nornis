using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Application.Tests.Architecture;

/// <summary>
/// Two of Prep's boundaries are promises about code that doesn't exist yet: nobody will read a
/// side conversation from anywhere but Prep, and nobody will file a print except through the
/// Library's upload path. Neither can be shown by running today's code, because the breach would
/// be a new caller. So these scan the source, as <c>UnsupportedChatParameterTests</c> does, and
/// a new reader or a new filing path fails here until someone decides it is allowed.
/// </summary>
[TestFixture]
public class PrepBoundaryTests
{
    /// <summary>
    /// Feature 30, Requirement 6: a Conversation is never extracted, never read by Ask's retriever
    /// and never public. These are the only files that may name its repository or its tables.
    /// </summary>
    private static readonly HashSet<string> ConversationReaders = new(StringComparer.Ordinal)
    {
        "src/Nornis.Domain/Repositories/IConversationRepository.cs",
        "src/Nornis.Domain/Models/WorldExportData.cs",
        "src/Nornis.Infrastructure/Persistence/NornisDbContext.cs",
        "src/Nornis.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs",
        "src/Nornis.Infrastructure/Persistence/Configurations/ConversationMessageConfiguration.cs",
        "src/Nornis.Infrastructure/Persistence/Repositories/ConversationRepository.cs",
        "src/Nornis.Infrastructure/Persistence/Repositories/AdventureRepository.cs",
        "src/Nornis.Infrastructure/Persistence/Repositories/WorldRepository.cs",
        "src/Nornis.Infrastructure/Persistence/Repositories/WorldExportReader.cs",
        "src/Nornis.Application/Services/AdventureConversationService.cs",
        "src/Nornis.Application/Services/WorldExportService.cs",
        "src/Nornis.Api/Program.cs",
    };

    private static readonly Regex ConversationAccess =
        new(@"\b(IConversationRepository|ConversationRepository|Conversations|ConversationMessages)\b", RegexOptions.Compiled);

    [Test]
    public void OnlyPrep_ReadsOrWritesASideConversation()
    {
        var offenders = SourceFiles()
            .Where(f => !ConversationReaders.Contains(f.Relative))
            .SelectMany(f => CodeLines(f).Where(l => ConversationAccess.IsMatch(l.Text)).Select(l => $"{f.Relative}:{l.Number}"))
            .ToList();

        Assert.That(offenders, Is.Empty,
            "A new reader of the side conversation. If it is Prep's, add the file here; if it is Ask, extraction or a public route, it breaks Requirement 6.");
    }

    [Test]
    public void TheConversationReaders_AllStillExist()
    {
        var files = SourceFiles().Select(f => f.Relative).ToHashSet();

        Assert.That(ConversationReaders.Where(r => !files.Contains(r)), Is.Empty, "a stale entry would hide a moved file");
    }

    /// <summary>
    /// Feature 30, Requirement 7: the Library/Bibliotaph integration hooks in at
    /// <c>LibraryService</c>'s request-upload and confirm, so a print must be filed there and
    /// nowhere else. A LibraryDocument gets an AdventureId in one place: the document
    /// <c>RequestUploadAsync</c> creates.
    /// </summary>
    [Test]
    public void APrintsAdventure_IsSetOnlyByLibraryServiceRequestUpload()
    {
        var setters = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file.Full);
            foreach (Match match in Regex.Matches(text, @"new\s+LibraryDocument\s*\{"))
            {
                var body = Block(text, match.Index + match.Length - 1);
                if (Regex.IsMatch(body, @"\bAdventureId\s*="))
                {
                    setters.Add($"{file.Relative}:{EnclosingMethod(text, match.Index)}");
                }
            }

            // A direct write to a document's AdventureId, as opposed to the bulk detach on delete,
            // which clears it through SetWhereAsync and never sets one.
            foreach (var line in CodeLines(file))
            {
                if (Regex.IsMatch(line.Text, @"\b(document|doc|print|row|d)\.AdventureId\s*=[^=>]"))
                {
                    setters.Add($"{file.Relative}:{line.Number}");
                }
            }
        }

        Assert.That(setters, Is.EqualTo(["src/Nornis.Application/Services/LibraryService.cs:RequestUploadAsync"]));
    }

    // ------------------------------------------------------------------ helpers --

    private sealed record SourceFile(string Full, string Relative);

    private static IEnumerable<SourceFile> SourceFiles()
    {
        var root = RepoRoot();
        var src = Path.Combine(root, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(f => new SourceFile(f, Path.GetRelativePath(root, f).Replace('\\', '/')))
            .Where(f => !f.Relative.Contains("/bin/", StringComparison.Ordinal)
                && !f.Relative.Contains("/obj/", StringComparison.Ordinal)
                && !f.Relative.Contains("/Migrations/", StringComparison.Ordinal));
    }

    private static IEnumerable<(int Number, string Text)> CodeLines(SourceFile file) =>
        File.ReadAllLines(file.Full)
            .Select((text, i) => (Number: i + 1, Text: text))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal));

    /// <summary>The text of the brace block that opens at <paramref name="open"/>.</summary>
    private static string Block(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            depth += text[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return text[open..(i + 1)];
            }
        }

        return text[open..];
    }

    /// <summary>The name of the last method declared before <paramref name="index"/>.</summary>
    private static string EnclosingMethod(string text, int index)
    {
        var methods = Regex.Matches(text[..index], @"\b(?:public|private|internal|protected)\b[^;{=]*?\s(\w+)\s*\(");
        return methods.Count == 0 ? "?" : methods[^1].Groups[1].Value;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return dir!.FullName;
    }
}
