using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class AiUsageRecorder : IAiUsageRecorder
{
    private readonly IAiUsageRecordRepository _repository;
    private readonly ExtractionOptions _extractionOptions;
    private readonly LoremasterOptions _loremasterOptions;
    private readonly LibraryOptions _libraryOptions;

    public AiUsageRecorder(
        IAiUsageRecordRepository repository,
        IOptions<ExtractionOptions> extractionOptions,
        IOptions<LoremasterOptions> loremasterOptions,
        IOptions<LibraryOptions> libraryOptions)
    {
        _repository = repository;
        _extractionOptions = extractionOptions.Value;
        _loremasterOptions = loremasterOptions.Value;
        _libraryOptions = libraryOptions.Value;
    }

    public async Task RecordAsync(
        Guid? worldId,
        Guid? userId,
        AiOperationType operationType,
        AiUsage? usage,
        bool succeeded,
        string? errorCode,
        Guid? sourceId = null,
        Guid? reviewBatchId = null,
        string? fallbackModel = null,
        CancellationToken ct = default)
    {
        // A refusal is not a call (feature 39): nothing could pay, or the key was refused, so
        // nothing was charged and there is nothing to price. The Worker's pipelines don't record
        // one at all; this keeps the streaming paths, which always write their ledger row, the same.
        if (!succeeded && ErrorCategories.IsRefusal(errorCode))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // Provider health is no longer read from here: a failed call reaches this without its
        // usage, so without its provider. AiCallBinder.ReportAsync records each outcome instead.

        var record = new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            OperationType = operationType,
            Model = usage?.Model ?? fallbackModel ?? string.Empty,
            InputTokens = usage?.InputTokens ?? 0,
            CachedInputTokens = usage?.CachedInputTokens,
            OutputTokens = usage?.OutputTokens ?? 0,
            TotalTokens = usage?.TotalTokens ?? 0,
            EstimatedCostUsd = CalculateCost(usage),
            SourceId = sourceId,
            ReviewBatchId = reviewBatchId,
            DurationMs = usage?.DurationMs ?? 0,
            Succeeded = succeeded,
            ErrorCode = errorCode,
            CreatedAt = now,
            Provider = usage?.Funding?.Provider,
            ModelId = usage?.Funding?.ModelId,
            FundedBy = usage?.Funding?.FundedBy,
            AiKeyId = usage?.Funding?.AiKeyId,
            KeyHolderUserId = usage?.Funding?.KeyHolderUserId,
        };

        await _repository.CreateAsync(record, ct);
    }

    private decimal CalculateCost(AiUsage? usage)
    {
        if (usage is null)
        {
            return 0m;
        }

        // A key-funded call carries its own price, from the catalog or the user's own line
        // (feature 39). Nornis's own deployments are priced from configuration, as before.
        if (usage.Funding?.Price is { } price)
        {
            return price.Cost(usage.InputTokens, usage.CachedInputTokens, usage.OutputTokens, usage.WebSearches);
        }

        var pricing = LookupPricing(usage.Model);
        if (pricing is null)
        {
            return 0m;
        }

        var inputCost = usage.InputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m;
        var outputCost = usage.OutputTokens * pricing.OutputPerMillionTokensUsd / 1_000_000m;

        return inputCost + outputCost;
    }

    // The lookup spans every configuration section that declares model prices — first
    // section naming the model wins; the sections agree on shared models. An unknown
    // model records zero cost rather than failing the call that produced it.
    private ModelPricing? LookupPricing(string model) =>
        _extractionOptions.ModelPricing.TryGetValue(model, out var pricing) ? pricing
        : _loremasterOptions.ModelPricing.TryGetValue(model, out pricing) ? pricing
        : _libraryOptions.ModelPricing.TryGetValue(model, out pricing) ? pricing
        : null;
}
