using MediCore.Appointment.Api.Controllers;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Tests.Unit;

public sealed class DoctorsControllerTests
{
    private static readonly Guid DoctorId = Guid.Parse("3f5b9c1e-1f0a-4a7c-9b3d-7c9a2e4f6b18");
    private static readonly Guid DeactivatedDoctorId = Guid.Parse("a1c4e8d2-55b6-4f39-8e71-2d6c0b9a4371");
    private static readonly Guid DepartmentId = Guid.Parse("b7d2a9e4-6c31-4f58-a0e2-91c4d7b3f605");

    // ── GET /api/doctors ──────────────────────────────────────────────────────

    [Fact]
    public async Task Listing_returns_the_bookable_doctors_from_the_cache()
    {
        var repository = new FakeDoctorCacheRepository();
        var controller = CreateController(repository);

        var result = await controller.List(specialization: null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var doctor = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<DoctorResponse>>(ok.Value));
        Assert.Equal(new DoctorResponse(DoctorId, "Nimal Perera", "Cardiology", DepartmentId), doctor);
    }

    [Fact]
    public async Task Listing_passes_the_specialization_filter_to_the_cache()
    {
        var repository = new FakeDoctorCacheRepository();
        var controller = CreateController(repository);

        await controller.List("cardiology", CancellationToken.None);

        Assert.Equal("cardiology", repository.LastSpecialization);
    }

    [Fact]
    public async Task An_empty_cache_lists_no_doctors_rather_than_failing()
    {
        // What a fresh deployment returns until the backfill has been run.
        var repository = new FakeDoctorCacheRepository(empty: true);
        var controller = CreateController(repository);

        var result = await controller.List(specialization: null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<DoctorResponse>>(ok.Value));
    }

    // ── GET /api/doctors/{doctorId} ───────────────────────────────────────────

    [Fact]
    public async Task A_bookable_doctor_is_returned_by_id()
    {
        var controller = CreateController(new FakeDoctorCacheRepository());

        var result = await controller.GetById(DoctorId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(DoctorId, Assert.IsType<DoctorResponse>(ok.Value).DoctorId);
    }

    [Theory]
    [InlineData("a1c4e8d2-55b6-4f39-8e71-2d6c0b9a4371")]
    [InlineData("5e9d3a72-8c14-4b06-9f2e-6a1d0c7b8e35")]
    public async Task A_deactivated_or_unknown_doctor_is_404(string doctorId)
    {
        var controller = CreateController(new FakeDoctorCacheRepository());

        var result = await controller.GetById(Guid.Parse(doctorId), CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("Doctor not found or not bookable.", problem.Title);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DoctorsController CreateController(IDoctorCacheRepository repository) =>
        new(new DoctorDirectoryService(repository));

    private sealed class FakeDoctorCacheRepository : IDoctorCacheRepository
    {
        private readonly List<DoctorCache> _doctors;

        public FakeDoctorCacheRepository(bool empty = false)
        {
            _doctors = empty
                ? []
                :
                [
                    new DoctorCache
                    {
                        DoctorId = DoctorId,
                        FullName = "Nimal Perera",
                        Specialization = "Cardiology",
                        DepartmentId = DepartmentId,
                        IsActive = true
                    },
                    new DoctorCache
                    {
                        DoctorId = DeactivatedDoctorId,
                        FullName = "Kamala Silva",
                        Specialization = "Cardiology",
                        DepartmentId = DepartmentId,
                        IsActive = false
                    }
                ];
        }

        public string? LastSpecialization { get; private set; }

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(
            string? specialization,
            CancellationToken cancellationToken = default)
        {
            LastSpecialization = specialization;
            return Task.FromResult<IReadOnlyList<DoctorCache>>(
                [.. _doctors.Where(doctor => doctor.IsActive)]);
        }

        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_doctors.FirstOrDefault(doctor => doctor.DoctorId == doctorId && doctor.IsActive));

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(
            Guid doctorId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The directory never writes to the doctor cache.");

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the public booking page lists specializations.");

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The directory never writes to the doctor cache.");
    }
}
