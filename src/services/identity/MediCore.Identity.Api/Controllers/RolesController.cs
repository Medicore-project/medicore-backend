using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Identity.Api.Controllers;

/// <summary>
/// The role definitions a staff member can be granted.
/// </summary>
/// <remarks>
/// Admin-only: the sole caller is the role-assignment screen, which is itself Admin's. Nothing
/// else needs the catalogue — a user's own role travels in their token.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class RolesController : ControllerBase
{
    private readonly IRoleRepository _roleRepository;

    public RolesController(IRoleRepository roleRepository)
    {
        _roleRepository = roleRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var roles = await _roleRepository.GetAllAsync(cancellationToken);
        return Ok(roles);
    }
}
