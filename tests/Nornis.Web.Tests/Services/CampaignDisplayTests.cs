using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class CampaignDisplayTests
{
    [Test]
    public void RecapBody_LiftsTheHeadingThePageAlreadyShows()
    {
        var body = CampaignDisplay.RecapBody("## The story so far\nThe trail began in Karvosti.\n\n## Where things stand\nStill lost.");

        Assert.That(body, Is.EqualTo("The trail began in Karvosti.\n\n## Where things stand\nStill lost."));
    }

    [Test]
    public void RecapBody_MatchesByWords_NotLevelOrSuffix()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CampaignDisplay.RecapBody("# the story so far — in order\r\nProse."), Is.EqualTo("Prose."));
            Assert.That(CampaignDisplay.RecapBody("### The Story So Far\nProse."), Is.EqualTo("Prose."));
        });
    }

    [Test]
    public void RecapBody_LeavesOtherOpeningsAlone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CampaignDisplay.RecapBody("## Where things stand\nProse."), Is.EqualTo("## Where things stand\nProse."));
            Assert.That(CampaignDisplay.RecapBody("The story so far is short."), Is.EqualTo("The story so far is short."),
                "prose that merely begins with the words is not a heading");
            Assert.That(CampaignDisplay.RecapBody(CampaignRecapEmpty), Is.EqualTo(CampaignRecapEmpty));
            Assert.That(CampaignDisplay.RecapBody(null), Is.Empty);
        });
    }

    private const string CampaignRecapEmpty = "Nothing from this campaign has been revealed to the party yet.";

    [Test]
    public void PlayedSpan_PrefersDeclaredDates_ThenSessions()
    {
        var declared = Campaign(new DateTimeOffset(2025, 10, 8, 0, 0, 0, TimeSpan.Zero), null);
        var undeclared = Campaign(null, null);

        Assert.Multiple(() =>
        {
            Assert.That(CampaignDisplay.PlayedSpan(declared, null, null, 25), Is.EqualTo("Began 2025-10-08 · 25 sessions"));
            Assert.That(CampaignDisplay.PlayedSpan(undeclared,
                new DateTimeOffset(2025, 10, 8, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), 1),
                Is.EqualTo("2025-10-08 – 2026-03-01 · 1 session"));
            Assert.That(CampaignDisplay.PlayedSpan(undeclared, null, null, 0), Is.EqualTo("0 sessions"));
        });
    }

    private static CampaignDto Campaign(DateTimeOffset? started, DateTimeOffset? ended) => new(
        Guid.NewGuid(), Guid.NewGuid(), "The Bleeding Heart", null, "Active", started, ended,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid());
}
