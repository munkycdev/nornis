namespace Nornis.Api.Contracts.Requests;

/// <summary>
/// Add or replace the signed-in user's key for one provider (feature 39). <c>Key</c> left out keeps
/// the stored key and checks it again. <c>BaseUrl</c> and <c>Models</c> are for an
/// OpenAI-compatible key only.
/// </summary>
public record SaveAiKeyRequest(
    string? Key,
    string? Label,
    string? BaseUrl,
    IReadOnlyList<AiCompatibleModelRequest>? Models);

/// <summary>One task's model on an OpenAI-compatible key; prices are USD per million tokens, 0 for free.</summary>
public record AiCompatibleModelRequest(
    string Task,
    string ModelName,
    bool SupportsImages,
    decimal InputPerMillionUsd,
    decimal OutputPerMillionUsd);

/// <summary>A task's model: a catalog id, <c>compatible:{id}</c>, or null for the default.</summary>
public record ChooseAiModelRequest(string? ModelId);
