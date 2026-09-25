using System.Text;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Api.Controllers;
using MediCore.Appointment.Api.Middleware;
using MediCore.Appointment.Application;
using MediCore.Appointment.Infrastructure;
using MediCore.Appointment.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "appointment")
        .WriteTo.Console()
        .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341");
});

var connectionString = builder.Configuration.GetConnectionString("AppointmentDatabase")
    ?? throw new InvalidOperationException("Connection string 'AppointmentDatabase' is missing.");

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "MediCore Appointment API", Version = "v1" });
});

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key configuration is missing.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "medicore-identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "medicore-clients";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAppointmentAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("appointment-default", limiter =>
    {
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    // The anonymous booking reads carry this in addition to the policy above, so they are limited
    // by both. See PublicRateLimitPolicies for why it is not partitioned by IP.
    options.AddFixedWindowLimiter(PublicRateLimitPolicies.PublicBooking, limiter =>
    {
        limiter.PermitLimit = PublicRateLimitPolicies.PermitLimit;
        limiter.Window = PublicRateLimitPolicies.Window;
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connectionString, name: "postgresql", tags: ["ready"]);

var app = builder.Build();

// ---------------------------------------------------------------------------
// Auto-run EF Core migrations on startup
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying EF Core migrations...");
        db.Database.Migrate();
        logger.LogInformation("EF Core migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply EF Core migrations. The app will still start.");
    }
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
// Inside the request logging, so the log records the 409 this produces rather than a 500.
app.UseMiddleware<ConcurrencyConflictMiddleware>();
app.UseHttpMetrics();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers().RequireRateLimiting("appointment-default");
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => true });
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapMetrics();

app.Run();

public partial class Program;
