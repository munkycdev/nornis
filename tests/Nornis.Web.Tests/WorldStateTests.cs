using NUnit.Framework;
using Nornis.Web.ApiClient;
using Nornis.Web.State;

namespace Nornis.Web.Tests;

[TestFixture]
public class WorldStateTests
{
    private static WorldState CreateState() =>
        new(new NornisApiClient(new HttpClient { BaseAddress = new Uri("http://localhost") }));

    [Test]
    public void SetContinuity_ReplacesAssessment_AndRaisesChanged()
    {
        var state = CreateState();
        var changedRaised = false;
        state.Changed += () => changedRaised = true;

        var assessment = new ContinuityAssessment(
            HasData: true,
            AssessmentId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Model: "test-model",
            Score: 90,
            EffectiveScore: 84,
            HeuristicScore: 90,
            Findings: []);

        state.SetContinuity(assessment);

        Assert.That(state.Continuity, Is.SameAs(assessment));
        Assert.That(changedRaised, Is.True);
    }

    [Test]
    public void SetContinuity_AcceptsNull_AndRaisesChanged()
    {
        var state = CreateState();
        var changedRaised = false;
        state.Changed += () => changedRaised = true;

        state.SetContinuity(null);

        Assert.That(state.Continuity, Is.Null);
        Assert.That(changedRaised, Is.True);
    }
}
