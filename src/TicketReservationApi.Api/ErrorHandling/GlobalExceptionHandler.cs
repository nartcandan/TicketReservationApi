using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TicketReservationApi.Application.Common.Exceptions;

namespace TicketReservationApi.Api.ErrorHandling;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    // SQL Server: duplicate key in unique index / unique constraint
    private static readonly int[] UniqueViolationErrorNumbers = [2601, 2627];

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException e => new ValidationProblemDetails(
                e.Errors
                    .GroupBy(f => f.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            },
            NotFoundException e => Problem(StatusCodes.Status404NotFound, "Resource not found.", e.Message),
            ConflictException e => Problem(StatusCodes.Status409Conflict, "Conflict.", e.Message),
            BusinessRuleException e => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violation.", e.Message, e.Rule),
            DbUpdateConcurrencyException => Problem(StatusCodes.Status409Conflict, "Conflict.",
                "The resource was modified by another request. Please retry."),
            DbUpdateException { InnerException: SqlException sql } when UniqueViolationErrorNumbers.Contains(sql.Number) =>
                Problem(StatusCodes.Status409Conflict, "Conflict.", "A conflicting record already exists."),
            _ => null
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception");
            problem = Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails Problem(int status, string title, string? detail = null, string? rule = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };

        if (rule is not null)
        {
            problem.Extensions["rule"] = rule;
        }

        return problem;
    }
}
