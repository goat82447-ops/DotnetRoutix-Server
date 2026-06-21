using DotnetRoutix.Server.Application.Contracts;
using DotnetRoutix.Server.Application.DTOs;
using DotnetRoutix.Server.Domain.Entities;
using DotnetRoutix.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DotnetRoutix.Server.Application.Services;

public sealed class AuthService : IAuthService
{
    private readonly IAuthRepository _authRepository;
    private readonly IEmailSender _emailSender;
    private readonly OtpOptions _otpOptions;

    public AuthService(IAuthRepository authRepository, IEmailSender emailSender, IOptions<OtpOptions> otpOptions)
    {
        _authRepository = authRepository;
        _emailSender = emailSender;
        _otpOptions = otpOptions.Value;
    }

    public async Task<DemoUserResponse?> GetDemoUserAsync()
    {
        var account = await _authRepository.GetFirstUserAsync();
        if (account is null)
        {
            return null;
        }

        return new DemoUserResponse(
            account.Id,
            account.FullName,
            account.Email,
            account.MobileNumber,
            account.DefaultPickupAddress);
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var account = await _authRepository.FindUserByCredentialsAsync(request.Email, request.Password);

        if (account is null)
        {
            return null;
        }

        account.LastLoginAtUtc = DateTime.UtcNow;
        account.LastIssuedPin = account.SecurityPin;
        await _authRepository.ReplaceUserAsync(account);

        return new LoginResponse(
            account.Id,
            account.FullName,
            account.Email,
            account.MobileNumber,
            account.DefaultPickupAddress,
            account.SecurityPin,
            "Login successful. Use the demo PIN to verify the parcel handoff.");
    }

    public async Task<PinVerificationResponse?> VerifyPinAsync(VerifyPinRequest request)
    {
        var account = await _authRepository.FindUserByIdAsync(request.UserId);
        if (account is null)
        {
            return null;
        }

        var isValid = string.Equals(account.SecurityPin, request.Pin, StringComparison.Ordinal);

        return new PinVerificationResponse(
            account.Id,
            isValid,
            isValid
                ? "PIN verified. Driver can release the parcel."
                : "Incorrect PIN. Please try again.");
    }

    public async Task<LoginStartResponse> LoginStartAsync(LoginStartRequest request)
    {
        var usernameOrEmail = request.Username?.Trim() ?? string.Empty;
        var password = request.Password?.Trim() ?? string.Empty;

        var user = await _authRepository.FindUserByUsernameAndPasswordAsync(usernameOrEmail, password);
        user ??= await _authRepository.FindUserByCredentialsAsync(usernameOrEmail, password);

        if (user is null)
        {
            throw new UnauthorizedAccessException("Invalid username or password");
        }

        if (_otpOptions.RequireOtpOnLogin)
        {
            return await IssueOtpForUserAsync(user, "OTP sent to your email");
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _authRepository.ReplaceUserAsync(user);

        var appUser = new AppUserDto(
            Id: user.Id.ToString(),
            Username: string.IsNullOrWhiteSpace(user.Username) ? user.Email : user.Username,
            DisplayName: string.IsNullOrWhiteSpace(user.DisplayName) ? user.FullName : user.DisplayName,
            Role: string.IsNullOrWhiteSpace(user.Role) ? "customer" : user.Role.ToLowerInvariant(),
            Email: user.Email,
            Mobile: ResolveMobile(user),
            ProfileImageUrl: user.ProfileImageUrl);

        return new LoginStartResponse(
            RequiresOtp: false,
            TempToken: string.Empty,
            Message: "Login successful",
            User: appUser,
            SessionToken: Guid.NewGuid().ToString(),
                Channels: new LoginChannels(user.Email, ResolveMobile(user)));
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
    {
        var existingUser = await _authRepository.FindUserByUsernameAsync(request.Username);
        if (existingUser is not null)
        {
            throw new InvalidOperationException("Username already exists");
        }

        var newUser = new UserAccount
        {
            Username = request.Username,
            DisplayName = request.DisplayName,
            Email = request.Email,
            Mobile = request.Mobile,
            Password = request.Password,
            Role = request.Role,
            FullName = request.DisplayName,
            MobileNumber = request.Mobile,
            SecurityPin = GeneratePin()
        };

        var tempToken = Guid.NewGuid().ToString();
        var emailOtp = GenerateOtp();

        newUser.TempOtpToken = tempToken;
        newUser.TempEmailOtp = emailOtp;
        newUser.TempOtpExpiryUtc = DateTime.UtcNow.AddMinutes(_otpOptions.ExpiryMinutes);

        var createdUser = await _authRepository.CreateUserAsync(newUser);
        if (createdUser is null)
        {
            throw new InvalidOperationException("Failed to create user account.");
        }

        var emailSent = await _emailSender.SendOtpEmailAsync(
            createdUser.Email,
            string.IsNullOrWhiteSpace(createdUser.DisplayName) ? createdUser.FullName : createdUser.DisplayName,
            emailOtp);

        var responseMessage = emailSent
            ? "Registration successful. OTP sent to your email."
            : "Registration successful. OTP generated, but email delivery failed. Use debug OTP.";

        return new RegisterResponse(
            Message: responseMessage,
            RequiresOtp: true,
            TempToken: tempToken,
            Channels: new LoginChannels(createdUser.Email, ResolveMobile(createdUser)),
            DevOtps: _otpOptions.DebugMode ? new DevOtps(emailOtp, emailOtp) : null);
    }

    public async Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TempToken))
        {
            throw new InvalidOperationException("Temp token is required.");
        }

        var user = await _authRepository.FindUserByTempOtpTokenAsync(request.TempToken);
        if (user is null)
        {
            throw new InvalidOperationException("Invalid or expired OTP session.");
        }

        if (user.TempOtpExpiryUtc is null || user.TempOtpExpiryUtc.Value < DateTime.UtcNow)
        {
            throw new InvalidOperationException("OTP has expired.");
        }

        var providedOtp = request.EmailOtp?.Trim() ?? string.Empty;
        var expectedOtp = user.TempEmailOtp?.Trim() ?? string.Empty;
        if (!string.Equals(providedOtp, expectedOtp, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid OTP code.");
        }

        user.EmailVerified = true;
        user.TempOtpToken = null;
        user.TempEmailOtp = null;
        user.TempOtpExpiryUtc = null;
        user.LastLoginAtUtc = DateTime.UtcNow;

        await _authRepository.ReplaceUserAsync(user);

        var appUser = MapToAppUser(user);

        return new VerifyOtpResponse(
            SessionToken: Guid.NewGuid().ToString(),
            User: appUser,
            Message: "OTP verified successfully");
    }

    private async Task<LoginStartResponse> IssueOtpForUserAsync(UserAccount user, string message)
    {
        var tempToken = Guid.NewGuid().ToString();
        var emailOtp = GenerateOtp();

        user.TempOtpToken = tempToken;
        user.TempEmailOtp = emailOtp;
        user.TempOtpExpiryUtc = DateTime.UtcNow.AddMinutes(_otpOptions.ExpiryMinutes);
        await _authRepository.ReplaceUserAsync(user);

        var emailSent = await _emailSender.SendOtpEmailAsync(
            user.Email,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.FullName : user.DisplayName,
            emailOtp);

        var responseMessage = emailSent
            ? message
            : "OTP generated, but email delivery failed. Use debug OTP.";

        return new LoginStartResponse(
            RequiresOtp: true,
            TempToken: tempToken,
            Message: responseMessage,
            Channels: new LoginChannels(user.Email, ResolveMobile(user)),
            DevOtps: _otpOptions.DebugMode ? new DevOtps(emailOtp, emailOtp) : null);
    }

    private static AppUserDto MapToAppUser(UserAccount user)
        => new(
            Id: user.Id.ToString(),
            Username: string.IsNullOrWhiteSpace(user.Username) ? user.Email : user.Username,
            DisplayName: string.IsNullOrWhiteSpace(user.DisplayName) ? user.FullName : user.DisplayName,
            Role: string.IsNullOrWhiteSpace(user.Role) ? "customer" : user.Role.ToLowerInvariant(),
            Email: user.Email,
            Mobile: ResolveMobile(user),
            ProfileImageUrl: user.ProfileImageUrl);

    private static string ResolveMobile(UserAccount user)
        => !string.IsNullOrWhiteSpace(user.Mobile) ? user.Mobile : user.MobileNumber;

    private static string GenerateOtp() => new Random().Next(100000, 999999).ToString();

    private static string GeneratePin() => new Random().Next(1000, 9999).ToString();
}
