using System.Text.Json;
using Nornis.Application.Errors;
using Nornis.Domain.Enums;

namespace Nornis.Application.Validation;

/// <summary>
/// Validates ProposedValueJson against ChangeType-specific schemas.
/// Stateless and singleton-compatible.
/// </summary>
public sealed class ProposalValidator : IProposalValidator
{
    private const int MaxJsonLength = 32_768;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AppResult ValidateProposedValue(string json, ReviewChangeType changeType)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload", "ProposedValueJson must not be empty."));
        }

        if (json.Length > MaxJsonLength)
        {
            return AppResult.Fail(new AppError(400, "payload_too_large",
                $"ProposedValueJson exceeds the maximum allowed size of {MaxJsonLength} characters."));
        }

        return changeType switch
        {
            ReviewChangeType.CreateArtifact => ValidateCreateArtifact(json),
            ReviewChangeType.UpdateArtifact => ValidateUpdateArtifact(json),
            ReviewChangeType.MergeArtifact => ValidateMergeArtifact(json),
            ReviewChangeType.AddFact => ValidateAddFact(json),
            ReviewChangeType.UpdateFact => ValidateUpdateFact(json),
            ReviewChangeType.AddRelationship => ValidateAddRelationship(json),
            ReviewChangeType.UpdateRelationship => ValidateUpdateRelationship(json),
            _ => AppResult.Fail(new AppError(400, "unknown_change_type",
                $"Unknown ChangeType '{changeType}'."))
        };
    }

    private static AppResult ValidateCreateArtifact(string json)
    {
        CreateArtifactPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CreateArtifactPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                $"Failed to deserialize CreateArtifact payload: {ex.Message}"));
        }

        if (payload is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "CreateArtifact payload deserialized to null."));
        }

        if (string.IsNullOrWhiteSpace(payload.Name))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "CreateArtifact: Name is required."));
        }

        if (payload.Name.Length < 1 || payload.Name.Length > 200)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "CreateArtifact: Name must be between 1 and 200 characters."));
        }

        if (string.IsNullOrWhiteSpace(payload.Type))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "CreateArtifact: Type is required."));
        }

        if (!Enum.TryParse<ArtifactType>(payload.Type, ignoreCase: true, out _))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                $"CreateArtifact: Type '{payload.Type}' is not a valid ArtifactType."));
        }

        return AppResult.Success();
    }

    private static AppResult ValidateUpdateArtifact(string json)
    {
        UpdateArtifactPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UpdateArtifactPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                $"Failed to deserialize UpdateArtifact payload: {ex.Message}"));
        }

        if (payload is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateArtifact payload deserialized to null."));
        }

        if (payload.Name is null && payload.Summary is null && payload.Visibility is null
            && payload.Confidence is null && payload.Status is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateArtifact: At least one field must be non-null."));
        }

        return AppResult.Success();
    }

    private static AppResult ValidateMergeArtifact(string json)
    {
        MergeArtifactPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MergeArtifactPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                $"Failed to deserialize MergeArtifact payload: {ex.Message}"));
        }

        if (payload is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "MergeArtifact payload deserialized to null."));
        }

        if (payload.SourceArtifactId == Guid.Empty)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "MergeArtifact: SourceArtifactId is required and must be a non-empty GUID."));
        }

        return AppResult.Success();
    }

    private static AppResult ValidateAddFact(string json)
    {
        AddFactPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AddFactPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                $"Failed to deserialize AddFact payload: {ex.Message}"));
        }

        if (payload is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddFact payload deserialized to null."));
        }

        if (string.IsNullOrWhiteSpace(payload.Predicate))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddFact: Predicate is required."));
        }

        if (payload.Predicate.Length < 1 || payload.Predicate.Length > 500)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddFact: Predicate must be between 1 and 500 characters."));
        }

        if (string.IsNullOrWhiteSpace(payload.Value))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddFact: Value is required."));
        }

        if (payload.Value.Length < 1 || payload.Value.Length > 4000)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddFact: Value must be between 1 and 4000 characters."));
        }

        return AppResult.Success();
    }

    private static AppResult ValidateUpdateFact(string json)
    {
        UpdateFactPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UpdateFactPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                $"Failed to deserialize UpdateFact payload: {ex.Message}"));
        }

        if (payload is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateFact payload deserialized to null."));
        }

        if (payload.Value is null && payload.Confidence is null
            && payload.TruthState is null && payload.Visibility is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateFact: At least one field must be non-null."));
        }

        return AppResult.Success();
    }

    private static AppResult ValidateAddRelationship(string json)
    {
        AddRelationshipPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AddRelationshipPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                $"Failed to deserialize AddRelationship payload: {ex.Message}"));
        }

        if (payload is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddRelationship payload deserialized to null."));
        }

        // Each endpoint needs an id or a name (names cover artifacts created in the same batch,
        // resolved by the applicator at accept time).
        if ((payload.ArtifactAId is null || payload.ArtifactAId == Guid.Empty)
            && string.IsNullOrWhiteSpace(payload.ArtifactAName))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddRelationship: ArtifactAId or ArtifactAName is required."));
        }

        if ((payload.ArtifactBId is null || payload.ArtifactBId == Guid.Empty)
            && string.IsNullOrWhiteSpace(payload.ArtifactBName))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddRelationship: ArtifactBId or ArtifactBName is required."));
        }

        if (string.IsNullOrWhiteSpace(payload.Type))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddRelationship: Type is required."));
        }

        if (payload.Type.Length < 1 || payload.Type.Length > 200)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddRelationship: Type must be between 1 and 200 characters."));
        }

        return AppResult.Success();
    }

    private static AppResult ValidateUpdateRelationship(string json)
    {
        UpdateRelationshipPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UpdateRelationshipPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                $"Failed to deserialize UpdateRelationship payload: {ex.Message}"));
        }

        if (payload is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateRelationship payload deserialized to null."));
        }

        if (payload.Type is null && payload.Description is null
            && payload.Confidence is null && payload.TruthState is null && payload.Visibility is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateRelationship: At least one field must be non-null."));
        }

        return AppResult.Success();
    }
}
