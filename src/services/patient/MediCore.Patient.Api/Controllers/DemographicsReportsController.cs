using FluentValidation;
using FluentValidation.Results;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Patient.Api.Controllers;

/// <summary>Provides filtered demographics and visit-history reports for administrators.</summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("reports/demographics")]
public sealed class DemographicsReportsController : ControllerBase
{
    private readonly IValidator<DemographicsReportFilter> _validator;
    private readonly IDemographicsReportService _service;
    private readonly IDemographicsCsvExporter _csvExporter;
    private readonly IDemographicsPdfExporter _pdfExporter;

    public DemographicsReportsController(
        IValidator<DemographicsReportFilter> validator,
        IDemographicsReportService service,
        IDemographicsCsvExporter csvExporter,
        IDemographicsPdfExporter pdfExporter)
    {
        _validator = validator;
        _service = service;
        _csvExporter = csvExporter;
        _pdfExporter = pdfExporter;
    }

    /// <summary>
    /// Returns the active-patient demographics and visit-history report as JSON, CSV or PDF.
    /// Every export uses the same validated filters and aggregated report data as the JSON response.
    /// </summary>
    /// <param name="filter">Optional age band, gender, district and inclusive visit-date filters.</param>
    /// <param name="format">Response format. Defaults to JSON.</param>
    [HttpGet]
    [Produces("application/json", "text/csv", "application/pdf")]
    [ProducesResponseType(typeof(DemographicsReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(
        [FromQuery] DemographicsReportFilter filter,
        [FromQuery] DemographicsReportFormat format = DemographicsReportFormat.Json,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(format))
        {
            return CreateValidationProblem(
            [
                new ValidationFailure(
                    nameof(format),
                    "Format must be one of: Json, Csv, Pdf.")
            ]);
        }

        var validation = await _validator.ValidateAsync(filter, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors);
        }

        var report = await _service.GenerateAsync(filter, cancellationToken);
        var dateStamp = report.GeneratedAtUtc.ToString("yyyyMMdd");

        return format switch
        {
            DemographicsReportFormat.Json => Ok(report),
            DemographicsReportFormat.Csv => File(
                _csvExporter.Export(report),
                "text/csv; charset=utf-8",
                $"medicore-demographics-{dateStamp}.csv"),
            DemographicsReportFormat.Pdf => File(
                _pdfExporter.Export(report),
                "application/pdf",
                $"medicore-demographics-{dateStamp}.pdf"),
            _ => throw new InvalidOperationException("Validated report format was not handled.")
        };
    }

    private IActionResult CreateValidationProblem(IEnumerable<ValidationFailure> failures)
    {
        var errors = failures
            .GroupBy(failure => ToCamelCase(failure.PropertyName))
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());

        return ValidationProblem(new ValidationProblemDetails(errors)
        {
            Title = "Demographics report filter validation failed.",
            Status = StatusCodes.Status400BadRequest
        });
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
