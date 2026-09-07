using MediCore.Identity.Api.Controllers;
using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace MediCore.Identity.Tests.Unit;

public sealed class AuthControllerTests
{
    private static AuthController CreateController(
        Mock<IStaffRepository> staffRepository,
        Mock<IPasswordHasher> passwordHasher,
        Mock<IRefreshTokenRepository> refreshTokenRepository,
        Mock<IJwtTokenGenerator>? tokenGenerator = null)
    {
        var controller = new AuthController(
            staffRepository.Object,
            passwordHasher.Object,
            (tokenGenerator ?? DefaultTokenGenerator()).Object,
            refreshTokenRepository.Object);

        // AuthController reads HttpContext.Connection.RemoteIpAddress directly.
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static Mock<IJwtTokenGenerator> DefaultTokenGenerator()
    {
        var mock = new Mock<IJwtTokenGenerator>();
        mock.Setup(g => g.GenerateAccessToken(It.IsAny<User>())).Returns("fake-access-token");
        mock.Setup(g => g.GenerateRefreshToken()).Returns("fake-refresh-token");
        return mock;
    }

    private static User ActiveUser(string password, Mock<IPasswordHasher> passwordHasher) => new()
    {
        Id = Guid.NewGuid(),
        Email = "admin@medicore.local",
        PasswordHash = "stored-hash-for-" + password,
        Role = "Admin",
        IsActive = true,
        IsDeleted = false,
    };

    [Fact]
    public async Task Login_with_an_email_that_does_not_exist_returns_401()
    {
        var staffRepository = new Mock<IStaffRepository>();
        staffRepository
            .Setup(r => r.GetUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var passwordHasher = new Mock<IPasswordHasher>();
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();

        var controller = CreateController(staffRepository, passwordHasher, refreshTokenRepository);

        var result = await controller.Login(new LoginRequest("nobody@medicore.local", "whatever"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Login_with_the_wrong_password_returns_401()
    {
        var passwordHasher = new Mock<IPasswordHasher>();
        var user = ActiveUser("correct-password", passwordHasher);
        passwordHasher.Setup(h => h.VerifyPassword("wrong-password", user.PasswordHash)).Returns(false);

        var staffRepository = new Mock<IStaffRepository>();
        staffRepository
            .Setup(r => r.GetUserByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();

        var controller = CreateController(staffRepository, passwordHasher, refreshTokenRepository);

        var result = await controller.Login(new LoginRequest(user.Email, "wrong-password"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Login_failure_responses_are_identical_whether_the_email_or_the_password_was_wrong()
    {
        // AC: "Invalid credentials return 401 without revealing which field was wrong."
        var passwordHasher = new Mock<IPasswordHasher>();
        var user = ActiveUser("correct-password", passwordHasher);
        passwordHasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), user.PasswordHash)).Returns(false);

        var staffRepositoryKnownEmail = new Mock<IStaffRepository>();
        staffRepositoryKnownEmail
            .Setup(r => r.GetUserByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        var staffRepositoryUnknownEmail = new Mock<IStaffRepository>();
        staffRepositoryUnknownEmail
            .Setup(r => r.GetUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var wrongPasswordResult = await CreateController(staffRepositoryKnownEmail, passwordHasher, new Mock<IRefreshTokenRepository>())
            .Login(new LoginRequest(user.Email, "wrong-password"), CancellationToken.None);
        var unknownEmailResult = await CreateController(staffRepositoryUnknownEmail, passwordHasher, new Mock<IRefreshTokenRepository>())
            .Login(new LoginRequest("nobody@medicore.local", "wrong-password"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(wrongPasswordResult.Result);
        Assert.IsType<UnauthorizedResult>(unknownEmailResult.Result);
        Assert.Equal(
            ((UnauthorizedResult)wrongPasswordResult.Result!).StatusCode,
            ((UnauthorizedResult)unknownEmailResult.Result!).StatusCode);
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_an_access_token_and_a_refresh_token()
    {
        var passwordHasher = new Mock<IPasswordHasher>();
        var user = ActiveUser("correct-password", passwordHasher);
        passwordHasher.Setup(h => h.VerifyPassword("correct-password", user.PasswordHash)).Returns(true);

        var staffRepository = new Mock<IStaffRepository>();
        staffRepository
            .Setup(r => r.GetUserByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();

        var controller = CreateController(staffRepository, passwordHasher, refreshTokenRepository);

        var result = await controller.Login(new LoginRequest(user.Email, "correct-password"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AuthResponse>(ok.Value);
        Assert.Equal("fake-access-token", response.AccessToken);
        Assert.Equal("fake-refresh-token", response.RefreshToken);
        refreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refresh_with_a_valid_token_revokes_the_old_token_and_issues_a_new_one()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "admin@medicore.local", Role = "Admin" };
        var oldToken = new RefreshToken
        {
            Token = "old-refresh-token",
            UserId = user.Id,
            User = user,
            Expires = DateTime.UtcNow.AddDays(1),
        };

        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        refreshTokenRepository
            .Setup(r => r.GetByTokenAsync("old-refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldToken);

        var controller = CreateController(new Mock<IStaffRepository>(), new Mock<IPasswordHasher>(), refreshTokenRepository);

        var result = await controller.Refresh(new RefreshTokenRequest("old-refresh-token"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(oldToken.Revoked); // old token rotated out
        refreshTokenRepository.Verify(r => r.UpdateAsync(oldToken, It.IsAny<CancellationToken>()), Times.Once);
        refreshTokenRepository.Verify(
            r => r.AddAsync(It.Is<RefreshToken>(rt => rt.Token == "fake-refresh-token"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Refresh_with_an_already_revoked_token_returns_401()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "admin@medicore.local", Role = "Admin" };
        var revokedToken = new RefreshToken
        {
            Token = "used-up-token",
            UserId = user.Id,
            User = user,
            Expires = DateTime.UtcNow.AddDays(1),
            Revoked = DateTime.UtcNow.AddMinutes(-1),
        };

        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        refreshTokenRepository
            .Setup(r => r.GetByTokenAsync("used-up-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokedToken);

        var controller = CreateController(new Mock<IStaffRepository>(), new Mock<IPasswordHasher>(), refreshTokenRepository);

        var result = await controller.Refresh(new RefreshTokenRequest("used-up-token"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        refreshTokenRepository.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Logout_revokes_the_active_refresh_token()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "admin@medicore.local", Role = "Admin" };
        var activeToken = new RefreshToken
        {
            Token = "active-token",
            UserId = user.Id,
            User = user,
            Expires = DateTime.UtcNow.AddDays(1),
        };

        var refreshTokenRepository = new Mock<IRefreshTokenRepository>();
        refreshTokenRepository
            .Setup(r => r.GetByTokenAsync("active-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeToken);

        var controller = CreateController(new Mock<IStaffRepository>(), new Mock<IPasswordHasher>(), refreshTokenRepository);

        var result = await controller.Logout(new RefreshTokenRequest("active-token"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.NotNull(activeToken.Revoked);
        refreshTokenRepository.Verify(r => r.UpdateAsync(activeToken, It.IsAny<CancellationToken>()), Times.Once);
    }
}
