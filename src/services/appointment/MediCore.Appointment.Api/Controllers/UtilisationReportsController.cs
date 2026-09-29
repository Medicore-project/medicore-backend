using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// The doctor utilisation report (SCRUM-38): per doctor, completed and no-show counts, a total,
/// the no-show rate and how full their slots were, as JSON, CSV or PDF.
/// </summary>
/// <remarks>
/// Through the gateway this is <c>/appointment/reports/utilisation</c>. Admin only — see
/// <see cref="AppointmentAuthorizationPolicies.ReportReader"/>. Every format is built from the same
/// validated filters and the same report, so an export always matches what the page showed.
/// </remarks>
[ApiController]
[Authorize(Policy = AppointmentAuthorizationPolicies.ReportReader)]
[Route("reports/utilisation")]
public sealed class UtilisationReportsController : AppointmentControllerBase
{
    private readonly IValidator<UtilisationReportFilter> _validator;
    private readonly IUtilisationReportService _service;
    private readonly IUtilisationCsvExporter _csvExporter;
    private readonly IUtilisationPdfExporter _pdfExporter;

    public UtilisationReportsController(
        IValidator<UtilisationReportFilter> validator,
        IUtilisationReportService service,
        IUtilisationCsvExporter csvExporter,
        IUtilisationPdfExporter pdfExporter)
    {
        _validator = validator;
        _service = service;
        _csvExporter = csvExporter;
        _pdfExporter = pdfExporter;
    }

    /// <summary>
    /// Returns the doctor utilisation report. With no filters, every doctor for the current month
    /// (Asia/Colombo).
    /// </summary>
    /// <remarks>
    /// Filters combine freely. <c>from</c> and <c>to</c> are inclusive Colombo dates; given only
    /// one, the period runs to the end of (or from the start of) that date's month. At most 366
    /// days. <c>departmentName</c> only labels the exports; it is never part of the query.
    /// <para>
    /// Total counts every appointment that was not cancelled. No-show rate is no-shows over
    /// completed plus no-shows, and slot fill is used slots over slots not blocked; each is a 0–1
    /// fraction, or null when there is nothing to divide by.
    /// </para>
    /// </remarks>
    /// <param name="filter">Optional doctor, department and date filters.</param>
    /// <param name="format">Response format. Defaults to JSON.</param>
    [HttpGet]
    [Produces("application/json", "text/csv", "application/pdf")]
    [ProducesResponseType(typeof(UtilisationReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(
        [FromQuery] UtilisationReportFilter filter,
        [FromQuery] UtilisationReportFormat format = UtilisationReportFormat.Json,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(format))
        {
            return CreateValidationProblem(
                [new ValidationFailure(nameof(format), "Format must be one of: Json, Csv, Pdf.")],
                "Utilisation report filter validation failed.");
        }

        var validation = await _validator.ValidateAsync(filter, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Utilisation report filter validation failed.");
        }

        var report = await _service.GenerateAsync(filter, cancellationToken);

        return format switch
        {
            UtilisationReportFormat.Json => Ok(report),
            UtilisationReportFormat.Csv => File(
                _csvExporter.Export(report),
                "text/csv; charset=utf-8",
                FileName(report, "csv")),
            UtilisationReportFormat.Pdf => File(
                _pdfExporter.Export(report),
                "application/pdf",
                FileName(report, "pdf")),
            _ => throw new InvalidOperationException("Validated report format was not handled.")
        };
    }

    /// <summary>Names the file after the period it covers, which is what tells two exports apart.</summary>
    private static string FileName(UtilisationReportResponse report, string extension) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"medicore-utilisation-{report.AppliedFilters.From:yyyyMMdd}-{report.AppliedFilters.To:yyyyMMdd}.{extension}");
}
