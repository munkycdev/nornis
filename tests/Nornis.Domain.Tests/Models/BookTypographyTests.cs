using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>
/// The type a booklet is set in: nothing chosen is today's type, each bound holds at its limit
/// and refuses one past it or off its step, and every face has a name to register under.
/// </summary>
[TestFixture]
public class BookTypographyTests
{
    [Test]
    public void NothingChosen_IsValid_AndIsNewsreaderTenOnOnePointThree()
    {
        var type = new BookTypography();

        Assert.Multiple(() =>
        {
            Assert.That(type.Validate(), Is.Null);
            Assert.That(type.FontOrDefault, Is.EqualTo(BookFont.Newsreader));
            Assert.That(type.SizeOrDefault, Is.EqualTo(10m));
            Assert.That(type.LineSpacingOrDefault, Is.EqualTo(1.3m));
        });
    }

    [TestCase(8.0)]
    [TestCase(8.5)]
    [TestCase(11.5)]
    [TestCase(14.0)]
    public void ASizeOnAHalfPointWithinTheBounds_IsValid(double size) =>
        Assert.That(new BookTypography { Size = (decimal)size }.Validate(), Is.Null);

    [TestCase(7.5)]
    [TestCase(14.5)]
    [TestCase(10.2)]
    public void ASizePastTheBoundsOrOffItsStep_IsRefused(double size) =>
        Assert.That(new BookTypography { Size = (decimal)size }.Validate(), Is.EqualTo(nameof(BookTypography.Size)));

    [TestCase(1.0)]
    [TestCase(1.35)]
    [TestCase(2.0)]
    public void LineSpacingOnATwentiethWithinTheBounds_IsValid(double spacing) =>
        Assert.That(new BookTypography { LineSpacing = (decimal)spacing }.Validate(), Is.Null);

    [TestCase(0.95)]
    [TestCase(2.05)]
    [TestCase(1.33)]
    public void LineSpacingPastTheBoundsOrOffItsStep_IsRefused(double spacing) =>
        Assert.That(new BookTypography { LineSpacing = (decimal)spacing }.Validate(), Is.EqualTo(nameof(BookTypography.LineSpacing)));

    [Test]
    public void AFontThatIsNotOneOfTheBooklets_IsRefused() =>
        Assert.That(new BookTypography { Font = (BookFont)99 }.Validate(), Is.EqualTo(nameof(BookTypography.Font)));

    [Test]
    public void EveryFont_HasItsOwnName() =>
        Assert.That(Enum.GetValues<BookFont>().Select(BookTypography.NameOf), Is.Unique.And.All.Not.Empty);
}
