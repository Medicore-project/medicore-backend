using FluentValidation;
using MediCore.Patient.Api.Controllers;
using MediCore.Patient.Api.Middleware;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Services;
using MediCore.Patient.Application.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Patient.Tests.Unit;

public sealed class PatientsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_registration_returns_201_and_generated_patient_number()
    {
        var patientId = Guid.NewGuid();
        var response = new PatientRegistrationResponse(
            patientId,
            "PAT-000001",
            "200012345678",
            "Nimali",
            "Perera",
            "Nimali Perera",
            new DateOnly(2000, 5, 15),
            "Female",
            "nimali@example.com",
            "0771234567",
            "12 Hospital Road",
            null,
            "Colombo",
            null,
            null,
            Now.UtcDateTime);
        var controller = CreateController(new PatientRegisteredResult(response));

        var result = await controller.Register(ValidRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result);
        var body = Assert.IsType<PatientRegistrationResponse>(created.Value);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal("PAT-000001", body.PatientNumber);
        Assert.Equal($"/api/patients/{patientId}", created.Location);
    }

    [Fact]
    public async Task Duplicate_registration_returns_409_with_existing_patient()
    {
        var existing = new ExistingPatientSummary(
            Guid.NewGuid(), "PAT-000099", "Existing Patient", "existing@example.com", false);
        var controller = CreateController(new DuplicatePatientResult(existing));

        var result = await controller.Register(ValidRequest(), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = Assert.IsType<DuplicatePatientResponse>(conflict.Value);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(existing.PatientId, body.ExistingPatient.PatientId);
        Assert.Equal("PAT-000099", body.ExistingPatient.PatientNumber);
    }

    [Fact]
    public async Task Invalid_request_returns_field_indexed_400_response()
    {
        var service = new StubRegistrationService(new PatientRegisteredResult(null!));
        var controller = CreateController(service);
        var request = ValidRequest() with { DateOfBirth = new DateOnly(2027, 1, 1) };

        var result = await controller.Register(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Contains("dateOfBirth", problem.Errors.Keys);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task Get_existing_patient_returns_200()
    {
        var profile = ProfileResponse();
        var profileService = new StubProfileService { Profile = profile };
        var controller = CreateController(
            new StubRegistrationService(new PatientRegisteredResult(null!)),
            profileService);

        var result = await controller.GetById(profile.PatientId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(profile, Assert.IsType<PatientProfileResponse>(ok.Value));
        Assert.Equal(1, profileService.GetCallCount);
    }

    [Fact]
    public async Task Valid_update_returns_updated_profile()
    {
        var profile = ProfileResponse();
        var profileService = new StubProfileService
        {
            UpdateResult = new PatientUpdatedResult(profile)
        };
        var controller = CreateController(
            new StubRegistrationService(new PatientRegisteredResult(null!)),
            profileService);

        var result = await controller.Update(profile.PatientId, ValidUpdateRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(profile, Assert.IsType<PatientProfileResponse>(ok.Value));
        Assert.Equal(1, profileService.UpdateCallCount);
    }

    [Fact]
    public async Task Invalid_update_returns_400_without_calling_service()
    {
        var profileService = new StubProfileService();
        var controller = CreateController(
            new StubRegistrationService(new PatientRegisteredResult(null!)),
            profileService);

        var result = await controller.Update(
            Guid.NewGuid(),
            ValidUpdateRequest() with { Phone = "invalid" },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("phone", problem.Errors.Keys);
        Assert.Equal(0, profileService.UpdateCallCount);
    }

    [Fact]
    public async Task Successful_delete_returns_204()
    {
        var profileService = new StubProfileService { DeleteResult = true };
        var controller = CreateController(
            new StubRegistrationService(new PatientRegisteredResult(null!)),
            profileService);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(1, profileService.DeleteCallCount);
    }

    [Fact]
    public async Task Search_returns_200_with_paginated_results()
    {
        var page = SearchResponse();
        var searchService = new StubSearchService { Response = page };
        var controller = CreateController(
            new StubRegistrationService(new PatientRegisteredResult(null!)),
            new StubProfileService(),
            searchService);

        var result = await controller.Search(
            new PatientSearchRequest("Nimali", 1, 20),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(page, Assert.IsType<PatientSearchResponse>(ok.Value));
        Assert.Equal(1, searchService.CallCount);
    }

    [Fact]
    public async Task Search_with_no_matches_returns_200_with_empty_items()
    {
        var controller = CreateController(
            new StubRegistrationService(new PatientRegisteredResult(null!)),
            new StubProfileService(),
            new StubSearchService());

        var result = await controller.Search(
            new PatientSearchRequest("missing", 1, 20),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PatientSearchResponse>(ok.Value);
        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task Invalid_search_pagination_returns_400_without_calling_service()
    {
        var searchService = new StubSearchService();
        var controller = CreateController(
            new StubRegistrationService(new PatientRegisteredResult(null!)),
            new StubProfileService(),
            searchService);

        var result = await controller.Search(
            new PatientSearchRequest("Nimali", 0, 101),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("page", problem.Errors.Keys);
        Assert.Contains("pageSize", problem.Errors.Keys);
        Assert.Equal(0, searchService.CallCount);
    }

    private static PatientsController CreateController(PatientRegistrationResult result) =>
        CreateController(new StubRegistrationService(result));

    private static PatientsController CreateController(IPatientRegistrationService service)
        => CreateController(service, new StubProfileService());

    private static PatientsController CreateController(
        IPatientRegistrationService service,
        IPatientProfileService profileService) =>
        CreateController(service, profileService, new StubSearchService());

    private static PatientsController CreateController(
        IPatientRegistrationService service,
        IPatientProfileService profileService,
        IPatientSearchService searchService)
    {
        IValidator<CreatePatientRequest> validator = new CreatePatientRequestValidator(new FixedTimeProvider(Now));
        IValidator<UpdatePatientRequest> updateValidator = new UpdatePatientRequestValidator(new FixedTimeProvider(Now));
        IValidator<PatientSearchRequest> searchValidator = new PatientSearchRequestValidator();
        var controller = new PatientsController(
            validator,
            updateValidator,
            searchValidator,
            service,
            profileService,
            searchService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Items[CorrelationIdMiddleware.ItemKey] = "corr-controller-test";
        return controller;
    }

    private sealed class StubSearchService : IPatientSearchService
    {
        public PatientSearchResponse Response { get; init; } = PatientSearchResponse.Empty(1, 20);
        public int CallCount { get; private set; }

        public Task<PatientSearchResponse> SearchAsync(
            PatientSearchRequest request,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Response);
        }
    }

    private sealed class StubProfileService : IPatientProfileService
    {
        public PatientProfileResponse? Profile { get; init; }
        public PatientUpdateResult UpdateResult { get; init; } = new PatientUpdateNotFoundResult();
        public bool DeleteResult { get; init; }
        public int GetCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }

        public Task<PatientProfileResponse?> GetByIdAsync(
            Guid patientId,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(Profile);
        }

        public Task<PatientUpdateResult> UpdateAsync(
            Guid patientId,
            UpdatePatientRequest request,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            return Task.FromResult(UpdateResult);
        }

        public Task<bool> DeleteAsync(
            Guid patientId,
            PatientAccessContext accessContext,
            CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            return Task.FromResult(DeleteResult);
        }
    }

    private static CreatePatientRequest ValidRequest() => new(
        "200012345678",
        "Nimali",
        "Perera",
        new DateOnly(2000, 5, 15),
        "Female",
        "nimali@example.com",
        "0771234567",
        "12 Hospital Road",
        null,
        "Colombo",
        null,
        null);

    private static UpdatePatientRequest ValidUpdateRequest() => new(
        "Nimali",
        "Perera",
        new DateOnly(2000, 5, 15),
        "Female",
        "nimali@example.com",
        "0771234567",
        "12 Hospital Road",
        null,
        "Colombo",
        null,
        null);

    private static PatientProfileResponse ProfileResponse() => new(
        Guid.NewGuid(),
        "PAT-000024",
        "200012345678",
        "Nimali",
        "Perera",
        "Nimali Perera",
        new DateOnly(2000, 5, 15),
        "Female",
        "nimali@example.com",
        "0771234567",
        "12 Hospital Road",
        null,
        "Colombo",
        null,
        null,
        Now.UtcDateTime,
        null);

    private static PatientSearchResponse SearchResponse()
    {
        var item = new PatientSearchResult(
            Guid.NewGuid(),
            "PAT-000024",
            "200012345678",
            "Nimali Perera",
            new DateOnly(2000, 5, 15),
            "0771234567",
            "nimali@example.com",
            "Colombo");

        return new PatientSearchResponse([item], 1, 1, 20, 1, false, false);
    }

    private sealed class StubRegistrationService : IPatientRegistrationService
    {
        private readonly PatientRegistrationResult _result;

        public StubRegistrationService(PatientRegistrationResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<PatientRegistrationResult> RegisterAsync(
            CreatePatientRequest request,
            string correlationId,
            string createdBy,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
