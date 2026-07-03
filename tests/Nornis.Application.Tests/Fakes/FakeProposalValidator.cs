using Nornis.Application.Errors;
using Nornis.Application.Validation;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Fakes;

public class FakeProposalValidator : IProposalValidator
{
    private AppResult? _nextResult;

    public void ConfigureFailure(string code, string message)
    {
        _nextResult = AppResult.Fail(new AppError(400, code, message));
    }

    public void ConfigureSuccess()
    {
        _nextResult = null;
    }

    public AppResult ValidateProposedValue(string json, ReviewChangeType changeType)
    {
        return _nextResult ?? AppResult.Success();
    }
}
