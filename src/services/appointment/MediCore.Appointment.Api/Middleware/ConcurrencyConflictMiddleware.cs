using MediCore.Appointment.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Middleware;

/// <summary>
/// Answers 409 for a write that lost a concurrency race and could not be resolved by retrying.
/// </summary>
/// <remarks>
/// <para>
/// SCRUM-35. Services retry <see cref="ConcurrentUpdateException"/> themselves and normally turn it
/// into an ordinary result; this is the backstop for the rare request that loses three times in a
/// row. Reconciliation is reached from schedule, leave and holiday endpoints alike, and threading a
/// conflict result through every one of their unions would be a lot of surface for a case this
/// rare, so it is caught once here instead.
/// </para>
/// <para>
/// <see cref="DuplicateSlotException"/> is the same situation reached through a unique index — two
/// reconciliations of one doctor inserting the same slot — and its own message already asks the
/// caller to retry. Before SCRUM-35 it surfaced as a 500.
/// </para>
/// <para>
/// Deliberately narrow: every other exception passes through untouched, so error handling for the
/// rest of the service is exactly what it was.
/// </para>
/// </remarks>
public sealed class ConcurrencyConflictMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ConcurrencyConflictMiddleware> _logger;

    public ConcurrencyConflictMiddleware(RequestDelegate next, ILogger<ConcurrencyConflictMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (
            exception is ConcurrentUpdateException or DuplicateSlotException
            && !context.Response.HasStarted)
        {
            _logger.LogWarning(
                exception,
                "{Method} {Path} lost a concurrent update and was answered with 409",
                context.Request.Method,
                context.Request.Path);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Title = exception.Message,
                    Status = StatusCodes.Status409Conflict
                },
                options: null,
                contentType: "application/problem+json");
        }
    }
}
