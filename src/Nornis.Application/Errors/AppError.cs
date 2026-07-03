namespace Nornis.Application.Errors;

public record AppError(int StatusCode, string Code, string Message);
