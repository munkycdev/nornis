using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

public partial class ExtractionServiceCampaignContextTests
{
    [Test]
    public async Task ProcessExtractionAsync_ImportedNote_NormalizesBodyBeforeAi()
    {
        var source = SeedQueuedSource(campaignId: null);
        source.Type = SourceType.ImportedNote;
        source.Body = """
            ---
            title: x
            ---
            Heading to [[[[5aa11353-d0ab-4616-9527-a77543e0b0ff|Kastor]]]]
            """;

        var outcome = await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(outcome.Type, Is.EqualTo(OutcomeType.Success));
        Assert.That(_aiClient.Requests[0].SourceBody, Is.EqualTo("Heading to [[Kastor]]"));
    }

    [Test]
    public async Task ProcessExtractionAsync_SessionNote_BodyIsNotNormalized()
    {
        var source = SeedQueuedSource(campaignId: null);
        source.Body = "Wikilink-looking text [[[[5aa11353-d0ab-4616-9527-a77543e0b0ff|Kastor]]]] stays as written";

        await _sut.ProcessExtractionAsync(source.Id, WorldId, CancellationToken.None);

        Assert.That(_aiClient.Requests[0].SourceBody, Does.Contain("[[[["),
            "only ImportedNote sources are normalized");
    }
}
