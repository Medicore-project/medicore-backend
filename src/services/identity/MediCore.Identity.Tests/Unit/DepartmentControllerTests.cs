using MediCore.Identity.Api.Controllers;
using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace MediCore.Identity.Tests.Unit;

public sealed class DepartmentControllerTests
{
    [Fact]
    public async Task Create_with_a_name_that_already_exists_returns_409_and_writes_nothing()
    {
        var repository = new Mock<IDepartmentRepository>();
        repository
            .Setup(r => r.GetByNameAsync("Cardiology", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Department { Name = "Cardiology" });

        var controller = new DepartmentsController(repository.Object);

        var result = await controller.Create(new CreateDepartmentRequest("Cardiology", "Heart care"), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        repository.Verify(r => r.AddAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_with_a_new_name_succeeds()
    {
        var repository = new Mock<IDepartmentRepository>();
        repository
            .Setup(r => r.GetByNameAsync("Neurology", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Department?)null);

        var controller = new DepartmentsController(repository.Object);

        var result = await controller.Create(new CreateDepartmentRequest("Neurology", "Brain and nerves"), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
        repository.Verify(r => r.AddAsync(It.Is<Department>(d => d.Name == "Neurology"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_is_blocked_with_400_when_the_department_still_has_active_staff()
    {
        var departmentId = Guid.NewGuid();
        var repository = new Mock<IDepartmentRepository>();
        repository
            .Setup(r => r.GetByIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Department { Id = departmentId, Name = "Cardiology" });
        repository
            .Setup(r => r.HasActiveStaffMembersAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = new DepartmentsController(repository.Object);

        var result = await controller.Delete(departmentId, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        repository.Verify(r => r.DeleteAsync(It.IsAny<Department>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_succeeds_when_the_department_has_no_active_staff()
    {
        var departmentId = Guid.NewGuid();
        var department = new Department { Id = departmentId, Name = "Cardiology" };
        var repository = new Mock<IDepartmentRepository>();
        repository
            .Setup(r => r.GetByIdAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(department);
        repository
            .Setup(r => r.HasActiveStaffMembersAsync(departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = new DepartmentsController(repository.Object);

        var result = await controller.Delete(departmentId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        repository.Verify(r => r.DeleteAsync(department, It.IsAny<CancellationToken>()), Times.Once);
    }
}
