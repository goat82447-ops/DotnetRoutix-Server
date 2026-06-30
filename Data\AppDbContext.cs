using DotnetRoutixServer.Models;
using MongoDB.Driver;

namespace DotnetRoutixServer.Data;

public sealed class AppDbContext
{
    private readonly IMongoCollection<UserAccount> _users;
    private readonly IMongoCollection<AuthSession> _sessions;
    private readonly IMongoCollection<OtpCode> _otpCodes;
    private readonly IMongoCollection<RideBooking> _bookings;
    private readonly IMongoCollection<SupportComplaint> _complaints;

    public AppDbContext(IConfiguration config)
    {
        var connStr = config["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017";
        var dbName  = config["MongoDb:DatabaseName"]    ?? "lunchbox_db";

        var client   = new MongoClient(connStr);
        var database = client.GetDatabase(dbName);

        _users      = database.GetCollection<UserAccount>("users");
        _sessions   = database.GetCollection<AuthSession>("auth_sessions");
        _otpCodes   = database.GetCollection<OtpCode>("otp_codes");
        _bookings   = database.GetCollection<RideBooking>("bookings");
        _complaints = database.GetCollection<SupportComplaint>("support_complaints");

        // Unique index on username + email
        _users.Indexes.CreateOne(new CreateIndexModel<UserAccount>(
            Builders<UserAccount>.IndexKeys.Ascending(u => u.Username),
            new CreateIndexOptions { Unique = true, Sparse = true }));
        _users.Indexes.CreateOne(new CreateIndexModel<UserAccount>(
            Builders<UserAccount>.IndexKeys.Ascending(u => u.Email),
            new CreateIndexOptions { Unique = true }));
    }

    // ── Users ──────────────────────────────────────────────────

    public Task<List<UserAccount>> GetAllUsersAsync()
        => _users.Find(Builders<UserAccount>.Filter.Empty).ToListAsync();

    public Task<UserAccount?> FindByIdAsync(string id)
        => _users.Find(u => u.Id == id).FirstOrDefaultAsync()!;

    public Task<UserAccount?> FindByUsernameAsync(string username)
        => _users.Find(u => u.Username == username).FirstOrDefaultAsync()!;

    public Task<UserAccount?> FindByEmailAsync(string email)
        => _users.Find(u => u.Email == email).FirstOrDefaultAsync()!;

    public async Task<bool> UserExistsAsync(string username, string email, string mobile)
    {
        var filter = Builders<UserAccount>.Filter.Or(
            Builders<UserAccount>.Filter.Eq(u => u.Username, username),
            Builders<UserAccount>.Filter.Eq(u => u.Email,    email),
            Builders<UserAccount>.Filter.Eq(u => u.Mobile,   mobile));
        return await _users.Find(filter).AnyAsync();
    }

    public Task InsertUserAsync(UserAccount user)
        => _users.InsertOneAsync(user);

    public Task ReplaceUserAsync(UserAccount user)
        => _users.ReplaceOneAsync(u => u.Id == user.Id, user);

    public Task DeleteUserAsync(string id)
        => _users.DeleteOneAsync(u => u.Id == id);

    public Task<List<UserAccount>> GetCaptainsAsync(string? vehicleType = null)
    {
        var filter = Builders<UserAccount>.Filter.Eq(u => u.Role, "captain");
        if (!string.IsNullOrEmpty(vehicleType))
            filter &= Builders<UserAccount>.Filter.Eq(u => u.CaptainVehicle, vehicleType);
        return _users.Find(filter).ToListAsync();
    }

    // ── Sessions ───────────────────────────────────────────────

    public Task<AuthSession?> GetSessionAsync(string token)
        => _sessions.Find(s => s.Token == token && s.ExpiresAt > DateTime.UtcNow)
                    .FirstOrDefaultAsync()!;

    public Task InsertSessionAsync(AuthSession session)
        => _sessions.InsertOneAsync(session);

    public Task DeleteSessionAsync(string token)
        => _sessions.DeleteOneAsync(s => s.Token == token);

    public Task DeleteAllUserSessionsAsync(string userId)
        => _sessions.DeleteManyAsync(s => s.UserId == userId);

    // ── OTP ────────────────────────────────────────────────────

    public Task<OtpCode?> GetActiveOtpAsync(string sessionToken, string channel)
        => _otpCodes.Find(o =>
            o.SessionToken == sessionToken &&
            o.Channel      == channel &&
            o.Consumed     == 0 &&
            o.ExpiresAt    > DateTime.UtcNow).FirstOrDefaultAsync()!;

    public Task InsertOtpAsync(OtpCode otp)
        => _otpCodes.InsertOneAsync(otp);

    public Task ConsumeOtpsAsync(string sessionToken)
        => _otpCodes.UpdateManyAsync(
            o => o.SessionToken == sessionToken,
            Builders<OtpCode>.Update.Set(o => o.Consumed, 1));

    public Task DeleteOtpsBySessionAsync(string sessionToken)
        => _otpCodes.DeleteManyAsync(o => o.SessionToken == sessionToken);

    // ── Bookings ───────────────────────────────────────────────

    public Task<List<RideBooking>> GetBookingsAsync(string? userId = null, string? captainId = null)
    {
        var filter = Builders<RideBooking>.Filter.Empty;
        if (!string.IsNullOrEmpty(userId))
        {
            filter = Builders<RideBooking>.Filter.Eq(b => b.UserId, userId);
        }
        else if (!string.IsNullOrEmpty(captainId))
        {
            var assignedToCaptain = Builders<RideBooking>.Filter.Eq(b => b.CaptainId, captainId);
            var openForAllCaptains = Builders<RideBooking>.Filter.And(
                Builders<RideBooking>.Filter.Eq(b => b.Status, "created"),
                Builders<RideBooking>.Filter.Or(
                    Builders<RideBooking>.Filter.Eq(b => b.NotificationTarget, "all"),
                    Builders<RideBooking>.Filter.Eq(b => b.NotificationTarget, string.Empty),
                    Builders<RideBooking>.Filter.Exists(b => b.NotificationTarget, false)
                )
            );
            filter = Builders<RideBooking>.Filter.Or(assignedToCaptain, openForAllCaptains);
        }
        return _bookings.Find(filter).SortByDescending(b => b.UpdatedAt).ToListAsync();
    }

    public Task<RideBooking?> GetBookingByIdAsync(string id)
        => _bookings.Find(b => b.Id == id).FirstOrDefaultAsync()!;

    public Task InsertBookingAsync(RideBooking booking)
        => _bookings.InsertOneAsync(booking);

    public Task ReplaceBookingAsync(RideBooking booking)
        => _bookings.ReplaceOneAsync(b => b.Id == booking.Id, booking);

    // ── Support ────────────────────────────────────────────────

    public Task InsertComplaintAsync(SupportComplaint c)
        => _complaints.InsertOneAsync(c);

    public Task<List<SupportComplaint>> GetComplaintsAsync(string? userId = null)
    {
        var filter = string.IsNullOrEmpty(userId)
            ? Builders<SupportComplaint>.Filter.Empty
            : Builders<SupportComplaint>.Filter.Eq(c => c.UserId, userId);
        return _complaints.Find(filter).SortByDescending(c => c.CreatedAt).ToListAsync();
    }
}
