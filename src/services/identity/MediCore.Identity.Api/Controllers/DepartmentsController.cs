using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentRepository _repository;

    public DepartmentsController(IDepartmentRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var departments = await _repository.GetAllAsync(cancellationToken);
        var response = departments.Select(d => new DepartmentResponse(
            d.Id, d.Name, d.Description, d.IsActive, d.CreatedAt, d.UpdatedAt));
            
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var department = await _repository.GetByIdAsync(id, cancellationToken);
        if (department == null)
            return NotFound();

        return Ok(new DepartmentResponse(
            department.Id, department.Name, department.Description, department.IsActive, department.CreatedAt, department.UpdatedAt));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var trimmedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            return BadRequest("Name cannot be empty or whitespace.");

        var existing = await _repository.GetByNameAsync(trimmedName, cancellationToken);
        if (existing != null)
            return Conflict($"A department with the name '{trimmedName}' already exists.");

        var department = new Department
        {
            Name = trimmedName,
            Description = request.Description?.Trim() ?? string.Empty,
            IsActive = true
        };

        await _repository.AddAsync(department, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = department.Id }, 
            new DepartmentResponse(department.Id, department.Name, department.Description, department.IsActive, department.CreatedAt, department.UpdatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var trimmedName = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
            return BadRequest("Name cannot be empty or whitespace.");

        var department = await _repository.GetByIdAsync(id, cancellationToken);
        if (department == null)
            return NotFound();

        var existingName = await _repository.GetByNameAsync(trimmedName, cancellationToken);
        if (existingName != null && existingName.Id != id)
            return Conflict($"A department with the name '{trimmedName}' already exists.");

        department.Name = trimmedName;
        department.Description = request.Description?.Trim() ?? string.Empty;
        department.IsActive = request.IsActive;

        await _repository.UpdateAsync(department, cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var department = await _repository.GetByIdAsync(id, cancellationToken);
        if (department == null)
            return NotFound();

        var hasStaff = await _repository.HasActiveStaffMembersAsync(id, cancellationToken);
        if (hasStaff)
            return BadRequest("Cannot delete department because it is currently assigned to one or more active staff members.");

        await _repository.DeleteAsync(department, cancellationToken);

        return NoContent();
    }
}
