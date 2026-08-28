using MediCore.Identity.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using Serilog;

using MediCore.Identity.Application;
using MediCore.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

var connectionString = builder.Configuration.GetConnectionString("IdentityDatabase")
    ?? throw new InvalidOperationException(
        "Connection string 'IdentityDatabase' is missing.");

builder.Services.AddControllers();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddNpgSql(
        connectionString,
        name: "postgresql",
        tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapMetrics();

app.Run();