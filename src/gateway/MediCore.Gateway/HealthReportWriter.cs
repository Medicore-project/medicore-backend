using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediCore.Gateway;

/// <summary>
/// Writes a structured JSON health report for the /health/services endpoint.
/// Format is compatible with the standard ASP.NET Core health check response
/// that most frontend dashboards (Grafana, etc.) expect.
/// </summary>
public static class HealthReportWriter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task WriteJsonReport(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new
        {
            status = report.Status.ToString(),
            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
            services = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status      = entry.Value.Status.ToString(),
                    durationMs  = (int)entry.Value.Duration.TotalMilliseconds,
                    description = entry.Value.Description,
                    error       = entry.Value.Exception?.Message,
                    tags        = entry.Value.Tags
                })
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response, _jsonOptions));
    }
}
