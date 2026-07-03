namespace Nornis.Application.Ai;

public static class ErrorCategories
{
    public const string TransientError = "TransientError";
    public const string SourceNotFound = "SourceNotFound";
    public const string EmptySourceBody = "EmptySourceBody";
    public const string ValidationFailure = "ValidationFailure";
    public const string AiCallFailure = "AiCallFailure";
    public const string ParseFailure = "ParseFailure";
    public const string Timeout = "Timeout";
}
