using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests;

[TestFixture]
public class NotesBackupTests
{
    [Test]
    public void Keys_AreDistinctPerOwner_AndBetweenTheFourKinds()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.That(NotesBackup.CaptureNotesKey(a), Is.Not.EqualTo(NotesBackup.CaptureNotesKey(b)));
        Assert.That(NotesBackup.SourceEditKey(a), Is.Not.EqualTo(NotesBackup.SourceEditKey(b)));

        // The same id in four roles must never share a key — a world, a source and a character
        // could in principle carry the same GUID and must not read each other's notes.
        var keys = new[]
        {
            NotesBackup.CaptureNotesKey(a), NotesBackup.CaptureFormKey(a),
            NotesBackup.SourceEditKey(a), NotesBackup.CharacterSheetKey(a)
        };
        Assert.That(keys, Is.Unique);
    }

    [Test]
    public void Form_RoundTripsThroughJson()
    {
        var form = new CaptureFormBackup(
            "Session 7", "SessionNote", "GMOnly", null, new DateTime(2026, 9, 17), Guid.NewGuid(), false,
            new DateTimeOffset(2026, 9, 17, 20, 15, 0, TimeSpan.Zero));

        var back = NotesBackup.TryParse(NotesBackup.Serialize(form));

        Assert.That(back, Is.EqualTo(form));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not json")]
    [TestCase("[1,2,3]")]
    public void Garbage_IsNotRestored(string? json)
    {
        Assert.That(NotesBackup.TryParse(json), Is.Null);
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
        Assert.That(NotesBackup.DescribeAge(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }
}
