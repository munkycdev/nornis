namespace Nornis.Application.Errors;

public class AppResult<T>
{
    public T? Value { get; }
    public AppError? Error { get; }
    public bool IsSuccess => Error is null;

    private AppResult(T value) { Value = value; }
    private AppResult(AppError error) { Error = error; }

    public static AppResult<T> Success(T value) => new(value);
    public static AppResult<T> Fail(AppError error) => new(error);
}

public class AppResult
{
    public AppError? Error { get; }
    public bool IsSuccess => Error is null;

    private AppResult() { }
    private AppResult(AppError error) { Error = error; }

    public static AppResult Success() => new();
    public static AppResult Fail(AppError error) => new(error);
}
