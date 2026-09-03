using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
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
