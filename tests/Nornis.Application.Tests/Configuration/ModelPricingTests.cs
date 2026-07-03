using NUnit.Framework;
using Nornis.Application.Configuration;

namespace Nornis.Application.Tests.Configuration;

[TestFixture]
public class ModelPricingTests
{
    [Test]
    public void Defaults_AreZero()
    {
        var pricing = new ModelPricing();

        Assert.Multiple(() =>
        {
            Assert.That(pricing.InputPerMillionTokensUsd, Is.EqualTo(0m));
            Assert.That(pricing.OutputPerMillionTokensUsd, Is.EqualTo(0m));
        });
    }

    [Test]
    public void Properties_CanBeSet()
    {
        var pricing = new ModelPricing
        {
            InputPerMillionTokensUsd = 5.00m,
            OutputPerMillionTokensUsd = 15.00m
        };

        Assert.Multiple(() =>
        {
            Assert.That(pricing.InputPerMillionTokensUsd, Is.EqualTo(5.00m));
            Assert.That(pricing.OutputPerMillionTokensUsd, Is.EqualTo(15.00m));
        });
    }
}
