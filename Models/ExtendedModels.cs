using MongoDB.Bson.Serialization.Attributes;

namespace DotnetRoutixServer.Models;

public sealed class PaymentData
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public double WalletBalance { get; set; } = 0;
    public bool PayLaterEnabled { get; set; } = false;
    public double PayLaterUsed { get; set; } = 0;
    public List<object> LinkedAccounts { get; set; } = [];
    public List<object> UpiIds { get; set; } = [];
    public List<object> WalletTxns { get; set; } = [];
    public List<object> PayHistory { get; set; } = [];
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

public sealed class AppFeedback
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FeedbackType { get; set; } = string.Empty;
    public string FeedbackLabel { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public double? Rating { get; set; }
    public string? Note { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string SubmittedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
}

public sealed class KycRecord
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Status { get; set; } = "not_started"; // not_started | pending | verified | rejected
    public string? DocumentType { get; set; }
    public string? ReferenceId { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CaptainFeedback
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string BookingId { get; set; } = string.Empty;
    public string CaptainUserId { get; set; } = string.Empty;
    public string CaptainName { get; set; } = string.Empty;
    public string SubmittedByUserId { get; set; } = string.Empty;
    public string SubmittedByName { get; set; } = string.Empty;
    public double RideRating { get; set; }
    public double CaptainRating { get; set; }
    public string? FeedbackText { get; set; }
    public bool LovedRide { get; set; }
    public bool LovedCaptain { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class UserPreferences
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PushSubscriptionRecord
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
