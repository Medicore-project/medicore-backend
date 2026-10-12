using System.Net;
using MediCore.Identity.Api.Controllers;
using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using MediCore.Identity.Application.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Identity.Tests.Unit;

public sealed class StaffPasswordChangeTests
{
    private static readonly Guid StaffId = Guid.Parse("7a1f0c52-3a0e-4c53-9d0b-6f1f6a2b9c11");

    // ── The password rule ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("        ")]
    [InlineData("seven77")]
    public void A_missing_or_short_password_is_refused(string password)
    {
        var result = new ChangeStaffPasswordRequestValidator().Validate(new ChangeStaffPasswordRequest(password));

        Assert.False(result.IsValid);
        Assert.All(result.Errors, error => Assert.Equal(nameof(ChangeStaffPasswordRequest.NewPassword), error.PropertyName));
    }

    [Fact]
    public void A_password_longer_than_the_limit_is_refused()
    {
        var result = new ChangeStaffPasswordRequestValidator()
            .Validate(new ChangeStaffPasswordRequest(new string('a', 101)));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(100)]
    public void A_password_within_the_limits_is_accepted(int length)
    {
        var result = new ChangeStaffPasswordRequestValidator()
            .Validate(new ChangeStaffPasswordRequest(new string('a', length)));

        Assert.True(result.IsValid);
    }

    // ── The endpoint ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_new_password_is_hashed_before_it_is_stored()
    {
        var fixture = new Fixture();
        fixture.Staff.Existing.Add(StaffId);

        var result = await fixture.Controller.ChangePassword(
            StaffId, new ChangeStaffPasswordRequest("N3w-Secret!"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var change = Assert.Single(fixture.Staff.PasswordChanges);
        Assert.Equal(StaffId, change.StaffId);
        // What reaches the repository is the hasher's output, never what the admin typed.
        Assert.Equal(new[] { "N3w-Secret!" }, fixture.Hasher.Hashed);
        Assert.Equal("hash-1", change.PasswordHash);
        // Recorded against the sessions the change revokes.
        Assert.Equal("203.0.113.7", change.ChangedByIp);
    }

    [Fact]
    public async Task Changing_the_password_of_a_staff_member_who_does_not_exist_is_not_found()
    {
        var fixture = new Fixture();

        var result = await fixture.Controller.ChangePassword(
            StaffId, new ChangeStaffPasswordRequest("N3w-Secret!"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task A_password_that_breaks_the_rule_is_refused_before_anything_is_hashed_or_saved()
    {
        var fixture = new Fixture();
        fixture.Staff.Existing.Add(StaffId);

        var result = await fixture.Controller.ChangePassword(
            StaffId, new ChangeStaffPasswordRequest("short"), CancellationToken.None);

        var refused = Assert.IsType<BadRequestObjectResult>(result);
        var messages = Assert.IsAssignableFrom<IEnumerable<string>>(refused.Value);
        Assert.Contains("Password must be at least 8 characters long.", messages);
        Assert.Equal(0, fixture.Hasher.HashCalls);
        Assert.Empty(fixture.Staff.PasswordChanges);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class Fixture
    {
        public Fixture()
        {
            Controller = new StaffController(
                Staff,
                Hasher,
                new UnusedRoleRepository(),
                new CreateStaffRequestValidator(),
                new UpdateStaffRequestValidator(),
                new ChangeStaffPasswordRequestValidator())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        Connection = { RemoteIpAddress = IPAddress.Parse("203.0.113.7") }
                    }
                }
            };
        }

        public FakeStaffRepository Staff { get; } = new();
        public FakePasswordHasher Hasher { get; } = new();
        public StaffController Controller { get; }
    }

    private sealed record PasswordChange(Guid StaffId, string PasswordHash, string ChangedByIp);

    private sealed class FakeStaffRepository : IStaffRepository
    {
        public HashSet<Guid> Existing { get; } = [];
        public List<PasswordChange> PasswordChanges { get; } = [];

        public Task<bool> ChangePasswordAsync(
            Guid staffId,
            string passwordHash,
            string changedByIp,
            CancellationToken cancellationToken = default)
        {
            if (!Existing.Contains(staffId))
            {
                return Task.FromResult(false);
            }

            PasswordChanges.Add(new PasswordChange(staffId, passwordHash, changedByIp));
            return Task.FromResult(true);
        }

        public Task<PagedResult<StaffResponse>> GetPagedAsync(
            int page,
            int pageSize,
            string? searchTerm,
            Guid? departmentId,
            string? role,
            bool? isActive,
            string? specialization = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never lists staff.");

        public Task<StaffResponse?> GetByIdAsync(Guid staffId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never reads a profile.");

        public Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never looks a user up by email.");

        public Task<StaffResponse> CreateStaffAsync(
            CreateStaffRequest request,
            string passwordHash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never creates staff.");

        public Task<StaffResponse?> UpdateStaffAsync(
            Guid staffId,
            UpdateStaffRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never edits a profile.");

        public Task<bool> DeactivateStaffAsync(Guid staffId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never deactivates staff.");

        public Task<int> QueueDoctorRepublishAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never republishes doctors.");
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public List<string> Hashed { get; } = [];
        public int HashCalls => Hashed.Count;

        public string HashPassword(string password)
        {
            Hashed.Add(password);
            return $"hash-{Hashed.Count}";
        }

        public bool VerifyPassword(string password, string passwordHash) =>
            throw new NotSupportedException("A password change by an admin verifies no old password.");
    }

    private sealed class UnusedRoleRepository : IRoleRepository
    {
        public Task<IReadOnlyList<RoleResponse>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never reads roles.");

        public Task<RoleResponse?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never reads roles.");

        public Task<bool> AssignRoleToStaffAsync(Guid staffId, Guid roleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A password change never assigns roles.");
    }
}
