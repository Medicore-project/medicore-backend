using MediCore.Identity.Application.Entities;

namespace MediCore.Identity.Application.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
}
