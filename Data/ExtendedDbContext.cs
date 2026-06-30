using DotnetRoutixServer.Models;
using MongoDB.Driver;

namespace DotnetRoutixServer.Data;

/// <summary>Extension methods for extra collections (payment, kyc, feedback, etc.)</summary>
public sealed class ExtendedDbContext
{
    private readonly IMongoCollection<PaymentData> _payments;
    private readonly IMongoCollection<AppFeedback> _feedback;
    private readonly IMongoCollection<KycRecord> _kyc;
    private readonly IMongoCollection<CaptainFeedback> _captainFeedback;
    private readonly IMongoCollection<UserPreferences> _preferences;
    private readonly IMongoCollection<PushSubscriptionRecord> _pushSubscriptions;

    public ExtendedDbContext(IConfiguration config)
    {
        var connStr = config["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
        var dbName  = config["MongoDb:DatabaseName"]    ?? "lunchbox_db";
        var db      = new MongoClient(connStr).GetDatabase(dbName);

        _payments       = db.GetCollection<PaymentData>("payment_data");
        _feedback       = db.GetCollection<AppFeedback>("app_feedback");
        _kyc            = db.GetCollection<KycRecord>("kyc_records");
        _captainFeedback= db.GetCollection<CaptainFeedback>("captain_feedback");
        _preferences    = db.GetCollection<UserPreferences>("user_preferences");
        _pushSubscriptions = db.GetCollection<PushSubscriptionRecord>("push_subscriptions");

        _payments.Indexes.CreateOne(new CreateIndexModel<PaymentData>(
            Builders<PaymentData>.IndexKeys.Ascending(p => p.UserId),
            new CreateIndexOptions { Unique = true }));
        _preferences.Indexes.CreateOne(new CreateIndexModel<UserPreferences>(
            Builders<UserPreferences>.IndexKeys.Ascending(p => p.UserId),
            new CreateIndexOptions { Unique = true }));
        _pushSubscriptions.Indexes.CreateOne(new CreateIndexModel<PushSubscriptionRecord>(
            Builders<PushSubscriptionRecord>.IndexKeys.Combine(
                Builders<PushSubscriptionRecord>.IndexKeys.Ascending(p => p.UserId),
                Builders<PushSubscriptionRecord>.IndexKeys.Ascending(p => p.Endpoint)
            ),
            new CreateIndexOptions { Unique = true }));
    }

    // ── Payment ──────────────────────────────────────────────────

    public async Task<PaymentData> GetOrCreatePaymentAsync(string userId)
    {
        var doc = await _payments.Find(p => p.UserId == userId).FirstOrDefaultAsync();
        if (doc is not null) return doc;
        doc = new PaymentData { UserId = userId };
        await _payments.InsertOneAsync(doc);
        return doc;
    }

    public Task ReplacePaymentAsync(PaymentData data)
        => _payments.ReplaceOneAsync(p => p.UserId == data.UserId, data, new ReplaceOptions { IsUpsert = true });

    // ── App Feedback ──────────────────────────────────────────────

    public Task InsertFeedbackAsync(AppFeedback fb)
        => _feedback.InsertOneAsync(fb);

    // ── KYC ────────────────────────────────────────────────────────

    public async Task<KycRecord> GetOrCreateKycAsync(string userId)
    {
        var rec = await _kyc.Find(k => k.UserId == userId).FirstOrDefaultAsync();
        if (rec is not null) return rec;
        rec = new KycRecord { UserId = userId };
        await _kyc.InsertOneAsync(rec);
        return rec;
    }

    public Task ReplaceKycAsync(KycRecord rec)
        => _kyc.ReplaceOneAsync(k => k.UserId == rec.UserId, rec, new ReplaceOptions { IsUpsert = true });

    // ── User Preferences ──────────────────────────────────────────

    public async Task<UserPreferences> GetOrCreatePreferencesAsync(string userId)
    {
        var rec = await _preferences.Find(p => p.UserId == userId).FirstOrDefaultAsync();
        if (rec is not null) return rec;
        rec = new UserPreferences { UserId = userId, DataJson = "{}", UpdatedAt = DateTime.UtcNow };
        await _preferences.InsertOneAsync(rec);
        return rec;
    }

    public Task ReplacePreferencesAsync(UserPreferences rec)
        => _preferences.ReplaceOneAsync(
            p => p.UserId == rec.UserId,
            rec,
            new ReplaceOptions { IsUpsert = true });

    // ── Push subscriptions ───────────────────────────────────────

    public Task SavePushSubscriptionAsync(PushSubscriptionRecord rec)
        => _pushSubscriptions.ReplaceOneAsync(
            p => p.UserId == rec.UserId && p.Endpoint == rec.Endpoint,
            rec,
            new ReplaceOptions { IsUpsert = true });

    public Task RemovePushSubscriptionAsync(string userId, string endpoint)
        => _pushSubscriptions.DeleteOneAsync(
            p => p.UserId == userId && p.Endpoint == endpoint);

    public Task RemovePushSubscriptionByEndpointAsync(string endpoint)
        => _pushSubscriptions.DeleteOneAsync(p => p.Endpoint == endpoint);

    public Task<List<PushSubscriptionRecord>> GetPushSubscriptionsByUserIdsAsync(IEnumerable<string> userIds)
        => _pushSubscriptions.Find(p => userIds.Contains(p.UserId)).ToListAsync();

    // ── Captain Feedback ───────────────────────────────────────────

    public Task InsertCaptainFeedbackAsync(CaptainFeedback fb)
        => _captainFeedback.InsertOneAsync(fb);

    public Task<List<CaptainFeedback>> GetCaptainFeedbackAsync(string captainId)
        => _captainFeedback.Find(f => f.CaptainUserId == captainId).ToListAsync();

    public async Task<object> GetCaptainFeedbackStatsAsync(string captainId)
    {
        var all = await GetCaptainFeedbackAsync(captainId);
        if (!all.Any()) return new { avgCaptainRating = 0.0, avgRideRating = 0.0, totalHearts = 0, feedbackCount = 0, recentComments = Array.Empty<object>() };
        return new
        {
            avgCaptainRating = all.Average(f => f.CaptainRating),
            avgRideRating    = all.Average(f => f.RideRating),
            totalHearts      = all.Count(f => f.LovedCaptain),
            feedbackCount    = all.Count,
            recentComments   = all.OrderByDescending(f => f.CreatedAt).Take(5).Select(f => new
            {
                bookingId    = f.BookingId,
                userName     = f.SubmittedByName,
                rideRating   = f.RideRating,
                captainRating= f.CaptainRating,
                feedbackText = f.FeedbackText,
                lovedRide    = f.LovedRide,
                lovedCaptain = f.LovedCaptain,
                createdAt    = f.CreatedAt
            }).ToArray()
        };
    }
}
