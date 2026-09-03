using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediCore.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IStaffRepository _staffRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IRefreshTokenRepository _refreshTokenRepository;

    public AuthController(
        IStaffRepository staffRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IRefreshTokenRepository refreshTokenRepository)
    {
        _staffRepository = staffRepository;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _refreshTokenRepository = refreshTokenRepository;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _staffRepository.GetUserByEmailAsync(request.Email, cancellationToken);
        if (user == null || user.IsDeleted)
        {
            return Unauthorized();
        }

        var isPasswordValid = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            return Unauthorized();
        }

        var accessToken = _tokenGenerator.GenerateAccessToken(user);
        var refreshTokenString = _tokenGenerator.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenString,
            UserId = user.Id,
            Expires = DateTime.UtcNow.AddDays(7),
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };

        await _refreshTokenRepository.AddAsync(refreshToken, cancellationToken);

        var response = new AuthResponse(
            accessToken,
            refreshTokenString,
            new UserDto(user.Id, user.Email, user.Role, $"{user.StaffProfile?.FirstName} {user.StaffProfile?.LastName}".Trim())
        );

        return Ok(response);
    }
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var refreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken, cancellationToken);
        if (refreshToken == null || !refreshToken.IsActive || refreshToken.User == null || refreshToken.User.IsDeleted)
        {
            return Unauthorized();
        }

        // Revoke the old token
        refreshToken.Revoked = DateTime.UtcNow;
        refreshToken.RevokedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _refreshTokenRepository.UpdateAsync(refreshToken, cancellationToken);

        // Generate new tokens
        var user = refreshToken.User;
        var newAccessToken = _tokenGenerator.GenerateAccessToken(user);
        var newRefreshTokenString = _tokenGenerator.GenerateRefreshToken();

        var newRefreshToken = new RefreshToken
        {
            Token = newRefreshTokenString,
            UserId = user.Id,
            Expires = DateTime.UtcNow.AddDays(7),
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };
        await _refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);

        var response = new AuthResponse(
            newAccessToken,
            newRefreshTokenString,
            new UserDto(user.Id, user.Email, user.Role, $"{user.StaffProfile?.FirstName} {user.StaffProfile?.LastName}".Trim())
        );

        return Ok(response);
    }

    [HttpPost("logout")]
    [AllowAnonymous] // Allow even if token is expired, so they can cleanly log out
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var refreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken, cancellationToken);
        
        if (refreshToken != null && refreshToken.IsActive)
        {
            refreshToken.Revoked = DateTime.UtcNow;
            refreshToken.RevokedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _refreshTokenRepository.UpdateAsync(refreshToken, cancellationToken);
        }

        return NoContent();
    }
}
