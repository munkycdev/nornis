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
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateAsync(record, ct);
    }

    private decimal CalculateCost(AiUsage? usage)
    {
        if (usage is null)
        {
            return 0m;
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
