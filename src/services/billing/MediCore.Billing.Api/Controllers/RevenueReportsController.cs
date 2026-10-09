using FluentValidation;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Infrastructure.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Billing.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("reports/revenue")]
public sealed class RevenueReportsController : ControllerBase
{
    private readonly IValidator<RevenueReportFilter> _validator;
    private readonly IRevenueReportService _service;
    private readonly RevenueCsvExporter _csv;
    private readonly RevenuePdfExporter _pdf;

    public RevenueReportsController(IValidator<RevenueReportFilter> validator,
        IRevenueReportService service, RevenueCsvExporter csv, RevenuePdfExporter pdf)
    {
        _validator = validator;
        _service = service;
        _csv = csv;
        _pdf = pdf;
    }

    [HttpGet]
    [Produces("application/json", "text/csv", "application/pdf")]
    [ProducesResponseType<RevenueReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] RevenueReportFilter filter,
        [FromQuery] RevenueReportFormat format = RevenueReportFormat.Json,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(format))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["format"] = ["Format must be Json, Csv or Pdf."]
            }));
        var validation = await _validator.ValidateAsync(filter, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.Errors
                .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray())));

        var report = await _service.GenerateAsync(filter, cancellationToken);
        var name = $"medicore-revenue-{report.AppliedFilters.From:yyyyMMdd}-{report.AppliedFilters.To:yyyyMMdd}";
        return format switch
        {
            RevenueReportFormat.Json => Ok(report),
            RevenueReportFormat.Csv => File(_csv.Export(report), "text/csv; charset=utf-8", name + ".csv"),
            RevenueReportFormat.Pdf => File(_pdf.Export(report), "application/pdf", name + ".pdf"),
            _ => throw new InvalidOperationException("Unsupported report format.")
        };
    }
}
