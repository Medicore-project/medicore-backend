using MediCore.Identity.Api.Controllers;
using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using MediCore.Identity.Application.Validators;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace MediCore.Identity.Tests.Unit;

public sealed class StaffControllerTests
{
    private static StaffController CreateController(
        Mock<IStaffRepository> staffRepository,
        Mock<IPasswordHasher>? passwordHasher = null,
        Mock<IRoleRepository>? roleRepository = null)
    {
        // Real FluentValidation validators are used (not mocked) so validation-order
        // behaviour (validate first, then check duplicates) is exercised for real.
        return new StaffController(
            staffRepository.Object,
            (passwordHasher ?? new Mock<IPasswordHasher>()).Object,
            (roleRepository ?? new Mock<IRoleRepository>()).Object,
            new CreateStaffRequestValidator(),
            new UpdateStaffRequestValidator());
    }

    private static CreateStaffRequest ValidCreateRequest(string email = "new.doctor@medicore.local") => new(
        Email: email,
        Password: "Password123",
        Role: "Doctor",
        FirstName: "Jane",
        LastName: "Doe",
        Phone: "0771234567",
        Specialization: "Cardiology",
        DepartmentId: Guid.NewGuid());

    [Fact]
    public async Task Create_with_an_email_that_already_exists_returns_409_and_writes_nothing()
    {
        var staffRepository = new Mock<IStaffRepository>();
        staffRepository
            .Setup(r => r.GetUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Email = "new.doctor@medicore.local", Role = "Doctor" });

        var controller = CreateController(staffRepository);

        var result = await controller.Create(ValidCreateRequest(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        staffRepository.Verify(
            r => r.CreateStaffAsync(It.IsAny<CreateStaffRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_with_invalid_input_returns_400_without_checking_for_a_duplicate_email()
    {
        var staffRepository = new Mock<IStaffRepository>();
        var controller = CreateController(staffRepository);

        var invalidRequest = ValidCreateRequest() with { Email = "not-an-email", Password = "short" };

        var result = await controller.Create(invalidRequest, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        staffRepository.Verify(
            r => r.GetUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        staffRepository.Verify(
            r => r.CreateStaffAsync(It.IsAny<CreateStaffRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_with_a_new_email_hashes_the_password_and_persists_the_staff_member()
    {
        var staffId = Guid.NewGuid();
        var staffRepository = new Mock<IStaffRepository>();
        staffRepository
            .Setup(r => r.GetUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        staffRepository
            .Setup(r => r.CreateStaffAsync(It.IsAny<CreateStaffRequest>(), "hashed-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffResponse(
                staffId, Guid.NewGuid(), "new.doctor@medicore.local", "Doctor", "Jane", "Doe", "Jane Doe",
                "0771234567", "Cardiology", Guid.NewGuid(), DateTime.UtcNow, true, DateTime.UtcNow, null));

        var passwordHasher = new Mock<IPasswordHasher>();
        passwordHasher.Setup(h => h.HashPassword("Password123")).Returns("hashed-password");

        var controller = CreateController(staffRepository, passwordHasher);

        var result = await controller.Create(ValidCreateRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(staffId, ((StaffResponse)created.Value!).Id);
        passwordHasher.Verify(h => h.HashPassword("Password123"), Times.Once);
    }

    [Fact]
    public async Task Delete_an_active_staff_member_soft_deletes_and_returns_204()
    {
        var staffId = Guid.NewGuid();
        var staffRepository = new Mock<IStaffRepository>();
        staffRepository
            .Setup(r => r.DeactivateStaffAsync(staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(staffRepository);

        var result = await controller.Delete(staffId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_an_unknown_staff_member_returns_404()
    {
        var staffRepository = new Mock<IStaffRepository>();
        staffRepository
            .Setup(r => r.DeactivateStaffAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = CreateController(staffRepository);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
