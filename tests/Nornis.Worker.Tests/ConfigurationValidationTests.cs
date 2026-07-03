using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Ai;
using Nornis.Infrastructure.Messaging;
using Nornis.Infrastructure.Persistence;
using Nornis.Infrastructure.Persistence.Repositories;
using Nornis.Worker;
using Nornis.Worker.Configuration;
using NUnit.Framework;
using OpenAI.Chat;

namespace Nornis.Worker.Tests;

[TestFixture]
public class ConfigurationValidationTests
{
    private static IHostBuilder CreateHostBuilderWithConfig(Dictionary<string, string?> configValues)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(configValues);
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;

                services.Configure<ExtractionOptions>(configuration.GetSection("Extraction"));
                services.Configure<WorkerOptions>(configuration.GetSection("ServiceBus"));

                var extractionOptions = configuration.GetSection("Extraction").Get<ExtractionOptions>();
                var workerOptions = configuration.GetSection("ServiceBus").Get<WorkerOptions>();

                if (string.IsNullOrWhiteSpace(extractionOptions?.AiModel))
                    throw new InvalidOperationException(
                        "Required configuration 'Extraction:AiModel' is missing. The worker cannot start without an AI model configured.");

                if (string.IsNullOrWhiteSpace(extractionOptions?.AiEndpoint))
                    throw new InvalidOperationException(
                        "Required configuration 'Extraction:AiEndpoint' is missing. The worker cannot start without an AI endpoint configured.");

                if (string.IsNullOrWhiteSpace(workerOptions?.ConnectionString))
                    throw new InvalidOperationException(
                        "Required configuration 'ServiceBus:ConnectionString' is missing. The worker cannot start without a Service Bus connection string configured.");
            });
    }

    [Test]
    public void MissingAiModel_FailsFastWithClearError()
    {
        var config = new Dictionary<string, string?>
        {
            // AiModel intentionally missing
            ["Extraction:AiEndpoint"] = "https://test.openai.azure.com/",
            ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey="
        };

        var ex = Assert.Throws<InvalidOperationException>(() => CreateHostBuilderWithConfig(config).Build());

        Assert.That(ex!.Message, Does.Contain("Extraction:AiModel"));
        Assert.That(ex.Message, Does.Contain("missing"));
    }

    [Test]
    public void MissingAiEndpoint_FailsFastWithClearError()
    {
        var config = new Dictionary<string, string?>
        {
            ["Extraction:AiModel"] = "gpt-4o",
            // AiEndpoint intentionally missing
            ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey="
        };

        var ex = Assert.Throws<InvalidOperationException>(() => CreateHostBuilderWithConfig(config).Build());

        Assert.That(ex!.Message, Does.Contain("Extraction:AiEndpoint"));
        Assert.That(ex.Message, Does.Contain("missing"));
    }

    [Test]
    public void MissingServiceBusConnectionString_FailsFastWithClearError()
    {
        var config = new Dictionary<string, string?>
        {
            ["Extraction:AiModel"] = "gpt-4o",
            ["Extraction:AiEndpoint"] = "https://test.openai.azure.com/",
            // ConnectionString intentionally missing
        };

        var ex = Assert.Throws<InvalidOperationException>(() => CreateHostBuilderWithConfig(config).Build());

        Assert.That(ex!.Message, Does.Contain("ServiceBus:ConnectionString"));
        Assert.That(ex.Message, Does.Contain("missing"));
    }
}
