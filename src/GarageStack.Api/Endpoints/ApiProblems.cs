namespace GarageStack.Api.Endpoints;

/// <summary>
/// Why a request was refused: a stable <paramref name="Code"/> a client can translate, and an
/// English <paramref name="Message"/> for logs and for anyone calling the API directly.
/// </summary>
public sealed record ValidationError(string Code, string Message);

/// <summary>
/// Every refusal the API answers with is a ProblemDetails body carrying a <c>code</c> extension,
/// so the browser can show its own translated text rather than the English detail.
/// </summary>
internal static class ApiProblems
{
    public static IResult BadRequest(ValidationError error) =>
        Problem(StatusCodes.Status400BadRequest, error.Code, error.Message);

    public static IResult BadRequest(string code, string message) =>
        Problem(StatusCodes.Status400BadRequest, code, message);

    public static IResult Problem(int statusCode, string code, string message) =>
        TypedResults.Problem(
            detail: message,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
