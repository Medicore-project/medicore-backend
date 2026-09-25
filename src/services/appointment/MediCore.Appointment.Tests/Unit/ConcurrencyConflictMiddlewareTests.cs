using System.Text.Json;
using MediCore.Appointment.Api.Middleware;
using MediCore.Appointment.Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Appointment.Tests.Unit;

public sealed class ConcurrencyConflictMiddlewareTests
{
    [Fact]
    public async Task A_conflict_that_escaped_every_retry_is_answered_with_409_problem_details()
    {
        var context = NewContext();

        await Middleware(_ => throw new ConcurrentUpdateException()).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        var body = await ReadBodyAsync(context);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal(new ConcurrentUpdateException().Message, body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Two_reconciliations_inserting_the_same_slot_is_a_409_too()
    {
        // Before SCRUM-35 this surfaced as a 500, although its own message asks the caller to retry.
        var context = NewContext();

        await Middleware(_ => throw new DuplicateSlotException()).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Every_other_exception_passes_through_untouched()
    {
        var context = NewContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Middleware(_ => throw new InvalidOperationException()).InvokeAsync(context));
    }

    [Fact]
    public async Task A_request_that_succeeds_is_not_touched()
    {
        var context = NewContext();

        await Middleware(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        }).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
    }

    private static ConcurrencyConflictMiddleware Middleware(RequestDelegate next) =>
        new(next, NullLogger<ConcurrencyConflictMiddleware>.Instance);

    private static DefaultHttpContext NewContext() => new()
    {
        Response = { Body = new MemoryStream() }
    };

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }
}
