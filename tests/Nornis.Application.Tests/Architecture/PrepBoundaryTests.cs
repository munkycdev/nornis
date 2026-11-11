using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Application.Tests.Architecture;

/// <summary>
/// Some of Prep's boundaries are promises about code that doesn't exist yet: nobody will read a
/// side conversation from anywhere but Prep, nobody will file a print except through the
/// Library's one filing path, and nobody will lay out a booklet but the one printer. Neither can be shown by running today's code, because the breach would
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
        "src/Nornis.Application/Services/AdventureDesignService.cs",
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
    /// <c>LibraryService</c>, so a print must be filed there and nowhere else. Since feature 34 a
    /// LibraryDocument gets an AdventureId in one place: the document <c>FilePrintAsync</c>
    /// creates (an upload that names an Adventure is refused).
    /// </summary>
    [Test]
    public void APrintsAdventure_IsSetOnlyByLibraryServiceFilePrint()
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

        Assert.That(setters, Is.EqualTo(["src/Nornis.Application/Services/LibraryService.cs:FilePrintAsync"]));
    }

    /// <summary>
    /// Feature 36: an upload is filed under an Adventure by <c>LibraryService</c> alone, where the
    /// GM-only rule, the same-world check and the refusal for prints and books live. The bulk
    /// release on an Adventure's delete clears it through SetWhereAsync and never sets one.
    /// </summary>
    [Test]
    public void AnUploadsAdventureLink_IsSetOnlyByLibraryService()
    {
        var setters = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file.Full);
            foreach (Match match in Regex.Matches(text, @"new\s+LibraryDocument\s*\{"))
            {
                if (Regex.IsMatch(Block(text, match.Index + match.Length - 1), @"\bLinkedAdventureId\s*=[^=>]"))
                {
                    setters.Add(file.Relative);
                }
            }

            setters.AddRange(CodeLines(file)
                .Where(line => Regex.IsMatch(line.Text, @"\w\.LinkedAdventureId\s*=[^=>]"))
                .Select(_ => file.Relative));
        }

        Assert.Multiple(() =>
        {
            Assert.That(setters, Is.Not.Empty, "the scan still finds LibraryService's own writes");
            Assert.That(setters.Distinct(), Is.EqualTo(["src/Nornis.Application/Services/LibraryService.cs"]));
        });
    }

    /// <summary>
    /// A print's details are a snapshot (feature 32, Requirement 4.2): set once when the print is
    /// filed and never changed. The guard is that there is one writer, the object initialiser in
    /// <c>FilePrintAsync</c>, so no later path can forget the rule.
    /// </summary>
    [Test]
    public void APrintsDetails_AreSetOnlyByLibraryServiceFilePrint()
    {
        var setters = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file.Full);
            foreach (Match match in Regex.Matches(text, @"new\s+LibraryDocument\s*\{"))
            {
                var body = Block(text, match.Index + match.Length - 1);
                if (Regex.IsMatch(body, @"\bPrintDetails\s*="))
                {
                    setters.Add($"{file.Relative}:{EnclosingMethod(text, match.Index)}");
                }
            }

            foreach (var line in CodeLines(file))
            {
                if (Regex.IsMatch(line.Text, @"\.PrintDetails\s*=[^=>]"))
                {
                    setters.Add($"{file.Relative}:{line.Number}");
                }
            }
        }

        Assert.That(setters, Is.EqualTo(["src/Nornis.Application/Services/LibraryService.cs:FilePrintAsync"]));
    }

    /// <summary>
    /// Feature 34: the PDF library is an Infrastructure detail behind <c>IBookletPdfRenderer</c>.
    /// If it leaks into Application, the Web or a second Infrastructure folder, swapping it (the
    /// design names MigraDoc as the fallback) stops being a one-folder change.
    /// </summary>
    [Test]
    public void QuestPdf_IsNamedOnlyInInfrastructurePrinting()
    {
        var offenders = SourceFiles()
            .Where(f => !f.Relative.StartsWith("src/Nornis.Infrastructure/Printing/", StringComparison.Ordinal))
            .SelectMany(f => CodeLines(f).Where(l => Regex.IsMatch(l.Text, @"\bQuestPDF\b")).Select(l => $"{f.Relative}:{l.Number}"))
            .ToList();

        var projects = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => File.ReadAllText(p).Contains("\"QuestPDF\"", StringComparison.Ordinal))
            .Select(p => Path.GetFileName(p))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(offenders, Is.Empty, "QuestPDF named outside Infrastructure/Printing");
            Assert.That(projects, Is.EqualTo(["Nornis.Infrastructure.csproj"]));
        });
    }

    /// <summary>
    /// Feature 34: the booklet on screen, the download and the filed print are the same PDF
    /// because one printer makes all three. A second caller of the model or the renderer would
    /// be a second booklet that can drift (the HTML preview and the filed PDF did, before).
    /// </summary>
    [Test]
    public void OnlyTheBookletPrinter_BuildsOrRendersABooklet()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/Nornis.Application/Prep/AdventureBookletPrinter.cs",
            "src/Nornis.Application/Prep/BookletModel.cs",
            "src/Nornis.Application/Prep/IBookletPdfRenderer.cs",
            "src/Nornis.Infrastructure/Printing/QuestPdfBookletRenderer.cs",
            "src/Nornis.Api/Program.cs",
        };

        var offenders = SourceFiles()
            .Where(f => !allowed.Contains(f.Relative))
            .SelectMany(f => CodeLines(f)
                .Where(l => Regex.IsMatch(l.Text, @"\b(IBookletPdfRenderer|QuestPdfBookletRenderer|BookletModel)\b"))
                .Select(l => $"{f.Relative}:{l.Number}"))
            .ToList();

        Assert.That(offenders, Is.Empty, "a booklet built or rendered outside AdventureBookletPrinter");
    }

    /// <summary>
    /// Only <see cref="GameBlocks"/> reads a game block's fence (feature 35). The booklet, the
    /// block warnings and Export to Nornis all parse through it; a second reader of the info
    /// string would be a second grammar that drifts. The two <c>PrepMarkup</c> files name the
    /// info strings (one is the Web's mirror, pinned by a mirror test) and the editor writes them.
    /// </summary>
    [Test]
    public void OnlyGameBlocks_ReadsAGameBlocksFence()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/Nornis.Application/Prep/GameBlocks.cs",
            "src/Nornis.Application/Prep/PrepMarkup.cs",
            "src/Nornis.Web/Services/PrepMarkup.cs",
        };

        var offenders = SourceFiles()
            .Where(f => !allowed.Contains(f.Relative))
            .SelectMany(f => CodeLines(f)
                .Where(l => Regex.IsMatch(l.Text, @"\b(StatBlockInfo|ItemBlockInfo)\b|""(statblock|item)""|```(statblock|item)\b"))
                .Select(l => $"{f.Relative}:{l.Number}"))
            .ToList();

        Assert.That(offenders, Is.Empty, "a game block's fence read outside GameBlocks");
    }

    /// <summary>
    /// Feature 37, Requirement 1.6: the design draft never prints and never exports. These are
    /// the only files that may read or write it; the booklet, Export to Nornis and every other
    /// reader of an Adventure read its sections.
    /// </summary>
    [Test]
    public void OnlyDesignMode_ReadsTheDraft()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/Nornis.Domain/Entities/Adventure.cs",
            "src/Nornis.Infrastructure/Persistence/Configurations/AdventureConfiguration.cs",
            "src/Nornis.Infrastructure/Persistence/Repositories/AdventureRepository.cs",
            "src/Nornis.Application/Services/AdventureDesignService.cs",
            "src/Nornis.Application/Services/AdventureDraftPlanService.cs",
            "src/Nornis.Application/Prep/AdventureConversationPrompt.cs",
            "src/Nornis.Api/Controllers/PrepController.cs",
        };

        var offenders = SourceFiles()
            .Where(f => !allowed.Contains(f.Relative))
            .SelectMany(f => CodeLines(f).Where(l => Regex.IsMatch(l.Text, @"\bDraftMarkdown\b")).Select(l => $"{f.Relative}:{l.Number}"))
            .ToList();

        Assert.That(offenders, Is.Empty, "a new reader of the design draft; if it prints or exports, it breaks Requirement 1.6");
    }

    /// <summary>
    /// Feature 37, Requirement 3.4: the cut is code's, checked so no word changes. Only the design
    /// service writes sections from a draft, through the cutter; the plan service cuts only to
    /// preview each row's opening words.
    /// </summary>
    [Test]
    public void OnlyTheCutter_WritesSectionsFromTheDraft()
    {
        var cutters = SourceFiles()
            .Where(f => CodeLines(f).Any(l => l.Text.Contains("AdventureDraftCutter.Cut(", StringComparison.Ordinal)))
            .Select(f => f.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();
        var writers = SourceFiles()
            .Where(f => CodeLines(f).Any(l => l.Text.Contains(".TryWriteSplitAsync(", StringComparison.Ordinal)))
            .Select(f => f.Relative)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(cutters, Is.EqualTo(
            [
                "src/Nornis.Application/Services/AdventureDesignService.cs",
                "src/Nornis.Application/Services/AdventureDraftPlanService.cs",
            ]));
            Assert.That(writers, Is.EqualTo(["src/Nornis.Application/Services/AdventureDesignService.cs"]));
        });
    }

    /// <summary>
    /// Feature 40, Requirement 6.1: an Adventure records that it drew on the web only where a
    /// research answer's text goes in, which is Put in section, Put in draft and Apply, and the
    /// record is read off the answer itself, never chosen by a caller.
    /// </summary>
    [Test]
    public void OnlyAResearchAnswersText_RecordsResearchedOnWeb()
    {
        var deciders = SourceFiles()
            .SelectMany(f => CodeLines(f)
                .Where(l => l.Text.Contains("researchedOnWeb:", StringComparison.Ordinal))
                .Select(l => (f.Relative, Decided: Decided(l.Text))))
            .OrderBy(d => d.Relative, StringComparer.Ordinal).ThenBy(d => d.Decided, StringComparer.Ordinal)
            .ToList();

        (string, string)[] expected =
        [
            ("src/Nornis.Application/Prep/AdventurePrintDetails.cs", "the record"),
            ("src/Nornis.Application/Services/AdventureConversationService.cs", "the answer"),
            ("src/Nornis.Application/Services/AdventureDesignService.cs", "the answer"),
            ("src/Nornis.Application/Services/AdventureDesignService.cs", "the answer"),
        ];
        Assert.That(deciders, Is.EqualTo(expected),
            "Put in section, Put in draft and Apply read it off the answer; the print only reads the record");

        static string Decided(string line) =>
            line.Contains("WebResearch is not null", StringComparison.Ordinal) ? "the answer"
            : line.Contains("researchedOnWeb: false", StringComparison.Ordinal) ? "never"
            : line.Contains("AiUse.ResearchedOnWeb", StringComparison.Ordinal) ? "the record"
            : line.Trim();
    }

    /// <summary>
    /// Feature 38, Requirement 1: AI use is recorded, never chosen. Only the repository sets the
    /// recorded flags, inside the writes that put the Loremaster's words into an Adventure, so
    /// the flag and the text commit together. Those writes have a known list of callers: a new
    /// path that writes AI text has to join this list deliberately.
    /// </summary>
    [Test]
    public void OnlyTheRepository_RecordsAiUse_FromAKnownListOfAiWriters()
    {
        var recorders = SourceFiles()
            .Where(f => CodeLines(f).Any(l => Regex.IsMatch(l.Text, @"\b(WroteText|MadeArt|ResearchedOnWeb)\s*=\s*true\b")))
            .Select(f => f.Relative)
            .ToList();
        var accepters = SourceFiles()
            .Where(f => CodeLines(f).Any(l => l.Text.Contains(".TryAcceptDraftAsync(", StringComparison.Ordinal)))
            .Select(f => f.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();
        var draftWriters = SourceFiles()
            .Where(f => CodeLines(f).Any(l => l.Text.Contains(".IsLoremasterWriting()", StringComparison.Ordinal)))
            .Select(f => f.Relative)
            .ToList();
        var artSetters = SourceFiles()
            .Where(f => CodeLines(f).Any(l => l.Text.Contains("generated: true", StringComparison.Ordinal)))
            .Select(f => f.Relative)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(recorders, Is.EqualTo(["src/Nornis.Infrastructure/Persistence/Repositories/AdventureRepository.cs"]));
            Assert.That(accepters, Is.EqualTo(["src/Nornis.Application/Services/AdventureConversationService.cs"]),
                "every accept records AI text: only Put in section may accept");
            Assert.That(draftWriters, Is.EqualTo(["src/Nornis.Application/Services/AdventureDesignService.cs"]));
            Assert.That(artSetters, Is.EqualTo(["src/Nornis.Application/Prep/Covers/AdventureCoverService.cs"]),
                "only choosing a generated option records AI art");
        });
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
