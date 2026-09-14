using System.Reflection;
using FluentValidation;
using MediCore.Patient.Api.Controllers;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Services;
using MediCore.Patient.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Patient.Tests.Unit;

public sealed class DemographicsReportsControllerTests
{
    [Fact]
    public async Task Json_is_default_and_returns_aggregated_report()
    {
        var fixture = new Fixture();
        var filter = new DemographicsReportFilter();

        var result = await fixture.Controller.Get(filter);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(fixture.Report, ok.Value);
        Assert.Same(filter, fixture.Service.LastFilter);
        Assert.Equal(1, fixture.Service.CallCount);
        Assert.Equal(0, fixture.Csv.CallCount);
        Assert.Equal(0, fixture.Pdf.CallCount);
    }

    [Theory]
    [InlineData(DemographicsReportFormat.Csv, "text/csv; charset=utf-8", ".csv")]
    [InlineData(DemographicsReportFormat.Pdf, "application/pdf", ".pdf")]
    public async Task Export_uses_the_same_filtered_report_and_expected_content_type(
        DemographicsReportFormat format,
        string expectedContentType,
        string expectedExtension)
    {
        var fixture = new Fixture();
        var filter = new DemographicsReportFilter(null, "Female", "Colombo", null, null);

        var result = await fixture.Controller.Get(filter, format);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(expectedContentType, file.ContentType);
        Assert.EndsWith(expectedExtension, file.FileDownloadName);
        Assert.Contains("20260914", file.FileDownloadName);
        Assert.Same(fixture.Report, format == DemographicsReportFormat.Csv
            ? fixture.Csv.LastReport
            : fixture.Pdf.LastReport);
        Assert.Equal(1, fixture.Service.CallCount);
    }

    [Fact]
    public async Task Invalid_filter_returns_400_without_querying_or_exporting()
    {
        var fixture = new Fixture();

        var result = await fixture.Controller.Get(
            new DemographicsReportFilter("invalid", null, null, null, null),
            DemographicsReportFormat.Csv);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("ageBand", problem.Errors.Keys);
        Assert.Equal(0, fixture.Service.CallCount);
        Assert.Equal(0, fixture.Csv.CallCount);
        Assert.Equal(0, fixture.Pdf.CallCount);
    }

    [Fact]
    public async Task Invalid_format_returns_400_without_querying()
    {
        var fixture = new Fixture();

        var result = await fixture.Controller.Get(
            new DemographicsReportFilter(),
            (DemographicsReportFormat)99);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("format", problem.Errors.Keys);
        Assert.Equal(0, fixture.Service.CallCount);
    }

    [Fact]
    public void Controller_requires_admin_role()
    {
        var authorization = typeof(DemographicsReportsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("Admin", authorization.Roles);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Report = EmptyReport();
            Service = new StubService(Report);
            Csv = new StubCsvExporter();
            Pdf = new StubPdfExporter();
            IValidator<DemographicsReportFilter> validator = new DemographicsReportFilterValidator();
            Controller = new DemographicsReportsController(validator, Service, Csv, Pdf);
        }

        public DemographicsReportResponse Report { get; }
        public StubService Service { get; }
        public StubCsvExporter Csv { get; }
        public StubPdfExporter Pdf { get; }
        public DemographicsReportsController Controller { get; }
    }

    private sealed class StubService : IDemographicsReportService
    {
        private readonly DemographicsReportResponse _report;

        public StubService(DemographicsReportResponse report)
        {
            _report = report;
        }

        public int CallCount { get; private set; }
        public DemographicsReportFilter? LastFilter { get; private set; }

        public Task<DemographicsReportResponse> GenerateAsync(
            DemographicsReportFilter filter,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastFilter = filter;
            return Task.FromResult(_report);
        }
    }

    private sealed class StubCsvExporter : IDemographicsCsvExporter
    {
        public int CallCount { get; private set; }
        public DemographicsReportResponse? LastReport { get; private set; }

        public byte[] Export(DemographicsReportResponse report)
        {
            CallCount++;
            LastReport = report;
            return [1];
        }
    }

    private sealed class StubPdfExporter : IDemographicsPdfExporter
    {
        public int CallCount { get; private set; }
        public DemographicsReportResponse? LastReport { get; private set; }

        public byte[] Export(DemographicsReportResponse report)
        {
            CallCount++;
            LastReport = report;
            return [2];
        }
    }

    private static DemographicsReportResponse EmptyReport() => new(
        new DateTime(2026, 9, 14, 10, 30, 0, DateTimeKind.Utc),
        new AppliedDemographicsFilters(null, null, null, null, null),
        0,
        0,
        0,
        0,
        [],
        [],
        [],
        [],
        []);
}
