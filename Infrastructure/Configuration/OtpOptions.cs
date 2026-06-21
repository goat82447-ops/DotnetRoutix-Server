namespace DotnetRoutix.Server.Infrastructure.Configuration;

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    public bool DebugMode { get; set; } = true;

    public int ExpiryMinutes { get; set; } = 10;

    public bool RequireOtpOnLogin { get; set; } = false;
}
