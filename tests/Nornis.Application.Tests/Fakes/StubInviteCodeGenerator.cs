using Nornis.Application.Services;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Deterministic invite-code generator for tests — yields code-1, code-2, … so assertions can
/// predict the code without depending on randomness.
/// </summary>
public class StubInviteCodeGenerator : IInviteCodeGenerator
{
    private int _counter;

    public string Generate() => $"code-{++_counter}";
}
