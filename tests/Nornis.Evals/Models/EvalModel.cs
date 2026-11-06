using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;

namespace Nornis.Evals.Models;

/// <summary>
/// The model an eval run calls, named as <c>provider/model</c> the way the catalog names its
/// lines: <c>openai/gpt-5.4</c>, <c>anthropic/claude-sonnet-5-5</c>, <c>gemini/gemini-3.8-flash</c>,
/// or <c>azure/nornis-extract</c> for Nornis's own deployment. A user's-key provider takes its key
/// from <c>NORNIS_OPENAI_KEY</c>, <c>NORNIS_ANTHROPIC_KEY</c> or <c>NORNIS_GEMINI_KEY</c>; Azure
/// reads <c>Extraction:AiEndpoint</c> and <c>Extraction:AiApiKey</c> from the Worker's user
/// secrets or the environment, as the Worker does.
/// </summary>
public sealed record EvalModel(string Name, AiProvider Provider, string Model, AiModelPrice? Price)
{
    private static readonly Dictionary<string, (AiProvider Provider, string KeyVariable)> Providers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = (AiProvider.OpenAi, "NORNIS_OPENAI_KEY"),
        ["anthropic"] = (AiProvider.Anthropic, "NORNIS_ANTHROPIC_KEY"),
        ["gemini"] = (AiProvider.Gemini, "NORNIS_GEMINI_KEY"),
        ["azure"] = (AiProvider.AzureOpenAi, ""),
    };

    /// <summary>Reads <paramref name="name"/> and prices it from the API's own configuration: a
    /// catalog line for a user's-key model, <c>Extraction:ModelPricing</c> for an Azure deployment.
    /// An unpriced model still runs; its report says the cost is unknown.</summary>
    public static EvalModel Parse(string name, IConfiguration configuration)
    {
        var slash = name.IndexOf('/');
        if (slash <= 0 || slash == name.Length - 1 || !Providers.TryGetValue(name[..slash], out var provider))
        {
            throw new ArgumentException(
                $"'{name}' isn't provider/model, with the provider one of {string.Join(", ", Providers.Keys)}.");
        }

        var model = name[(slash + 1)..];
        AiModelPrice? price;
        if (provider.Provider == AiProvider.AzureOpenAi)
        {
            var pricing = configuration.GetSection($"{ExtractionOptions.SectionName}:ModelPricing:{model}").Get<ModelPricing>();
            price = pricing is null ? null : new AiModelPrice(pricing.InputPerMillionTokensUsd, null, pricing.OutputPerMillionTokensUsd);
        }
        else
        {
            var catalog = configuration.GetSection(AiModelsOptions.SectionName).Get<AiModelsOptions>() ?? new AiModelsOptions();
            price = catalog.Catalog.FirstOrDefault(c => c.Provider == provider.Provider && c.Model == model)?.Price;
        }

        return new EvalModel(name.ToLowerInvariant(), provider.Provider, model, price);
    }

    /// <summary>The client and traits for this model, built the way the API and Worker build
    /// them, so the eval measures what a GM's key would get.</summary>
    public AiBinding Bind(IConfiguration configuration, UserKeyChatClients userKeys)
    {
        var funding = new AiFunding(Provider, Model, Provider == AiProvider.AzureOpenAi ? AiFundedBy.Nornis : AiFundedBy.Key, Price: Price);
        if (Provider == AiProvider.AzureOpenAi)
        {
            var endpoint = configuration[$"{ExtractionOptions.SectionName}:AiEndpoint"];
            var key = configuration[$"{ExtractionOptions.SectionName}:AiApiKey"];
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "Extraction:AiEndpoint and Extraction:AiApiKey aren't set (the Worker's user secrets, or Extraction__AiEndpoint and Extraction__AiApiKey).");
            }

            var client = NornisChatClients.Create(AzureOpenAiClientFactory.Create(new Uri(endpoint), new ApiKeyCredential(key), maxRetries: 2));
            return new AiBinding(client.For(Model), OpenAiProviderTraits.Instance, funding);
        }

        var variable = Providers[Name[..Name.IndexOf('/')]].KeyVariable;
        var secret = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException($"Set {variable} to your {AiProviderNames.Display(Provider)} API key to run {Name}.");
        }

        var userKey = new UserAiKey { Id = Guid.NewGuid(), Provider = Provider, UpdatedAt = DateTimeOffset.UtcNow };
        return new AiBinding(userKeys.For(userKey, Model, () => secret), Traits(Provider), funding with { AiKeyId = userKey.Id });
    }

    private static IAiProviderTraits Traits(AiProvider provider) => provider switch
    {
        AiProvider.Anthropic => AnthropicProviderTraits.Instance,
        AiProvider.Gemini => GeminiProviderTraits.Instance,
        _ => OpenAiProviderTraits.Instance,
    };
}

/// <summary>Hands every call the one model the run is measuring: no funding rule, no trial,
/// no key lookup. Failures are the run's to count, so nothing is reported anywhere.</summary>
public sealed class EvalBinder(AiBinding binding) : IAiCallBinder
{
    public Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, bool needsImages, bool needsWebSearch, CancellationToken ct) =>
        Task.FromResult(binding);

    public Task ReportAsync(AiBinding binding, Exception? failure, CancellationToken ct) => Task.CompletedTask;
}
