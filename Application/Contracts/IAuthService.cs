using DotnetRoutix.Server.Application.DTOs;

namespace DotnetRoutix.Server.Application.Contracts;

public interface IAuthService
{
    Task<DemoUserResponse?> GetDemoUserAsync();

    Task<LoginResponse?> LoginAsync(LoginRequest request);

    Task<PinVerificationResponse?> VerifyPinAsync(VerifyPinRequest request);

    Task<LoginStartResponse> LoginStartAsync(LoginStartRequest request);

    Task<RegisterResponse> RegisterAsync(RegisterRequest request);

    Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request);
}
