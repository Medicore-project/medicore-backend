using FluentValidation;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Infrastructure.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Billing.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("reports/outstanding")]
public sealed class OutstandingReportsController : ControllerBase
{
    private readonly IValidator<OutstandingReportFilter> _validator;
    private readonly IOutstandingReportService _service;
    private readonly OutstandingCsvExporter _csv;
    private readonly OutstandingPdfExporter _pdf;

    public OutstandingReportsController(IValidator<OutstandingReportFilter> validator,
        IOutstandingReportService service, OutstandingCsvExporter csv, OutstandingPdfExporter pdf)
    {
        _validator = validator;
        _service = service;
        _csv = csv;
        _pdf = pdf;
    }

    [HttpGet]
    [Produces("application/json", "text/csv", "application/pdf")]
    [ProducesResponseType<OutstandingReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] OutstandingReportFilter filter,
        [FromQuery] OutstandingReportFormat format = OutstandingReportFormat.Json,
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
        var name = $"medicore-outstanding-{report.AppliedFilters.AsOf:yyyyMMdd}";
        return format switch
        {
            OutstandingReportFormat.Json => Ok(report),
            OutstandingReportFormat.Csv => File(_csv.Export(report), "text/csv; charset=utf-8", name + ".csv"),
            OutstandingReportFormat.Pdf => File(_pdf.Export(report), "application/pdf", name + ".pdf"),
            _ => throw new InvalidOperationException("Unsupported report format.")
        };
    }
}
