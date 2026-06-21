namespace DotnetRoutix.Server.Application.DTOs;

// Legacy DTOs (kept for backward compatibility)
public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(
    int Id,
    string FullName,
    string Email,
    string MobileNumber,
    string DefaultPickupAddress,
    string SecurityPin,
    string Message);

public sealed record VerifyPinRequest(int UserId, string Pin);

public sealed record PinVerificationResponse(
    int UserId,
    bool IsValid,
    string Message);

public sealed record DemoUserResponse(
    int Id,
    string FullName,
    string Email,
    string MobileNumber,
    string DefaultPickupAddress);

// Frontend-compatible DTOs
public sealed record AppUserDto(
    string Id,
    string Username,
    string DisplayName,
    string Role,
    string? Email = null,
    string? Mobile = null,
    string? ProfileImageUrl = null);

public sealed record LoginStartRequest(string Username, string Password, string Role);

public sealed record LoginStartResponse(
    bool RequiresOtp,
    string TempToken,
    string Message,
    AppUserDto? User = null,
    string? SessionToken = null,
    LoginChannels? Channels = null,
    DevOtps? DevOtps = null);

public sealed record LoginChannels(string Email, string? Mobile = null);

public sealed record DevOtps(string EmailOtp, string? MobileOtp = null);

public sealed record RegisterRequest(
    string Username,
    string DisplayName,
    string Email,
    string Mobile,
    string Password,
    string Role);

public sealed record RegisterResponse(
    string Message,
    bool? RequiresOtp = null,
    string? TempToken = null,
    LoginChannels? Channels = null,
    DevOtps? DevOtps = null);

public sealed record VerifyOtpRequest(string TempToken, string EmailOtp, string? MobileOtp = null);

public sealed record VerifyOtpResponse(
    string SessionToken,
    AppUserDto User,
    string Message);
