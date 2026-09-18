using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests;

[TestFixture]
public class CaptureBackupTests
{
    [Test]
    public void Keys_AreDistinctPerWorld_AndBetweenNotesAndForm()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.That(CaptureBackup.NotesKey(a), Is.Not.EqualTo(CaptureBackup.NotesKey(b)));
        Assert.That(CaptureBackup.NotesKey(a), Is.Not.EqualTo(CaptureBackup.FormKey(a)));
    }

    [Test]
    public void Form_RoundTripsThroughJson()
    {
        var form = new CaptureFormBackup(
            "Session 7", "SessionNote", "GMOnly", null, new DateTime(2026, 9, 17), Guid.NewGuid(), false,
            new DateTimeOffset(2026, 9, 17, 20, 15, 0, TimeSpan.Zero));

        var back = CaptureBackup.TryParse(CaptureBackup.Serialize(form));

        Assert.That(back, Is.EqualTo(form));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not json")]
    [TestCase("[1,2,3]")]
    public void Garbage_IsNotRestored(string? json)
    {
        Assert.That(CaptureBackup.TryParse(json), Is.Null);
    }

    [TestCase(0, "a moment ago")]
    [TestCase(59, "a moment ago")]
    [TestCase(60, "a minute ago")]
    [TestCase(5 * 60, "5 minutes ago")]
    [TestCase(60 * 60, "an hour ago")]
    [TestCase(3 * 60 * 60 + 30 * 60, "3 hours ago")]
    [TestCase(24 * 60 * 60, "yesterday")]
    [TestCase(3 * 24 * 60 * 60, "3 days ago")]
    public void DescribeAge_ReadsAsWords(int seconds, string expected)
    {
        Assert.That(CaptureBackup.DescribeAge(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }
}
