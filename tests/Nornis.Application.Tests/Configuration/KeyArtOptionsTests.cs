using Nornis.Application.Configuration;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Configuration;

[TestFixture]
public class KeyArtOptionsTests
{
    /// <summary>The defaults the Key art card and requirement 2 are written against.</summary>
    [Test]
    public void TheDefaults_AreTheSpecs()
    {
        var options = new KeyArtOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.MaxImagesPerMonth, Is.EqualTo(8));
            Assert.That(options.OptionsPerGeneration, Is.EqualTo(2));
            Assert.That(options.MaxUploadBytes, Is.EqualTo(10 * 1024 * 1024));
            Assert.That(options.SharpWidth, Is.EqualTo(1_600));
            Assert.That(options.FirstNonPositiveBound(), Is.Null);
        });
    }

    /// <summary>One Generate must fit in a month, or the cap would refuse every click.</summary>
    [Test]
    public void OneGeneration_FitsInTheMonth() =>
        Assert.That(new KeyArtOptions().OptionsPerGeneration, Is.LessThanOrEqualTo(new KeyArtOptions().MaxImagesPerMonth));

    [Test]
    public void ABoundOfZero_IsNamed() =>
        Assert.That(new KeyArtOptions { OptionsPerGeneration = 0 }.FirstNonPositiveBound(), Is.EqualTo(nameof(KeyArtOptions.OptionsPerGeneration)));

    [Test]
    public void TheBrief_IsACoversLength() =>
        Assert.That(WorldKeyArt.MaxBriefChars, Is.EqualTo(AdventureCover.MaxBriefChars));
}
