using System.Reflection;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Api.Controllers;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>SCRUM-38: <c>GET reports/utilisation</c> and its three formats.</summary>
public sealed class UtilisationReportsControllerTests
{
    [Fact]
    public async Task Json_is_the_default_and_returns_the_report()
    {
        var fixture = new Fixture();
        var filter = new UtilisationReportFilter();

        var result = await fixture.Controller.Get(filter);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(fixture.Report, ok.Value);
        Assert.Same(filter, fixture.Service.LastFilter);
        Assert.Equal(1, fixture.Service.CallCount);
        Assert.Equal(0, fixture.Csv.CallCount);
        Assert.Equal(0, fixture.Pdf.CallCount);
    }

    [Theory]
    [InlineData(UtilisationReportFormat.Csv, "text/csv; charset=utf-8", "medicore-utilisation-20260901-20260930.csv")]
    [InlineData(UtilisationReportFormat.Pdf, "application/pdf", "medicore-utilisation-20260901-20260930.pdf")]
    public async Task An_export_renders_the_same_report_named_after_its_period(
        UtilisationReportFormat format,
        string contentType,
        string fileName)
    {
        var fixture = new Fixture();

        var result = await fixture.Controller.Get(
            new UtilisationReportFilter { DoctorId = Guid.NewGuid(), DepartmentName = "Cardiology" },
            format);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(contentType, file.ContentType);
        Assert.Equal(fileName, file.FileDownloadName);
        Assert.Same(fixture.Report, format == UtilisationReportFormat.Csv ? fixture.Csv.LastReport : fixture.Pdf.LastReport);
        Assert.Equal(1, fixture.Service.CallCount);
    }

    [Fact]
    public async Task An_invalid_filter_is_400_with_camel_cased_keys_and_nothing_runs()
    {
        var fixture = new Fixture();

        var result = await fixture.Controller.Get(
            new UtilisationReportFilter { DoctorId = Guid.Empty, From = new DateOnly(2026, 9, 10), To = new DateOnly(2026, 9, 1) },
            UtilisationReportFormat.Pdf);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("doctorId", problem.Errors.Keys);
        Assert.Contains("to", problem.Errors.Keys);
        Assert.Equal("Utilisation report filter validation failed.", problem.Title);
        Assert.Equal(0, fixture.Service.CallCount);
        Assert.Equal(0, fixture.Pdf.CallCount);
    }

    [Fact]
    public async Task An_unknown_format_is_400_and_nothing_runs()
    {
        var fixture = new Fixture();

        var result = await fixture.Controller.Get(new UtilisationReportFilter(), (UtilisationReportFormat)99);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("format", problem.Errors.Keys);
        Assert.Equal(0, fixture.Service.CallCount);
    }

    [Fact]
    public void The_whole_report_requires_the_report_reader_policy()
    {
        // On the class, so every format — and any action added later — is Admin only.
        var attribute = Assert.Single(typeof(UtilisationReportsController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AppointmentAuthorizationPolicies.ReportReader, attribute.Policy);
        Assert.Empty(typeof(UtilisationReportsController).GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public void The_route_is_reports_utilisation()
    {
        var route = Assert.Single(typeof(UtilisationReportsController).GetCustomAttributes<RouteAttribute>());

        Assert.Equal("reports/utilisation", route.Template);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class Fixture
    {
        public Fixture()
        {
            Report = new UtilisationReportResponse(
                new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc),
                new AppliedUtilisationFilters(null, null, null, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), true),
                new UtilisationTotals(0, 0, 0, 0, 0, 0, null, 0, 0, null),
                []);
            Service = new StubService(Report);
            Controller = new UtilisationReportsController(new UtilisationReportFilterValidator(), Service, Csv, Pdf);
        }

        public UtilisationReportResponse Report { get; }

        public StubService Service { get; }

        public StubCsv Csv { get; } = new();

        public StubPdf Pdf { get; } = new();

        public UtilisationReportsController Controller { get; }
    }

    private sealed class StubService(UtilisationReportResponse report) : IUtilisationReportService
    {
        public int CallCount { get; private set; }

        public UtilisationReportFilter? LastFilter { get; private set; }

        public Task<UtilisationReportResponse> GenerateAsync(
            UtilisationReportFilter filter,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastFilter = filter;
            return Task.FromResult(report);
        }
    }

    private sealed class StubCsv : IUtilisationCsvExporter
    {
        public int CallCount { get; private set; }

        public UtilisationReportResponse? LastReport { get; private set; }

        public byte[] Export(UtilisationReportResponse report)
        {
            CallCount++;
            LastReport = report;
            return [1, 2, 3];
        }
    }

    private sealed class StubPdf : IUtilisationPdfExporter
    {
        public int CallCount { get; private set; }

        public UtilisationReportResponse? LastReport { get; private set; }

        public byte[] Export(UtilisationReportResponse report)
        {
            CallCount++;
            LastReport = report;
            return [4, 5, 6];
        }
    }
}
