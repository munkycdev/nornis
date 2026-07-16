using System.Text.Json;
using Nornis.Application.Messaging;
using NUnit.Framework;

namespace Nornis.Application.Tests.Messaging;

[TestFixture]
public class ExtractionMessageTests
{
    [Test]
    public void OldMessageWithoutKind_DeserializesToExtraction()
    {
        // Messages enqueued before the Kind field existed must keep routing to
        // normal extraction after a worker deploy.
        var sourceId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var json = $$"""{"SourceId":"{{sourceId}}","WorldId":"{{worldId}}"}""";

        var message = JsonSerializer.Deserialize<ExtractionMessage>(json);

        Assert.That(message!.Kind, Is.EqualTo(ExtractionKind.Extraction));
        Assert.That(message.SourceId, Is.EqualTo(sourceId));
    }

    [Test]
    public void BackfillMessage_RoundTripsKind()
    {
        var message = new ExtractionMessage(Guid.NewGuid(), Guid.NewGuid(), ExtractionKind.RelationshipBackfill);

        var roundTripped = JsonSerializer.Deserialize<ExtractionMessage>(JsonSerializer.Serialize(message));

        Assert.That(roundTripped!.Kind, Is.EqualTo(ExtractionKind.RelationshipBackfill));
    }
}
