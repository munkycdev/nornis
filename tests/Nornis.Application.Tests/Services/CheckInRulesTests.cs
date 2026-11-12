using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>The small tables of the check-in (feature 44): answers per kind, status per answer, cadence.</summary>
[TestFixture]
public class CheckInRulesTests
{
    [Test]
    public void EveryKind_NamesItsAnswers_AndEveryAnswerBelongsToSomeKind()
    {
        foreach (var kind in Enum.GetValues<CheckInQuestionKind>())
        {
            Assert.That(CheckInAnswers.AllowedFor(kind), Does.Contain(CheckInAnswer.Skip), $"{kind} must be skippable");
        }

        var placed = Enum.GetValues<CheckInQuestionKind>().SelectMany(CheckInAnswers.AllowedFor).Distinct();
        Assert.That(placed, Is.EquivalentTo(Enum.GetValues<CheckInAnswer>()));
    }

    [TestCase(CheckInQuestionKind.Storyline, CheckInAnswer.Pursuing, true)]
    [TestCase(CheckInQuestionKind.Storyline, CheckInAnswer.Archive, false)]
    [TestCase(CheckInQuestionKind.Entry, CheckInAnswer.Archive, true)]
    [TestCase(CheckInQuestionKind.Entry, CheckInAnswer.Done, false)]
    [TestCase(CheckInQuestionKind.Pattern, CheckInAnswer.Stop, true)]
    [TestCase(CheckInQuestionKind.Pattern, CheckInAnswer.Background, false)]
    public void IsAllowed_FollowsTheTable(CheckInQuestionKind kind, CheckInAnswer answer, bool allowed) =>
        Assert.That(CheckInAnswers.IsAllowed(kind, answer), Is.EqualTo(allowed));

    [TestCase(CheckInQuestionKind.Storyline, CheckInAnswer.Background, ArtifactStatus.Dormant)]
    [TestCase(CheckInQuestionKind.Storyline, CheckInAnswer.Done, ArtifactStatus.Resolved)]
    [TestCase(CheckInQuestionKind.Entry, CheckInAnswer.Archive, ArtifactStatus.Archived)]
    [TestCase(CheckInQuestionKind.Storyline, CheckInAnswer.Pursuing, null)]
    [TestCase(CheckInQuestionKind.Entry, CheckInAnswer.Keep, null)]
    [TestCase(CheckInQuestionKind.Pattern, CheckInAnswer.Stop, null)]
    [TestCase(CheckInQuestionKind.Pattern, CheckInAnswer.Skip, null)]
    public void StatusFor_SetsAStatusOnlyWhereTheSpecSays(CheckInQuestionKind kind, CheckInAnswer answer, ArtifactStatus? expected) =>
        Assert.That(CheckInAnswers.StatusFor(kind, answer), Is.EqualTo(expected));

    [Test]
    public void SessionsSince_CountsDatedSessionsAfterTheAnchor_OrAllWithoutOne()
    {
        var day = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sessions = new[] { day, day.AddDays(1), day.AddDays(2), day.AddDays(3) };

        Assert.Multiple(() =>
        {
            Assert.That(CheckInCadence.SessionsSince(sessions, null), Is.EqualTo(4));
            Assert.That(CheckInCadence.SessionsSince(sessions, day.AddDays(1)), Is.EqualTo(2), "the anchor's own session does not count");
            Assert.That(CheckInCadence.SessionsSince(sessions, day.AddDays(9)), Is.EqualTo(0));
            Assert.That(CheckInCadence.IsDue(2, 3), Is.False);
            Assert.That(CheckInCadence.IsDue(3, 3), Is.True);
            Assert.That(CheckInCadence.IsDue(3, 0), Is.False, "a zero cadence never comes due");
        });
    }
}
