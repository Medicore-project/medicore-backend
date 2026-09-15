using System.Security.Claims;
using FluentValidation;
using MediCore.Patient.Api.Controllers;
using MediCore.Patient.Api.Middleware;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Services;
using MediCore.Patient.Application.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Patient.Tests.Unit;

public sealed class MedicalRecordsControllerTests
{
    [Fact]
    public async Task List_returns_an_empty_page_instead_of_an_error()
    {
        var service = new StubMedicalRecordService { Page = EmptyPage() };
        var controller = CreateController(service);

        var result = await controller.GetPage(
            Guid.NewGuid(),
            new MedicalRecordListRequest(),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<PagedMedicalRecordResponse>(ok.Value).Items);
    }

    [Fact]
    public async Task Invalid_list_pagination_returns_400_without_calling_service()
    {
        var service = new StubMedicalRecordService();
        var controller = CreateController(service);

        var result = await controller.GetPage(
            Guid.NewGuid(),
            new MedicalRecordListRequest { Page = 0, PageSize = 101 },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("page", problem.Errors.Keys);
        Assert.Equal(0, service.PageCalls);
    }

    [Fact]
    public async Task Get_missing_record_returns_404()
    {
        var controller = CreateController(new StubMedicalRecordService());

        var result = await controller.GetById(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Version_history_returns_200()
    {
        var response = RecordResponse();
        var controller = CreateController(new StubMedicalRecordService { Versions = [response] });

        var result = await controller.GetVersions(
            response.PatientId,
            response.RecordId,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MedicalRecordResponse>>(ok.Value));
    }

    [Fact]
    public async Task Valid_create_returns_201_with_record_location()
    {
        var response = RecordResponse();
        var service = new StubMedicalRecordService
        {
            CreateResult = new MedicalRecordCreatedResult(response)
        };
        var controller = CreateController(service);

        var result = await controller.Create(
            response.PatientId,
            new CreateMedicalRecordRequest(response.VisitReference, "Clinical notes", []),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(MedicalRecordsController.GetById), created.ActionName);
        Assert.Equal(response, Assert.IsType<MedicalRecordResponse>(created.Value));
    }

    [Fact]
    public async Task Create_for_missing_patient_returns_404()
    {
        var controller = CreateController(new StubMedicalRecordService());

        var result = await controller.Create(
            Guid.NewGuid(),
            new CreateMedicalRecordRequest(Guid.NewGuid(), "Clinical notes", []),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Invalid_create_returns_400_without_calling_service()
    {
        var service = new StubMedicalRecordService();
        var controller = CreateController(service);

        var result = await controller.Create(
            Guid.NewGuid(),
            new CreateMedicalRecordRequest(Guid.Empty, "", []),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task Stale_update_returns_409_with_current_version()
    {
        var service = new StubMedicalRecordService
        {
            UpdateResult = new MedicalRecordUpdateConflictResult(4)
        };
        var controller = CreateController(service);

        var result = await controller.Update(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UpdateMedicalRecordRequest(3, "Updated notes", []),
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Contains("current version is 4", problem.Detail);
    }

    [Fact]
    public async Task Successful_update_returns_the_new_version()
    {
        var response = RecordResponse() with { Version = 2 };
        var controller = CreateController(new StubMedicalRecordService
        {
            UpdateResult = new MedicalRecordUpdatedResult(response)
        });

        var result = await controller.Update(
            response.PatientId,
            response.RecordId,
            new UpdateMedicalRecordRequest(1, "Updated notes", []),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, Assert.IsType<MedicalRecordResponse>(ok.Value).Version);
    }

    [Fact]
    public async Task Invalid_update_returns_400()
    {
        var controller = CreateController(new StubMedicalRecordService());

        var result = await controller.Update(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UpdateMedicalRecordRequest(0, "", []),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Successful_delete_returns_204()
    {
        var service = new StubMedicalRecordService { DeleteResult = true };
        var controller = CreateController(service);

        var result = await controller.Delete(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_missing_record_returns_404()
    {
        var controller = CreateController(new StubMedicalRecordService());

        var result = await controller.Delete(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Access_context_is_derived_from_authenticated_claims()
    {
        var service = new StubMedicalRecordService { Page = EmptyPage() };
        var controller = CreateController(service);

        await controller.GetPage(Guid.NewGuid(), new MedicalRecordListRequest(), CancellationToken.None);

        Assert.NotNull(service.AccessContext);
        Assert.Equal("doctor-1", service.AccessContext.ActorId);
        Assert.Equal("Doctor", service.AccessContext.ActorRole);
        Assert.Equal("doctor@medicore.lk", service.AccessContext.ActorEmail);
        Assert.Equal("corr-controller-26", service.AccessContext.CorrelationId);
    }

    private static MedicalRecordsController CreateController(IMedicalRecordService service)
    {
        IValidator<CreateMedicalRecordRequest> createValidator = new CreateMedicalRecordRequestValidator();
        IValidator<UpdateMedicalRecordRequest> updateValidator = new UpdateMedicalRecordRequestValidator();
        IValidator<MedicalRecordListRequest> listValidator = new MedicalRecordListRequestValidator();
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "doctor-1"),
            new Claim(ClaimTypes.Role, "Doctor"),
            new Claim(ClaimTypes.Email, "doctor@medicore.lk")
        ], "Test");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        context.Items[CorrelationIdMiddleware.ItemKey] = "corr-controller-26";

        return new MedicalRecordsController(createValidator, updateValidator, listValidator, service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static PagedMedicalRecordResponse EmptyPage() => new([], 0, 1, 20, 0, false, false);

    private static MedicalRecordResponse RecordResponse()
    {
        var patientId = Guid.NewGuid();
        return new MedicalRecordResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            patientId,
            Guid.NewGuid(),
            "Clinical notes",
            "doctor-1",
            "doctor@medicore.lk",
            "Doctor",
            DateTime.UtcNow,
            1,
            null,
            true,
            []);
    }

    private sealed class StubMedicalRecordService : IMedicalRecordService
    {
        public PagedMedicalRecordResponse? Page { get; set; }
        public MedicalRecordResponse? Record { get; set; }
        public IReadOnlyList<MedicalRecordResponse>? Versions { get; set; }
        public MedicalRecordCreateResult CreateResult { get; set; } = new MedicalRecordCreatePatientNotFoundResult();
        public MedicalRecordUpdateResult UpdateResult { get; set; } = new MedicalRecordUpdateNotFoundResult();
        public bool DeleteResult { get; set; }
        public int PageCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public PatientAccessContext? AccessContext { get; private set; }

        public Task<PagedMedicalRecordResponse?> GetPageAsync(
            Guid patientId,
            MedicalRecordListRequest request,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default)
        {
            PageCalls++;
            AccessContext = accessContext;
            return Task.FromResult(Page);
        }

        public Task<MedicalRecordResponse?> GetByIdAsync(
            Guid patientId,
            Guid recordId,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default) => Task.FromResult(Record);

        public Task<IReadOnlyList<MedicalRecordResponse>?> GetVersionsAsync(
            Guid patientId,
            Guid recordId,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default) => Task.FromResult(Versions);

        public Task<MedicalRecordCreateResult> CreateAsync(
            Guid patientId,
            CreateMedicalRecordRequest request,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(CreateResult);
        }

        public Task<MedicalRecordUpdateResult> UpdateAsync(
            Guid patientId,
            Guid recordId,
            UpdateMedicalRecordRequest request,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default) => Task.FromResult(UpdateResult);

        public Task<bool> DeleteAsync(
            Guid patientId,
            Guid recordId,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default) => Task.FromResult(DeleteResult);
    }
}
