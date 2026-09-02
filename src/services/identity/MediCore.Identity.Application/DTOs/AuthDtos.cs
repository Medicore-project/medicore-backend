namespace MediCore.Identity.Application.DTOs;

public record LoginRequest(string Email, string Password);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    UserDto User
);

public record UserDto(
    Guid Id,
    string Email,
    string Role,
    string Name
);

public record RefreshTokenRequest(string RefreshToken);
