using Nornis.Application.Ai;
using NUnit.Framework;

namespace Nornis.Application.Tests.Ai;

/// <summary>
/// Cases mirror the real export format: YAML frontmatter, doubled-bracket UUID links,
/// block references with aliases, plain wikilinks, and curly-brace annotations.
/// </summary>
[TestFixture]
public class ImportedNoteNormalizerTests
{
    [Test]
    public void Normalize_StripsYamlFrontmatter()
    {
        var body = "---\ntitle: \"2024-01-24\"\ntype: Session\ncreated: 2025-12-22 00:34:17\n---\n\n# 2024-01-24\n\nContent here.";

        var result = ImportedNoteNormalizer.Normalize(body);

        Assert.That(result, Does.Not.Contain("---"));
        Assert.That(result, Does.Not.Contain("type: Session"));
        Assert.That(result, Does.StartWith("# 2024-01-24"));
        Assert.That(result, Does.Contain("Content here."));
    }

    [Test]
    public void Normalize_DoubledUuidLink_BecomesPlainLink()
    {
        var body = "Heading to [[[[5aa11353-d0ab-4616-9527-a77543e0b0ff|Kastor]]]]";

        var result = ImportedNoteNormalizer.Normalize(body);

        Assert.That(result, Is.EqualTo("Heading to [[Kastor]]"));
    }

    [Test]
    public void Normalize_BlockReferenceWithAlias_KeepsAliasAsLink()
    {
        var body = "Known to have a [[[[5aa11353-d0ab-4616-9527-a77543e0b0ff|Kastor]]#^0cce5e|Bell Tower]] that is ancient";

        var result = ImportedNoteNormalizer.Normalize(body);

        Assert.That(result, Is.EqualTo("Known to have a [[Bell Tower]] that is ancient"));
    }

    [Test]
    public void Normalize_SingleUuidLink_BecomesPlainLink()
    {
        var body = "{Question: Is the chieftain named [[0c0dc6c2-8ab5-4d81-b961-6324a1493d51|Ksandukha]] Ksandukha?}";

        var result = ImportedNoteNormalizer.Normalize(body);

        Assert.That(result, Is.EqualTo("{Question: Is the chieftain named [[Ksandukha]] Ksandukha?}"),
            "curly-brace annotations must be preserved; only the link is rewritten");
    }

    [Test]
    public void Normalize_PlainWikilink_IsUnchanged()
    {
        var body = "[[Illgoblins]] - pretty nasty!";

        var result = ImportedNoteNormalizer.Normalize(body);

        Assert.That(result, Is.EqualTo("[[Illgoblins]] - pretty nasty!"));
    }

    [Test]
    public void Normalize_MultipleLinksOnOneLine_AllRewritten()
    {
        var body = "3 days from [[[[a35f7e62-fc1c-4b9a-ad8d-eae0a6ccfcce|Thistlehold]]]] to [[[[5aa11353-d0ab-4616-9527-a77543e0b0ff|Kastor]]]]";

        var result = ImportedNoteNormalizer.Normalize(body);

        Assert.That(result, Is.EqualTo("3 days from [[Thistlehold]] to [[Kastor]]"));
    }

    [Test]
    public void Normalize_NoMarkup_ReturnsTrimmedBodyUnchanged()
    {
        var body = "  We met Captain Voss in Black Harbor.  ";

        var result = ImportedNoteNormalizer.Normalize(body);

        Assert.That(result, Is.EqualTo("We met Captain Voss in Black Harbor."));
    }

    [Test]
    public void Normalize_FrontmatterOnlyNote_ReturnsEmpty()
    {
        var body = "---\ntitle: \"stub\"\n---\n   \n";

        var result = ImportedNoteNormalizer.Normalize(body);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Normalize_FullSampleShape_ProducesCleanText()
    {
        var body = """
            ---
            title: "2024-01-24"
            type: Session
            ---

            # 2024-01-24

            * Heading to [[[[5aa11353-d0ab-4616-9527-a77543e0b0ff|Kastor]]]]
            * Known to have a [[[[5aa11353-d0ab-4616-9527-a77543e0b0ff|Kastor]]#^0cce5e|Bell Tower]]
            * {Question: Is the chieftain named [[0c0dc6c2-8ab5-4d81-b961-6324a1493d51|Ksandukha]] Ksandukha?}
            * [[Illgoblins]] - pretty nasty!
            """;

        var result = ImportedNoteNormalizer.Normalize(body);

        Assert.That(result, Does.Contain("[[Kastor]]"));
        Assert.That(result, Does.Contain("[[Bell Tower]]"));
        Assert.That(result, Does.Contain("{Question: Is the chieftain named [[Ksandukha]] Ksandukha?}"));
        Assert.That(result, Does.Contain("[[Illgoblins]]"));
        Assert.That(result, Does.Not.Contain("5aa11353"), "UUIDs must not reach the AI");
        Assert.That(result, Does.Not.Contain("#^"), "block references must not reach the AI");
        Assert.That(result, Does.Not.Contain("[[[["), "doubled brackets must be collapsed");
    }
}
