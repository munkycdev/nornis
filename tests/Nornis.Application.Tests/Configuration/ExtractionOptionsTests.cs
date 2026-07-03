using NUnit.Framework;
using Nornis.Application.Configuration;

namespace Nornis.Application.Tests.Configuration;

[TestFixture]
public class ExtractionOptionsTests
{
    [Test]
    public void Defaults_AreAppliedCorrectly()
    {
        var options = new ExtractionOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.AiModel, Is.EqualTo(string.Empty));
            Assert.That(options.AiEndpoint, Is.EqualTo(string.Empty));
            Assert.That(options.AiTimeoutSeconds, Is.EqualTo(60));
            Assert.That(options.MaxArtifactContextCount, Is.EqualTo(50));
            Assert.That(options.MaxFactsPerArtifact, Is.EqualTo(20));
            Assert.That(options.MaxParseRetryAttempts, Is.EqualTo(2));
            Assert.That(options.ModelPricing, Is.Not.Null);
            Assert.That(options.ModelPricing, Is.Empty);
        });
    }

    [Test]
    public void ModelPricing_CanStoreMultipleModels()
    {
        var options = new ExtractionOptions
        {
            ModelPricing = new Dictionary<string, ModelPricing>
            {
                ["gpt-4o"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 2.50m,
                    OutputPerMillionTokensUsd = 10.00m
                },
                ["gpt-4o-mini"] = new ModelPricing
                {
                    InputPerMillionTokensUsd = 0.15m,
                    OutputPerMillionTokensUsd = 0.60m
                }
            }
        };

        Assert.Multiple(() =>
        {
            Assert.That(options.ModelPricing, Has.Count.EqualTo(2));
            Assert.That(options.ModelPricing["gpt-4o"].InputPerMillionTokensUsd, Is.EqualTo(2.50m));
            Assert.That(options.ModelPricing["gpt-4o"].OutputPerMillionTokensUsd, Is.EqualTo(10.00m));
            Assert.That(options.ModelPricing["gpt-4o-mini"].InputPerMillionTokensUsd, Is.EqualTo(0.15m));
            Assert.That(options.ModelPricing["gpt-4o-mini"].OutputPerMillionTokensUsd, Is.EqualTo(0.60m));
        });
    }
}
