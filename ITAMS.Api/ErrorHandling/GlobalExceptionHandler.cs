using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace ITAMS.Api.ErrorHandling;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = CreateProblemDetails(httpContext, exception);

        if (problemDetails.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "Handled exception while processing {Method} {Path}.",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });

        if (written)
        {
            return true;
        }

        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private ProblemDetails CreateProblemDetails(HttpContext httpContext, Exception exception)
    {
        if (TryCreateMongoProblemDetails(httpContext, exception, out var mongoProblemDetails))
        {
            return mongoProblemDetails;
        }

        return CreateProblemDetails(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "Unexpected server error",
            "An unexpected error occurred while processing the request.");
    }

    private bool TryCreateMongoProblemDetails(
        HttpContext httpContext,
        Exception exception,
        out ProblemDetails problemDetails)
    {
        if (IsMongoValidationFailure(exception))
        {
            problemDetails = CreateProblemDetails(
                httpContext,
                StatusCodes.Status400BadRequest,
                "MongoDB validation failed",
                BuildMongoValidationDetail(exception));
            return true;
        }

        if (exception is MongoWriteException writeException && IsDuplicateKey(writeException))
        {
            problemDetails = CreateProblemDetails(
                httpContext,
                StatusCodes.Status409Conflict,
                "Duplicate key conflict",
                "The request violates a unique index in MongoDB.");
            return true;
        }

        problemDetails = null!;
        return false;
    }

    private ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail)
    {
        return new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        }.WithTraceId(Activity.Current?.Id ?? httpContext.TraceIdentifier);
    }

    private string BuildMongoValidationDetail(Exception exception)
    {
        const string genericMessage = "The request payload violates MongoDB collection validation rules.";

        if (!environment.IsDevelopment())
        {
            return genericMessage;
        }

        var rawMessage = ExtractMongoValidationMessage(exception);
        return string.IsNullOrWhiteSpace(rawMessage)
            ? genericMessage
            : $"{genericMessage} MongoDB reported: {rawMessage}";
    }

    private static string? ExtractMongoValidationMessage(Exception exception) =>
        exception switch
        {
            MongoWriteException writeException when !string.IsNullOrWhiteSpace(writeException.WriteError?.Message) =>
                writeException.WriteError!.Message,
            MongoCommandException commandException when !string.IsNullOrWhiteSpace(commandException.Message) =>
                commandException.Message,
            _ when exception.InnerException is not null => ExtractMongoValidationMessage(exception.InnerException),
            _ => null
        };

    private static bool IsMongoValidationFailure(Exception exception) =>
        exception switch
        {
            MongoWriteException writeException =>
                writeException.WriteError?.Code == 121 ||
                ContainsValidationFailureText(writeException.WriteError?.Message) ||
                (writeException.InnerException is not null && IsMongoValidationFailure(writeException.InnerException)),
            MongoCommandException commandException =>
                commandException.Code == 121 ||
                ContainsValidationFailureText(commandException.Message),
            _ => false
        };

    private static bool IsDuplicateKey(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey ||
        exception.WriteError?.Code is 11000 or 11001;

    private static bool ContainsValidationFailureText(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        (message.Contains("document failed validation", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("document validation failure", StringComparison.OrdinalIgnoreCase));
}

internal static class ProblemDetailsExtensions
{
    public static ProblemDetails WithTraceId(this ProblemDetails problemDetails, string traceId)
    {
        problemDetails.Extensions["traceId"] = traceId;
        return problemDetails;
    }
}
