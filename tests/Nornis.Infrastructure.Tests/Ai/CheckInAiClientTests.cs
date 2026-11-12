using Nornis.Application.Ai;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>The chooser's parse (feature 44): raw strings out, the service resolves them.</summary>
[TestFixture]
public class CheckInAiClientTests
{
    [Test]
    public void ParsePicks_ReadsEveryField_AndKeepsNullsNull()
    {
        var picks = CheckInAiClient.ParsePicks("""
            {"picks":[
              {"candidate":"c2","question":"Is the witch's warning still a thread?","why":"Quiet for 4 sessions.","suggestedRule":"Koves is background.","recordOnArtifactId":"5f2d6c6e-1b1e-4c1e-9a5e-2d3f4a5b6c7d"},
              {"candidate":"c1","question":"Keep the lamp?","why":"Mentioned once.","suggestedRule":null,"recordOnArtifactId":null}
            ]}
            """);

        Assert.That(picks, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(picks[0].CandidateKey, Is.EqualTo("c2"));
            Assert.That(picks[0].Question, Is.EqualTo("Is the witch's warning still a thread?"));
            Assert.That(picks[0].Why, Is.EqualTo("Quiet for 4 sessions."));
            Assert.That(picks[0].SuggestedRule, Is.EqualTo("Koves is background."));
            Assert.That(picks[0].RecordOnArtifactId, Is.EqualTo("5f2d6c6e-1b1e-4c1e-9a5e-2d3f4a5b6c7d"));
            Assert.That(picks[1].SuggestedRule, Is.Null);
            Assert.That(picks[1].RecordOnArtifactId, Is.Null);
        });
    }

    [Test]
    public void ParsePicks_SkipsAPickWithoutACandidate()
    {
        var picks = CheckInAiClient.ParsePicks("""{"picks":[{"candidate":" ","question":"q","why":"w","suggestedRule":null,"recordOnArtifactId":null}]}""");

        Assert.That(picks, Is.Empty);
    }

    [Test]
    public void ParsePicks_WithoutThePicksArray_Throws()
    {
        Assert.Throws<AiParseException>(() => CheckInAiClient.ParsePicks("""{"choices":[]}"""));
        Assert.Throws<AiParseException>(() => CheckInAiClient.ParsePicks("null"));
    }
}
