using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpecializationsController : ControllerBase
{
    private readonly ISpecializationRepository _repository;

    public SpecializationsController(ISpecializationRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var specializations = await _repository.GetAllAsync(cancellationToken);
        var response = specializations.Select(s => new SpecializationResponse(
            s.Id, s.Name, s.Description, s.IsActive, s.CreatedAt, s.UpdatedAt));
            
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var specialization = await _repository.GetByIdAsync(id, cancellationToken);
        if (specialization == null)
            return NotFound();

        return Ok(new SpecializationResponse(
            specialization.Id, specialization.Name, specialization.Description, specialization.IsActive, specialization.CreatedAt, specialization.UpdatedAt));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSpecializationRequest request, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByNameAsync(request.Name, cancellationToken);
        if (existing != null)
            return Conflict($"A specialization with the name '{request.Name}' already exists.");

        var specialization = new Specialization
        {
            Name = request.Name,
            Description = request.Description,
            IsActive = true
        };

        await _repository.AddAsync(specialization, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = specialization.Id }, 
            new SpecializationResponse(specialization.Id, specialization.Name, specialization.Description, specialization.IsActive, specialization.CreatedAt, specialization.UpdatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSpecializationRequest request, CancellationToken cancellationToken)
    {
        var specialization = await _repository.GetByIdAsync(id, cancellationToken);
        if (specialization == null)
            return NotFound();

        var existingName = await _repository.GetByNameAsync(request.Name, cancellationToken);
        if (existingName != null && existingName.Id != id)
            return Conflict($"A specialization with the name '{request.Name}' already exists.");

        specialization.Name = request.Name;
        specialization.Description = request.Description;
        specialization.IsActive = request.IsActive;

        await _repository.UpdateAsync(specialization, cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var specialization = await _repository.GetByIdAsync(id, cancellationToken);
        if (specialization == null)
            return NotFound();

        var hasStaff = await _repository.HasActiveStaffMembersAsync(id, cancellationToken);
        if (hasStaff)
            return BadRequest("Cannot delete specialization because it is currently assigned to one or more active staff members.");

        await _repository.DeleteAsync(specialization, cancellationToken);

        return NoContent();
    }
}
