namespace Nornis.Application.Ai;

/// <summary>
/// The provider refused its own web search tool for the model a research turn ran on (feature 40,
/// Requirement 5): an untested pick, or a model that lost the capability. Never retried, and never
/// answered by quietly dropping the tool or switching model.
/// </summary>
public sealed class AiWebSearchUnsupportedException(Exception innerException)
    : AiHttpException("The model can't search the web.", 400, innerException);
