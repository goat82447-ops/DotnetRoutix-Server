using MongoDB.Bson.Serialization.Attributes;

namespace DotnetRoutix.Server.Domain.Entities;

public sealed class UserAccount
{
    [BsonId]
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Mobile { get; set; } = string.Empty;

    public string Role { get; set; } = "customer";

    public string FullName { get; set; } = string.Empty;

    public string MobileNumber { get; set; } = string.Empty;

    public string DefaultPickupAddress { get; set; } = string.Empty;

    public string SecurityPin { get; set; } = string.Empty;

    public string LastIssuedPin { get; set; } = string.Empty;

    public DateTime? LastLoginAtUtc { get; set; }

    public string? ProfileImageUrl { get; set; }

    public string? TempOtpToken { get; set; }

    public string? TempEmailOtp { get; set; }

    public DateTime? TempOtpExpiryUtc { get; set; }

    public bool EmailVerified { get; set; }

    public bool MobileVerified { get; set; }
}
