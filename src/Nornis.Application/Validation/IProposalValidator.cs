using Nornis.Application.Errors;
using Nornis.Domain.Enums;

namespace Nornis.Application.Validation;

/// <summary>
/// Validates ProposedValueJson against ChangeType-specific schemas.
/// </summary>
public interface IProposalValidator
{
    AppResult ValidateProposedValue(string json, ReviewChangeType changeType);
}
