using MediCore.Gateway;
using MediCore.Gateway.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using Serilog;
using System.Threading.RateLimiting;

// =============================================================================
// MediCore API Gateway — Program.cs
// Entry point for the YARP reverse proxy gateway.
// Responsibilities:
//   - Route all client traffic to the correct downstream service
//   - Enforce rate limiting (100 req/min global, 5 req/min on /auth/login)
//   - Apply CORS allowlist
//   - Inject security headers on every response
//   - Expose an aggregated health endpoint for all services
//   - Expose /metrics for Prometheus scraping
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Serilog — structured logging, writes to Console + Seq
// ---------------------------------------------------------------------------
builder.Host.UseSerilog((context, services, cfg) =>
{
    cfg.ReadFrom.Configuration(context.Configuration)
       .ReadFrom.Services(services)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Service", "gateway")
       .WriteTo.Console()
       .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341");
});

// ---------------------------------------------------------------------------
// 2. YARP Reverse Proxy — configuration driven from appsettings
// ---------------------------------------------------------------------------
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ---------------------------------------------------------------------------
// 3. Rate Limiting (ASP.NET Core 8 built-in)
//    Policy "global"     : 100 requests per 60 s per IP  — applied to all routes
//    Policy "auth-login" :   5 requests per 60 s per IP  — applied only to /auth/login
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Adds a Retry-After header so clients know when to retry
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please try again later.", cancellationToken);
    };

    // Global policy — 100 requests per minute per IP
    options.AddPolicy("global", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Auth-login policy — 5 requests per minute per IP (stricter)
    options.AddPolicy("auth-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// ---------------------------------------------------------------------------
// 4. CORS — only the configured allow-list origins are accepted
// ---------------------------------------------------------------------------
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("MediCoreCors", policy =>
    {
        policy
            .WithOrigins(corsOrigins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Content-Type", "Authorization", "X-Correlation-Id")
            .AllowCredentials();
    });
});

// ---------------------------------------------------------------------------
// 5. Aggregated Health Checks — HTTP-probes each upstream service
//    Stubbed services use tags=["degraded"] so they don't fail the check.
// ---------------------------------------------------------------------------
var identityBase   = builder.Configuration["Services:Identity"]   ?? "http://localhost:5001";
var patientBase    = builder.Configuration["Services:Patient"]    ?? "http://localhost:5002";
var appointBase    = builder.Configuration["Services:Appointment"] ?? "http://localhost:5003";
var billingBase    = builder.Configuration["Services:Billing"]    ?? "http://localhost:5004";

builder.Services
    .AddHealthChecks()
    // Gateway liveness (no downstream dependency)
    .AddCheck("gateway-self", () => HealthCheckResult.Healthy("Gateway is alive"), tags: ["live"])
    // Identity — real service; unhealthy counts as failure
    .AddUrlGroup(
        new Uri($"{identityBase}/health/live"),
        name: "identity",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["services", "ready"])
    // Patient — stubbed; mark Degraded if unreachable so gateway stays green
    .AddUrlGroup(
        new Uri($"{patientBase}/health/live"),
        name: "patient",
        failureStatus: HealthStatus.Degraded,
        tags: ["services"])
    // Appointment — stubbed
    .AddUrlGroup(
        new Uri($"{appointBase}/health/live"),
        name: "appointment",
        failureStatus: HealthStatus.Degraded,
        tags: ["services"])
    // Billing — stubbed
    .AddUrlGroup(
        new Uri($"{billingBase}/health/live"),
        name: "billing",
        failureStatus: HealthStatus.Degraded,
        tags: ["services"]);

// ---------------------------------------------------------------------------
// Build
// ---------------------------------------------------------------------------
var app = builder.Build();

// ---------------------------------------------------------------------------
// 6. Middleware pipeline (order matters!)
// ---------------------------------------------------------------------------

// HTTPS redirect — in production HSTS header + redirect; skipped in dev
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

// Security headers — injected on every response before any output
app.UseMiddleware<SecurityHeadersMiddleware>();

// Serilog request logging (after security headers so headers appear in logs)
app.UseSerilogRequestLogging();

// Prometheus metrics collection
app.UseHttpMetrics();

// CORS — must come before routing
app.UseCors("MediCoreCors");

// Rate limiting — after CORS so preflight OPTIONS requests are not rate-limited
app.UseRateLimiter();

// ---------------------------------------------------------------------------
// 7. Endpoints
// ---------------------------------------------------------------------------

// Gateway liveness — lightweight, no upstream calls
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]  = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
}).AllowAnonymous();

// Aggregated services health — probes all upstream /health/live endpoints
app.MapHealthChecks("/health/services", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("services"),
    ResponseWriter = HealthReportWriter.WriteJsonReport,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Degraded]  = StatusCodes.Status200OK,   // stubbed services don't break this
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
}).AllowAnonymous();

// Prometheus scrape endpoint
app.MapMetrics("/metrics");

// YARP routing
// Rate limiting is applied automatically by YARP for each route that has a
// "RateLimiterPolicy" property set in appsettings.json, provided that
// app.UseRateLimiter() is already in the pipeline above (which it is).
app.MapReverseProxy();

app.Run();
