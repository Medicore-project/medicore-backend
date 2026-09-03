using FluentValidation;
using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StaffController : ControllerBase
{
    private readonly IStaffRepository _staffRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRoleRepository _roleRepository;
    private readonly IValidator<CreateStaffRequest> _createValidator;
    private readonly IValidator<UpdateStaffRequest> _updateValidator;

    public StaffController(
        IStaffRepository staffRepository,
        IPasswordHasher passwordHasher,
        IRoleRepository roleRepository,
        IValidator<CreateStaffRequest> createValidator,
        IValidator<UpdateStaffRequest> updateValidator)
    {
        _staffRepository = staffRepository;
        _passwordHasher = passwordHasher;
        _roleRepository = roleRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var result = await _staffRepository.GetPagedAsync(
            page, pageSize, search, departmentId, role, isActive, cancellationToken);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var staff = await _staffRepository.GetByIdAsync(id, cancellationToken);
        if (staff == null)
            return NotFound();

        return Ok(staff);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateStaffRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var existingUser = await _staffRepository.GetUserByEmailAsync(request.Email, cancellationToken);
        if (existingUser != null)
            return Conflict($"A staff member with email '{request.Email}' already exists.");

        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var staff = await _staffRepository.CreateStaffAsync(request, passwordHash, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = staff.Id }, staff);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateStaffRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var staff = await _staffRepository.UpdateStaffAsync(id, request, cancellationToken);
        if (staff == null)
            return NotFound();

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deactivated = await _staffRepository.DeactivateStaffAsync(id, cancellationToken);
        if (!deactivated)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{id:guid}/roles")]
    public async Task<IActionResult> AssignRole(
        Guid id,
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RoleId == Guid.Empty)
            return BadRequest("A valid role ID is required.");

        var success = await _roleRepository.AssignRoleToStaffAsync(id, request.RoleId, cancellationToken);
        if (!success)
            return NotFound("Staff member or role not found.");

        return NoContent();
    }
}
