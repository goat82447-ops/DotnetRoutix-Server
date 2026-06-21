namespace DotnetRoutix.Server.Application.Contracts;

public interface IEmailSender
{
    Task<bool> SendOtpEmailAsync(string toEmail, string displayName, string otpCode);
}
