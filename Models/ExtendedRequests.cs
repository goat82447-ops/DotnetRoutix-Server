namespace DotnetRoutixServer.Models;

// ── Payment ───────────────────────────────────────────────────
public sealed record AddWalletRequest(double Amount);
public sealed record PatchPaymentRequest(
    double? WalletBalance, bool? PayLaterEnabled, double? PayLaterUsed);

// ── Promo ─────────────────────────────────────────────────────
public sealed record ValidatePromoRequest(string Code, double Amount);

// ── KYC ───────────────────────────────────────────────────────
public sealed record SubmitKycRequest(
    string DocumentType, string ReferenceId, string? Notes);

// ── Feedback ──────────────────────────────────────────────────
public sealed record SubmitAppFeedbackRequest(
    string FeedbackType, string FeedbackLabel, string AppVersion,
    string Route, string SubmittedAt, double? Rating, string? Note);

public sealed record SubmitCaptainFeedbackRequest(
    string BookingId, string? CaptainId, string CaptainName,
    double RideRating, double CaptainRating,
    string? FeedbackText, bool LovedRide, bool LovedCaptain);

// ── User Preferences ─────────────────────────────────────────────
public sealed record UpdateUserPreferencesRequest(string DataJson);
public sealed record SavePushSubscriptionRequest(string Endpoint, string P256dh, string Auth);
public sealed record RemovePushSubscriptionRequest(string Endpoint);

// ── Booking extras ────────────────────────────────────────────
public sealed record VerifyBookingOtpRequest(string Otp);
public sealed record CancelBookingRequest(string? Reason);
public sealed record PayBookingRequest(string PaymentMethod, double Amount);

// ── Pricing ───────────────────────────────────────────────────
public sealed record LiveFareRequest(GeoPoint? Pickup, GeoPoint? Drop, string? VehicleType);
