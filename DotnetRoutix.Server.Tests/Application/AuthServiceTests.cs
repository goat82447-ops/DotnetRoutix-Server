using DotnetRoutix.Server.Application.Contracts;
using DotnetRoutix.Server.Application.DTOs;
using DotnetRoutix.Server.Application.Services;
using DotnetRoutix.Server.Domain.Entities;
using DotnetRoutix.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DotnetRoutix.Server.Tests.Application;

public sealed class AuthServiceTests
{
    private static AuthService CreateService(Mock<IAuthRepository> repositoryMock)
    {
        var emailSenderMock = new Mock<IEmailSender>();
        var otpOptions = Options.Create(new OtpOptions
        {
            DebugMode = true,
            ExpiryMinutes = 10,
            RequireOtpOnLogin = false
        });

        return new AuthService(repositoryMock.Object, emailSenderMock.Object, otpOptions);
    }

    [Fact]
    public async Task GetDemoUserAsync_ReturnsMappedResponse_WhenUserExists()
    {
        var repositoryMock = new Mock<IAuthRepository>();
        repositoryMock
            .Setup(repo => repo.GetFirstUserAsync())
            .ReturnsAsync(new UserAccount
            {
                Id = 42,
                FullName = "Demo User",
                Email = "demo@lunchbox.local",
                MobileNumber = "+91 90000 00001",
                DefaultPickupAddress = "Hyderabad"
            });

        var service = CreateService(repositoryMock);

        var result = await service.GetDemoUserAsync();

        Assert.NotNull(result);
        Assert.Equal(42, result.Id);
        Assert.Equal("Demo User", result.FullName);
        Assert.Equal("demo@lunchbox.local", result.Email);
    }

    [Fact]
    public async Task LoginAsync_ReturnsNull_WhenCredentialsAreInvalid()
    {
        var repositoryMock = new Mock<IAuthRepository>();
        repositoryMock
            .Setup(repo => repo.FindUserByCredentialsAsync("bad@user.com", "bad-pass"))
            .ReturnsAsync((UserAccount?)null);

        var service = CreateService(repositoryMock);

        var result = await service.LoginAsync(new LoginRequest("bad@user.com", "bad-pass"));

        Assert.Null(result);
        repositoryMock.Verify(repo => repo.ReplaceUserAsync(It.IsAny<UserAccount>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_UpdatesAuditFieldsAndReturnsResponse_WhenCredentialsAreValid()
    {
        var account = new UserAccount
        {
            Id = 1,
            FullName = "Aarav Sharma",
            Email = "demo@lunchbox.local",
            Password = "LunchBox@123",
            MobileNumber = "+91 98765 43210",
            DefaultPickupAddress = "Market Street",
            SecurityPin = "4821"
        };

        var repositoryMock = new Mock<IAuthRepository>();
        repositoryMock
            .Setup(repo => repo.FindUserByCredentialsAsync(account.Email, account.Password))
            .ReturnsAsync(account);

        var service = CreateService(repositoryMock);

        var result = await service.LoginAsync(new LoginRequest(account.Email, account.Password));

        Assert.NotNull(result);
        Assert.Equal(account.Id, result.Id);
        Assert.Equal("4821", result.SecurityPin);
        Assert.NotNull(account.LastLoginAtUtc);
        Assert.Equal(account.SecurityPin, account.LastIssuedPin);
        repositoryMock.Verify(repo => repo.ReplaceUserAsync(account), Times.Once);
    }

    [Fact]
    public async Task VerifyPinAsync_ReturnsNull_WhenUserIsMissing()
    {
        var repositoryMock = new Mock<IAuthRepository>();
        repositoryMock
            .Setup(repo => repo.FindUserByIdAsync(99))
            .ReturnsAsync((UserAccount?)null);

        var service = CreateService(repositoryMock);

        var result = await service.VerifyPinAsync(new VerifyPinRequest(99, "1111"));

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyPinAsync_ReturnsInvalid_WhenPinDoesNotMatch()
    {
        var repositoryMock = new Mock<IAuthRepository>();
        repositoryMock
            .Setup(repo => repo.FindUserByIdAsync(5))
            .ReturnsAsync(new UserAccount { Id = 5, SecurityPin = "4821" });

        var service = CreateService(repositoryMock);

        var result = await service.VerifyPinAsync(new VerifyPinRequest(5, "9999"));

        Assert.NotNull(result);
        Assert.False(result.IsValid);
    }
}
