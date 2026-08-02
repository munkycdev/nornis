using System.Text.Json;
using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Domain.Enums;

namespace Nornis.Application.Validation;

/// <summary>
/// Validates ProposedValueJson against ChangeType-specific schemas.
/// Stateless and singleton-compatible.
/// </summary>
public sealed class ProposalValidator : IProposalValidator
{
    /// <summary>
    /// The one cap. Public because extraction must build payloads against the same number
    /// the accept path enforces — when they disagreed (50,000 there, 32,768 here) anything
    /// in between was persisted Pending and then failed payload_too_large on every accept,
    /// forever.
    /// </summary>
    public const int MaxJsonLength = 32_768;

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
            ReviewChangeType.AddPlacemark => ValidateAddPlacemark(json),
            _ => AppResult.Fail(new AppError(400, "unknown_change_type",
                $"Unknown ChangeType '{changeType}'."))
        };
    }

    /// <summary>
    /// The deserialize-and-null-check prologue every arm used to open with, once. Parses
    /// with <see cref="ProposalJson.Options"/> — the same options the applicator reads
    /// with, so this gate can never pass a payload the apply cannot parse.
    /// </summary>
    private static bool TryDeserialize<T>(string json, string label, out T payload, out AppResult failure)
        where T : class
    {
        T? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<T>(json, ProposalJson.Options);
        }
        catch (JsonException ex)
        {
            payload = null!;
            failure = AppResult.Fail(new AppError(400, "invalid_payload",
                $"Failed to deserialize {label} payload: {ex.Message}"));
            return false;
        }

        if (parsed is null)
        {
            payload = null!;
            failure = AppResult.Fail(new AppError(400, "invalid_payload",
                $"{label} payload deserialized to null."));
            return false;
        }

        payload = parsed;
        failure = AppResult.Success();
        return true;
    }

    /// <summary>
    /// One policy for enum-carrying payload strings: null means "not provided"; anything
    /// else must parse, case-insensitively, or the payload is rejected here at the gate.
    /// (The arms used to split three ways — reject, silently ignore, silently default.)
    /// </summary>
    private static AppResult? InvalidEnum<TEnum>(string? value, string label, string field)
        where TEnum : struct, Enum
    {
        if (value is null || Enum.TryParse<TEnum>(value, ignoreCase: true, out _))
        {
            return null;
        }

        return AppResult.Fail(new AppError(400, "invalid_payload",
            $"{label}: {field} '{value}' is not a valid {typeof(TEnum).Name}."));
    }

    private static AppResult? InvalidName(string? name, string label)
    {
        if (name is null || (!string.IsNullOrWhiteSpace(name) && name.Length <= 200))
        {
            return null;
        }

        return AppResult.Fail(new AppError(400, "invalid_payload",
            $"{label}: Name must be between 1 and 200 characters."));
    }

    private static AppResult ValidateCreateArtifact(string json)
    {
        if (!TryDeserialize<CreateArtifactPayload>(json, "CreateArtifact", out var payload, out var failure))
            return failure;

        if (string.IsNullOrWhiteSpace(payload.Name))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "CreateArtifact: Name is required."));
        }

        if (InvalidName(payload.Name, "CreateArtifact") is { } badName)
            return badName;

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

        if (InvalidEnum<VisibilityScope>(payload.Visibility, "CreateArtifact", "Visibility") is { } badVisibility)
            return badVisibility;

        if (payload.MapPlacemark is { } pin)
        {
            if (pin.AttachmentId == Guid.Empty)
            {
                return AppResult.Fail(new AppError(400, "invalid_payload",
                    "CreateArtifact: MapPlacemark.AttachmentId is required."));
            }

            if (pin.X is < 0m or > 1m || pin.Y is < 0m or > 1m)
            {
                return AppResult.Fail(new AppError(400, "invalid_payload",
                    "CreateArtifact: MapPlacemark coordinates must be within 0..1."));
            }
        }

        return AppResult.Success();
    }

    private static AppResult ValidateAddPlacemark(string json)
    {
        if (!TryDeserialize<AddPlacemarkPayload>(json, "AddPlacemark", out var payload, out var failure))
            return failure;

        if (payload.AttachmentId == Guid.Empty)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddPlacemark: AttachmentId is required."));
        }

        if ((payload.ArtifactId is null || payload.ArtifactId == Guid.Empty)
            && string.IsNullOrWhiteSpace(payload.ArtifactName))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddPlacemark: ArtifactId or ArtifactName is required."));
        }

        if (payload.X is < 0m or > 1m || payload.Y is < 0m or > 1m)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddPlacemark: coordinates must be within 0..1."));
        }

        if (payload.Label is { Length: > 200 })
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "AddPlacemark: Label must not exceed 200 characters."));
        }

        return AppResult.Success();
    }

    private static AppResult ValidateUpdateArtifact(string json)
    {
        if (!TryDeserialize<UpdateArtifactPayload>(json, "UpdateArtifact", out var payload, out var failure))
            return failure;

        if (payload.Name is null && payload.Summary is null && payload.Visibility is null
            && payload.Confidence is null && payload.Status is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateArtifact: At least one field must be non-null."));
        }

        if (InvalidName(payload.Name, "UpdateArtifact") is { } badName)
            return badName;

        if (InvalidEnum<VisibilityScope>(payload.Visibility, "UpdateArtifact", "Visibility") is { } badVisibility)
            return badVisibility;

        if (InvalidEnum<ArtifactStatus>(payload.Status, "UpdateArtifact", "Status") is { } badStatus)
            return badStatus;

        return AppResult.Success();
    }

    private static AppResult ValidateMergeArtifact(string json)
    {
        if (!TryDeserialize<MergeArtifactPayload>(json, "MergeArtifact", out var payload, out var failure))
            return failure;

        if (payload.SourceArtifactId == Guid.Empty)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "MergeArtifact: SourceArtifactId is required and must be a non-empty GUID."));
        }

        if (InvalidName(payload.Name, "MergeArtifact") is { } badName)
            return badName;

        if (InvalidEnum<VisibilityScope>(payload.Visibility, "MergeArtifact", "Visibility") is { } badVisibility)
            return badVisibility;

        return AppResult.Success();
    }

    private static AppResult ValidateAddFact(string json)
    {
        if (!TryDeserialize<AddFactPayload>(json, "AddFact", out var payload, out var failure))
            return failure;

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

        if (InvalidEnum<TruthState>(payload.TruthState, "AddFact", "TruthState") is { } badTruthState)
            return badTruthState;

        if (InvalidEnum<VisibilityScope>(payload.Visibility, "AddFact", "Visibility") is { } badVisibility)
            return badVisibility;

        return AppResult.Success();
    }

    private static AppResult ValidateUpdateFact(string json)
    {
        if (!TryDeserialize<UpdateFactPayload>(json, "UpdateFact", out var payload, out var failure))
            return failure;

        if (payload.Value is null && payload.Confidence is null
            && payload.TruthState is null && payload.Visibility is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateFact: At least one field must be non-null."));
        }

        if (InvalidEnum<TruthState>(payload.TruthState, "UpdateFact", "TruthState") is { } badTruthState)
            return badTruthState;

        if (InvalidEnum<VisibilityScope>(payload.Visibility, "UpdateFact", "Visibility") is { } badVisibility)
            return badVisibility;

        return AppResult.Success();
    }

    private static AppResult ValidateAddRelationship(string json)
    {
        if (!TryDeserialize<AddRelationshipPayload>(json, "AddRelationship", out var payload, out var failure))
            return failure;

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

        if (InvalidEnum<TruthState>(payload.TruthState, "AddRelationship", "TruthState") is { } badTruthState)
            return badTruthState;

        if (InvalidEnum<VisibilityScope>(payload.Visibility, "AddRelationship", "Visibility") is { } badVisibility)
            return badVisibility;

        return AppResult.Success();
    }

    private static AppResult ValidateUpdateRelationship(string json)
    {
        if (!TryDeserialize<UpdateRelationshipPayload>(json, "UpdateRelationship", out var payload, out var failure))
            return failure;

        if (payload.Type is null && payload.Description is null
            && payload.Confidence is null && payload.TruthState is null && payload.Visibility is null)
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateRelationship: At least one field must be non-null."));
        }

        if (payload.Type is not null && (string.IsNullOrWhiteSpace(payload.Type) || payload.Type.Length > 200))
        {
            return AppResult.Fail(new AppError(400, "invalid_payload",
                "UpdateRelationship: Type must be between 1 and 200 characters."));
        }

        if (InvalidEnum<TruthState>(payload.TruthState, "UpdateRelationship", "TruthState") is { } badTruthState)
            return badTruthState;

        if (InvalidEnum<VisibilityScope>(payload.Visibility, "UpdateRelationship", "Visibility") is { } badVisibility)
            return badVisibility;

        return AppResult.Success();
    }
}
