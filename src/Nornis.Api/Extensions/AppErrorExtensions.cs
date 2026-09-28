using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Application.Errors;

namespace Nornis.Api.Extensions;

public static class AppErrorExtensions
{
    private const string SanitizedCode = "internal_error";
    private const string SanitizedMessage = "Something went wrong. Please try again.";

    /// <summary>
    /// The one mapping from an <see cref="AppError"/> to an HTTP response.
    /// 4xx and 503 bodies are user-facing by contract and pass through; any other
    /// server-side status may carry exception text in its message, so the body is
    /// replaced wholesale (the security property in
    /// ErrorResponsesNeverExposeInternalsTests). The status itself always passes
    /// through — a 502 tells the client to retry later, and sanitizing must not
    /// erase that.
    /// </summary>
    public static IActionResult ToActionResult(this AppError error) =>
        new ObjectResult(error.ToErrorResponse()) { StatusCode = error.StatusCode };

    /// <summary>
    /// The body alone, sanitized by the same rule — for an error that arrives after the response
    /// has started, such as partway through a streamed answer, where no status can be set.
    /// </summary>
    public static ErrorResponse ToErrorResponse(this AppError error)
    {
        var passThrough = error.StatusCode < 500 || error.StatusCode == 503;
        return passThrough
            ? new ErrorResponse(error.Code, error.Message)
            : new ErrorResponse(SanitizedCode, SanitizedMessage);
    }
}
