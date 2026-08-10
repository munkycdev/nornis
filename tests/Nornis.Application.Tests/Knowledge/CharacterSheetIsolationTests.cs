using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Application.Tests.Knowledge;

/// <summary>
/// Guards the one rule the written character sheet lives under: it must never reach an AI path.
///
/// Everything the Loremaster can quote traces back to a source. The sheet is the single piece
/// of user text in the system that carries no provenance at all — a player types it, nothing
/// cites it, and no reviewer ever accepted it. Letting it into a prompt would let unprovenanced
/// text be answered as though it were canon, which is the product's central promise inverted.
///
/// This scans source rather than asserting behaviour because the damage is done at the moment
/// someone *writes* the read. A context assembler that quietly starts including
/// <c>character.Sheet</c> produces confident, plausible, uncitable answers, and no assertion
/// about output would name the cause. A compiler cannot express "this property is readable
/// here but not there", so the guard is mechanical and lives here.
///
/// If a genuine need to feed a sheet to an AI path ever arrives, it does not arrive by editing
/// this list — it arrives by giving the sheet provenance first.
/// </summary>
[TestFixture]
public class CharacterSheetIsolationTests
{
    /// <summary>
    /// Directories whose whole job is assembling text for a model, plus the retrieval layer
    /// that feeds them.
    /// </summary>
    private static readonly string[] AiPathDirectories =
    [
        Path.Combine("src", "Nornis.Application", "Knowledge"),
        Path.Combine("src", "Nornis.Infrastructure", "Ai"),
        Path.Combine("src", "Nornis.Worker"),
    ];

    /// <summary>
    /// Services that compose prompts or retrieval context. Named individually because they sit
    /// in the same folder as services that legitimately read the sheet.
    /// </summary>
    private static readonly string[] AiPathFiles =
    [
        Path.Combine("src", "Nornis.Application", "Services", "LoremasterService.cs"),
        Path.Combine("src", "Nornis.Application", "Services", "ExtractionService.cs"),
        Path.Combine("src", "Nornis.Application", "Services", "WorldDigestService.cs"),
        Path.Combine("src", "Nornis.Application", "Services", "CampaignRecapService.cs"),
        Path.Combine("src", "Nornis.Application", "Services", "ArtifactSummaryService.cs"),
        Path.Combine("src", "Nornis.Application", "Services", "ContinuityAuditService.cs"),
        Path.Combine("src", "Nornis.Application", "Services", "LibraryIndexingService.cs"),
    ];

    /// <summary>Any read of the sheet property, however it is reached.</summary>
    private static readonly Regex SheetRead = new(@"\.Sheet\b", RegexOptions.Compiled);

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Nornis.Application")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return dir!;
    }

    private static IEnumerable<string> AiPathSourceFiles()
    {
        var root = RepositoryRoot().FullName;

        foreach (var relative in AiPathDirectories)
        {
            var directory = Path.Combine(root, relative);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                // obj/ holds generated copies of the same source; scanning them double-reports.
                if (!file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                {
                    yield return file;
                }
            }
        }

        foreach (var relative in AiPathFiles)
        {
            var file = Path.Combine(root, relative);
            if (File.Exists(file))
            {
                yield return file;
            }
        }
    }

    [Test]
    public void NoAiPathReadsTheWrittenSheet()
    {
        var offenders = AiPathSourceFiles()
            .Where(file => SheetRead.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetFileName(file))
            .ToList();

        Assert.That(offenders, Is.Empty,
            "The written character sheet carries no provenance and must not reach a prompt, "
            + "retrieval context, or embedding input. Offending file(s): "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The guard is worthless if it scans nothing — a renamed file or folder would turn it
    /// green forever, which is the failure mode that makes a guard a claim instead of a guard.
    /// Every path it names must resolve.
    /// </summary>
    [Test]
    public void TheGuardActuallyScansWhatItNames()
    {
        var root = RepositoryRoot().FullName;

        var missingDirectories = AiPathDirectories
            .Where(d => !Directory.Exists(Path.Combine(root, d)))
            .ToList();

        var missingFiles = AiPathFiles
            .Where(f => !File.Exists(Path.Combine(root, f)))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(missingDirectories, Is.Empty,
                "an AI-path directory has moved; the guard silently stopped covering it");
            Assert.That(missingFiles, Is.Empty,
                "an AI-path file has moved or been renamed; the guard silently stopped covering it");
            Assert.That(AiPathSourceFiles().ToList(), Is.Not.Empty);
        });
    }
}
