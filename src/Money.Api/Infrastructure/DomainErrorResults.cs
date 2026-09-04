using Microsoft.AspNetCore.Http;
using Money.Domain.Primitives;

namespace Money.Api.Infrastructure;

/// <summary>Maps domain errors onto RFC 9457 Problem Details. One place, one table.</summary>
public static class DomainErrorResults
{
    private const string TypeBase = "https://moneyapp.local/problems/";

    public static int StatusCodeFor(string code)
    {
        if (code.EndsWith(".not_found", StringComparison.Ordinal)) return StatusCodes.Status404NotFound;

        if (code.EndsWith(".already_voided", StringComparison.Ordinal)
            || code.EndsWith(".already_initialised", StringComparison.Ordinal)
            || code.EndsWith(".already_archived", StringComparison.Ordinal)
            || code.Contains(".duplicate_", StringComparison.Ordinal))
        {
            return StatusCodes.Status409Conflict;
        }

        return StatusCodes.Status400BadRequest;
    }

    public static IResult ToProblem(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusCodeFor(error.Code);

        return Results.Problem(
            detail: error.Message,
            statusCode: status,
            title: TitleFor(status),
            type: TypeBase + error.Code,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    public static IResult ToProblem<T>(Result<T> result) => ToProblem(result.Error!);

    public static IResult ToProblem(Result result) => ToProblem(result.Error!);

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        _ => "Request could not be completed"
    };
}
