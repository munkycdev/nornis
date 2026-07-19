namespace Nornis.Application.Services;

/// <summary>
/// Produces URL-safe, unguessable invite codes. Abstracted so tests can supply a
/// deterministic sequence.
/// </summary>
public interface IInviteCodeGenerator
{
    string Generate();
}
