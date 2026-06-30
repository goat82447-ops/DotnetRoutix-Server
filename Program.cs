using DotnetRoutixServer.Data;
using DotnetRoutixServer.Models;
using DotnetRoutixServer.Services;
using System.Text.Json;
using WebPush;
using DotnetRoutix.Server.Application.Validators;
using DotnetRoutixServer.Data;
using DotnetRoutix.Server.Application.Contracts;
using DotnetRoutixServer.Models;
using DotnetRoutix.Server.Application.Services;
using DotnetRoutixServer.Services;
using DotnetRoutix.Server.Infrastructure.Configuration;
using System.Text.Json;
using DotnetRoutix.Server.Infrastructure.Repositories;
using WebPush;
using DotnetRoutix.Server.Infrastructure.Seeding;
using DotnetRoutix.Server.Infrastructure.Services;
using FluentValidation;
using FluentValidation.AspNetCore;

// Render sets PORT env var dynamically — honour it
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://0.0.0.0:{port}");

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────
builder.Services.AddSingleton<AppDbContext>();
builder.Services.AddSingleton<ExtendedDbContext>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<GitHubService>();
builder.Services.AddEndpointsApiExplorer();

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200", "https://enterprise-lunchbox-lms-prod.vercel.app"];

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins)
         .AllowAnyHeader()
         .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// ── Seed database ─────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
}

// ── Local helpers ─────────────────────────────────────────────
string GenOtp()   => Random.Shared.Next(100000, 999999).ToString();
string GenToken(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

async Task<AuthSession?> ResolveSession(HttpRequest req, AppDbContext db)
{
    var token = req.Headers["x-session-token"].FirstOrDefault();
    return string.IsNullOrEmpty(token) ? null : await db.GetSessionAsync(token);
}

VapidDetails? ResolveVapidDetails(IConfiguration config)
{
    var subject = config["Push:Subject"];
    var publicKey = config["Push:PublicKey"];
    var privateKey = config["Push:PrivateKey"];
    if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        return null;
    return new VapidDetails(subject, publicKey, privateKey);
}

async Task SendPushToUsersAsync(
    IEnumerable<string> userIds,
    string title,
    string body,
    string path,
    ExtendedDbContext ext,
    IConfiguration config)
{
    var targetIds = userIds
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (targetIds.Count == 0) return;

    var vapid = ResolveVapidDetails(config);
    if (vapid is null) return;

    var records = await ext.GetPushSubscriptionsByUserIdsAsync(targetIds);
    if (records.Count == 0) return;

    var client = new WebPushClient();
    var payload = JsonSerializer.Serialize(new
    {
        title,
        body,
        url = path,
        icon = "/assets/lunchbox-logo.svg",
        timestamp = DateTime.UtcNow
    });

    foreach (var rec in records)
    {
        var subscription = new PushSubscription(rec.Endpoint, rec.P256dh, rec.Auth);
        try
        {
            await client.SendNotificationAsync(subscription, payload, vapid);
        }
        catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await ext.RemovePushSubscriptionByEndpointAsync(rec.Endpoint);
        }
    }
}

// ── Root + Health ─────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new
{
    service = "RouteX .NET Backend",
    version = "v2.0",
    mode = "full",
    endpoints = new[]
    {
        "GET  /health",
        "POST /api/auth/register",
        "POST /api/auth/verify-otp",
        "POST /api/auth/login",
        "POST /api/auth/logout",
        "GET  /api/auth/me",
        "DELETE /api/auth/account",
        "POST /api/auth/profile-image",
        "GET  /api/auth/users",
        "GET  /api/auth/users/stats",
        "GET  /api/auth/captains",
        "POST /api/bookings",
        "GET  /api/bookings",
        "GET  /api/bookings/{id}",
        "PATCH /api/bookings/{id}/status",
        "POST /api/bookings/{id}/sos",
        "POST /api/bookings/{id}/feedback",
        "POST /api/support/complaints",
        "GET  /api/support/complaints"
    }
}));

app.MapGet("/health", () => Results.Ok(new
{
    service = "routex-dotnet",
    status = "ok",
    timestamp = DateTime.UtcNow
}));

// ══════════════════════════════════════════════════════════════
//  AUTH — REGISTER
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/auth/register", async (
    RegisterRequest req,
    HttpContext ctx,
    AppDbContext db,
    EmailService emailSvc,
    IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(req.Username)    ||
        string.IsNullOrWhiteSpace(req.DisplayName) ||
        string.IsNullOrWhiteSpace(req.Email)       ||
        string.IsNullOrWhiteSpace(req.Mobile)      ||
        string.IsNullOrWhiteSpace(req.Password)    ||
        string.IsNullOrWhiteSpace(req.Role))
        return Results.BadRequest(new { error = "username, displayName, email, mobile, password, role are required." });

    var role = req.Role.Trim().ToLowerInvariant();
    if (!new[] { "customer", "admin", "captain" }.Contains(role))
        return Results.BadRequest(new { error = "role must be customer, admin, or captain." });

    if (role == "captain" && string.IsNullOrWhiteSpace(req.CaptainVehicle))
        return Results.BadRequest(new { error = "captainVehicle is required for captain." });

    var username = req.Username.Trim().ToLowerInvariant();
    var email    = req.Email.Trim().ToLowerInvariant();
    var mobile   = req.Mobile.Trim();

    if (await db.UserExistsAsync(username, email, mobile))
        return Results.Conflict(new { error = "User already exists." });

    var user = new UserAccount
    {
        Username             = username,
        DisplayName          = req.DisplayName.Trim(),
        Email                = email,
        Mobile               = mobile,
        Password             = BCrypt.Net.BCrypt.HashPassword(req.Password),
        Role                 = role,
        CaptainVehicle       = role == "captain" ? req.CaptainVehicle?.Trim() : null,
        ProfileImageUrl      = req.ProfileImageUrl?.Trim(),
        CustomerOtpCompleted = 0
    };

    await db.InsertUserAsync(user);

    // Issue temp token + OTP
    var tempToken = GenToken("tmp");
    var otp       = GenOtp();
    var expiryMin = int.TryParse(config["App:OtpExpiryMinutes"], out var m) ? m : 10;

    await db.InsertSessionAsync(new AuthSession
    {
        Token     = tempToken,
        UserId    = user.Id,
        Username  = user.Username,
        Role      = user.Role,
        Type      = "temp",
        ExpiresAt = DateTime.UtcNow.AddMinutes(15)
    });

    await db.InsertOtpAsync(new OtpCode
    {
        UserId       = user.Id,
        SessionToken = tempToken,
        Channel      = "email",
        Code         = otp,
        ExpiresAt    = DateTime.UtcNow.AddMinutes(expiryMin)
    });

    try
    {
        await emailSvc.SendOtpAsync(user.Email, otp);
    }
    catch (Exception ex)
    {
        // Rollback: remove session + OTP + user
        await db.DeleteOtpsBySessionAsync(tempToken);
        await db.DeleteSessionAsync(tempToken);
        await db.DeleteUserAsync(user.Id);
        return Results.Problem(
            $"Unable to send OTP email: {ex.Message}",
            statusCode: StatusCodes.Status502BadGateway);
    }

    var isDebug = config["App:OtpDebugMode"] == "true";

    return Results.Created("/api/auth/me", isDebug
        ? (object)new
          {
              message     = "OTP sent to your email. Verify to complete registration.",
              requiresOtp = true,
              tempToken,
              channels    = new { email = user.Email },
              devOtps     = new { emailOtp = otp }
          }
        : new
          {
              message     = "OTP sent to your email. Verify to complete registration.",
              requiresOtp = true,
              tempToken,
              channels    = new { email = user.Email }
          });
});

// ══════════════════════════════════════════════════════════════
//  AUTH — VERIFY OTP
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/auth/verify-otp", async (
    VerifyOtpRequest req,
    AppDbContext db,
    IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(req.TempToken) || string.IsNullOrWhiteSpace(req.EmailOtp))
        return Results.BadRequest(new { error = "tempToken and emailOtp are required." });

    var session = await db.GetSessionAsync(req.TempToken);
    if (session is null || session.Type != "temp")
        return Results.Unauthorized();

    var otpRecord = await db.GetActiveOtpAsync(req.TempToken, "email");
    if (otpRecord is null || otpRecord.Code != req.EmailOtp.Trim())
        return Results.BadRequest(new { error = "Invalid or expired OTP." });

    await db.ConsumeOtpsAsync(req.TempToken);

    var user = await db.FindByIdAsync(session.UserId);
    if (user is null) return Results.NotFound();

    user.CustomerOtpCompleted = 1;
    await db.ReplaceUserAsync(user);

    var sessionToken = GenToken("sess");
    var expiryHours  = int.TryParse(config["App:SessionExpiryHours"], out var h) ? h : 24;

    await db.InsertSessionAsync(new AuthSession
    {
        Token     = sessionToken,
        UserId    = user.Id,
        Username  = user.Username,
        Role      = user.Role,
        Type      = "session",
        MfaVerified = 1,
        ExpiresAt = DateTime.UtcNow.AddHours(expiryHours)
    });

    return Results.Ok(new
    {
        sessionToken,
        user = MapUser(user),
        message = "Email verified. Registration complete!"
    });
});

// ══════════════════════════════════════════════════════════════
//  AUTH — LOGIN
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/auth/login", async (
    LoginRequest req,
    AppDbContext db,
    IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "Username and password are required." });

    var username = req.Username.Trim().ToLowerInvariant();
    var user     = await db.FindByUsernameAsync(username);

    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.Password))
        return Results.Unauthorized();

    if (!string.IsNullOrEmpty(req.Role) &&
        !req.Role.Trim().ToLowerInvariant().Equals(user.Role, StringComparison.OrdinalIgnoreCase))
        return Results.Problem("Selected login mode does not match your account role.", statusCode: 401);

    if (user.Role == "customer" && user.CustomerOtpCompleted != 1)
        return Results.Problem(
            "Complete your one-time registration OTP verification first.",
            statusCode: StatusCodes.Status403Forbidden);

    user.LastLoginAt = DateTime.UtcNow;
    await db.ReplaceUserAsync(user);

    var sessionToken = GenToken("sess");
    var expiryHours  = int.TryParse(config["App:SessionExpiryHours"], out var h) ? h : 24;

    await db.InsertSessionAsync(new AuthSession
    {
        Token       = sessionToken,
        UserId      = user.Id,
        Username    = user.Username,
        Role        = user.Role,
        Type        = "session",
        MfaVerified = 1,
        ExpiresAt   = DateTime.UtcNow.AddHours(expiryHours)
    });

    return Results.Ok(new
    {
        requiresOtp  = false,
        tempToken    = string.Empty,
        sessionToken,
        user         = MapUser(user),
        message      = "Login successful.",
        channels     = new { email = user.Email, mobile = user.Mobile }
    });
});

// ══════════════════════════════════════════════════════════════
//  AUTH — LOGOUT
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/auth/logout", async (HttpRequest req, AppDbContext db) =>
{
    var token = req.Headers["x-session-token"].FirstOrDefault();
    if (!string.IsNullOrEmpty(token)) await db.DeleteSessionAsync(token);
    return Results.Ok(new { message = "Logged out successfully." });
});

// ══════════════════════════════════════════════════════════════
//  AUTH — ME
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/auth/me", async (HttpRequest req, AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var user = await db.FindByIdAsync(session.UserId);
    if (user is null) return Results.NotFound();
    return Results.Ok(MapUser(user));
});

// ══════════════════════════════════════════════════════════════
//  AUTH — DELETE ACCOUNT
// ══════════════════════════════════════════════════════════════
app.MapDelete("/api/auth/account", async (HttpRequest req, AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    await db.DeleteUserAsync(session.UserId);
    await db.DeleteAllUserSessionsAsync(session.UserId);
    return Results.Ok(new { message = "Account deleted successfully." });
});

// ══════════════════════════════════════════════════════════════
//  AUTH — PROFILE IMAGE
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/auth/profile-image", async (
    UpdateProfileImageRequest body,
    HttpRequest req,
    AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var user = await db.FindByIdAsync(session.UserId);
    if (user is null) return Results.NotFound();

    user.ProfileImageUrl = body.ProfileImageUrl;
    await db.ReplaceUserAsync(user);
    return Results.Ok(new { message = "Profile image updated.", profileImageUrl = body.ProfileImageUrl });
});

// ══════════════════════════════════════════════════════════════
//  AUTH — USER PREFERENCES
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/auth/preferences", async (
    HttpRequest req,
    AppDbContext db,
    ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var rec = await ext.GetOrCreatePreferencesAsync(session.UserId);
    return Results.Ok(new
    {
        dataJson = rec.DataJson,
        updatedAt = rec.UpdatedAt
    });
});

app.MapPut("/api/auth/preferences", async (
    UpdateUserPreferencesRequest body,
    HttpRequest req,
    AppDbContext db,
    ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.DataJson))
        return Results.BadRequest(new { error = "dataJson is required." });

    try
    {
        using var _ = JsonDocument.Parse(body.DataJson);
    }
    catch
    {
        return Results.BadRequest(new { error = "dataJson must be valid JSON." });
    }

    var rec = await ext.GetOrCreatePreferencesAsync(session.UserId);
    rec.DataJson = body.DataJson;
    rec.UpdatedAt = DateTime.UtcNow;
    await ext.ReplacePreferencesAsync(rec);

    return Results.Ok(new
    {
        message = "Preferences saved successfully.",
        updatedAt = rec.UpdatedAt
    });
});

app.MapGet("/api/push/public-key", (IConfiguration config) =>
{
    var publicKey = config["Push:PublicKey"] ?? string.Empty;
    return Results.Ok(new { publicKey });
});

app.MapPost("/api/push/subscribe", async (
    SavePushSubscriptionRequest body,
    HttpRequest req,
    AppDbContext db,
    ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Endpoint) ||
        string.IsNullOrWhiteSpace(body.P256dh) ||
        string.IsNullOrWhiteSpace(body.Auth))
    {
        return Results.BadRequest(new { error = "endpoint, p256dh and auth are required." });
    }

    await ext.SavePushSubscriptionAsync(new PushSubscriptionRecord
    {
        UserId = session.UserId,
        Endpoint = body.Endpoint.Trim(),
        P256dh = body.P256dh.Trim(),
        Auth = body.Auth.Trim(),
        UpdatedAt = DateTime.UtcNow
    });

    return Results.Ok(new { message = "Push subscription saved." });
});

app.MapPost("/api/push/unsubscribe", async (
    RemovePushSubscriptionRequest body,
    HttpRequest req,
    AppDbContext db,
    ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Endpoint))
        return Results.BadRequest(new { error = "endpoint is required." });

    await ext.RemovePushSubscriptionAsync(session.UserId, body.Endpoint.Trim());
    return Results.Ok(new { message = "Push subscription removed." });
});

// ══════════════════════════════════════════════════════════════
//  AUTH — USERS (admin)
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/auth/users", async (HttpRequest req, AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var users = await db.GetAllUsersAsync();
    return Results.Ok(users.Select(u => new
    {
        id                   = u.Id,
        username             = u.Username,
        displayName          = u.DisplayName,
        email                = u.Email,
        mobile               = u.Mobile,
        role                 = u.Role,
        captainVehicle       = u.CaptainVehicle,
        customerOtpCompleted = u.CustomerOtpCompleted == 1,
        createdAt            = u.CreatedAt
    }));
});

app.MapGet("/api/auth/users/stats", async (HttpRequest req, AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null || session.Role != "admin") return Results.Forbid();

    var all = await db.GetAllUsersAsync();
    return Results.Ok(new
    {
        total     = all.Count,
        customers = all.Count(u => u.Role == "customer"),
        captains  = all.Count(u => u.Role == "captain"),
        admins    = all.Count(u => u.Role == "admin"),
        source    = "mongodb"
    });
});

// ══════════════════════════════════════════════════════════════
//  AUTH — CAPTAINS
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/auth/captains", async (
    HttpRequest req, string? vehicleType,
    AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var captains = await db.GetCaptainsAsync(vehicleType);
    return Results.Ok(captains.Select(u => new
    {
        id             = u.Id,
        username       = u.Username,
        displayName    = u.DisplayName,
        phone          = u.Mobile,
        vehicleType    = u.CaptainVehicle ?? "bike",
        profileImageUrl= u.ProfileImageUrl,
        rating         = 4.8,
        availability   = "available",
        createdAt      = u.CreatedAt
    }));
});

// ══════════════════════════════════════════════════════════════
//  BOOKINGS — CREATE
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/bookings", async (
    CreateBookingRequest body,
    HttpRequest req,
    AppDbContext db,
    ExtendedDbContext ext,
    IConfiguration config) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var bookingId = $"RX{DateTime.UtcNow:yyMMdd}{Random.Shared.Next(1000, 9999)}";

    var booking = new RideBooking
    {
        Id                 = bookingId,
        UserId             = session.UserId,
        UserName           = session.Username,
        BookingFor         = body.BookingFor,
        RecipientName      = body.RecipientName,
        RecipientPhone     = body.RecipientPhone,
        ScheduledAt        = body.ScheduledAt is not null ? DateTime.Parse(body.ScheduledAt) : null,
        IsScheduled        = body.ScheduledAt is not null,
        ServiceType        = body.ServiceType,
        PaymentMethod      = body.PaymentMethod,
        VehicleType        = body.VehicleType,
        Pickup             = body.Pickup,
        Drop               = body.Drop,
        CurrentLocation    = body.Pickup,
        Status             = "created",
        Otp                = Random.Shared.Next(1000, 9999).ToString(),
        CaptainId          = body.CaptainId,
        NotificationTarget = body.NotificationTarget ?? "all",
        Notification       = "pending",
        EstimatedFare      = body.EstimatedFare,
        RideNotes          = body.RideNotes,
        DriverName         = "Ravi Kumar",
        DriverPhone        = "+919000000001"
    };

    await db.InsertBookingAsync(booking);
    var captains = await db.GetCaptainsAsync();
    await SendPushToUsersAsync(
        captains.Select(c => c.Id),
        "New ride request",
        $"{booking.ServiceType} ride from {booking.Pickup.Address} to {booking.Drop.Address}",
        "/captain-profile",
        ext,
        config);

    return Results.Created($"/api/bookings/{booking.Id}", MapBooking(booking));
});

// ══════════════════════════════════════════════════════════════
//  BOOKINGS — LIST
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/bookings", async (HttpRequest req, AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var bookings = session.Role == "admin"
        ? await db.GetBookingsAsync()
        : session.Role == "captain"
            ? await db.GetBookingsAsync(captainId: session.UserId)
            : await db.GetBookingsAsync(userId: session.UserId);

    return Results.Ok(bookings.Select(MapBooking));
});

// ══════════════════════════════════════════════════════════════
//  EVENTS — SSE (cross-device live booking updates)
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/events", async (HttpContext ctx, AppDbContext db) =>
{
    var token = ctx.Request.Query["sessionToken"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(token))
        token = ctx.Request.Headers["x-session-token"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(token))
        return Results.Unauthorized();

    var session = await db.GetSessionAsync(token);
    if (session is null)
        return Results.Unauthorized();

    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("Connection", "keep-alive");
    ctx.Response.Headers.Append("X-Accel-Buffering", "no");

    var ct = ctx.RequestAborted;
    var lastSeen = DateTime.UtcNow;

    try
    {
        while (!ct.IsCancellationRequested)
        {
            var bookings = session.Role == "admin"
                ? await db.GetBookingsAsync()
                : session.Role == "captain"
                    ? await db.GetBookingsAsync(captainId: session.UserId)
                    : await db.GetBookingsAsync(userId: session.UserId);

            var changed = bookings
                .Where(b => b.UpdatedAt > lastSeen)
                .OrderBy(b => b.UpdatedAt)
                .ToList();

            foreach (var booking in changed)
            {
                var eventName = booking.CreatedAt == booking.UpdatedAt ? "new_booking" : "booking_updated";
                var payload = JsonSerializer.Serialize(MapBooking(booking));
                await ctx.Response.WriteAsync($"event: {eventName}\n", ct);
                await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
                lastSeen = booking.UpdatedAt;
            }

            await ctx.Response.WriteAsync(": keep-alive\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
    catch (OperationCanceledException)
    {
        // client disconnected
    }

    return Results.Empty;
});

// ══════════════════════════════════════════════════════════════
//  BOOKINGS — GET BY ID
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/bookings/{id}", async (
    string id, HttpRequest req, AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var booking = await db.GetBookingByIdAsync(id);
    return booking is null
        ? Results.NotFound(new { error = "Booking not found." })
        : Results.Ok(MapBooking(booking));
});

// ══════════════════════════════════════════════════════════════
//  BOOKINGS — UPDATE STATUS
// ══════════════════════════════════════════════════════════════
app.MapPatch("/api/bookings/{id}/status", async (
    string id, UpdateBookingStatusRequest body,
    HttpRequest req, AppDbContext db, ExtendedDbContext ext, IConfiguration config) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var booking = await db.GetBookingByIdAsync(id);
    if (booking is null) return Results.NotFound(new { error = "Booking not found." });

    booking.Status    = body.Status;
    booking.UpdatedAt = DateTime.UtcNow;
    await db.ReplaceBookingAsync(booking);
    await SendPushToUsersAsync(
        new[] { booking.UserId, booking.CaptainId ?? string.Empty },
        "Ride update",
        $"Booking {booking.Id} status updated to {booking.Status}.",
        "/activity",
        ext,
        config);
    return Results.Ok(MapBooking(booking));
});

// ══════════════════════════════════════════════════════════════
//  BOOKINGS — SOS
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/bookings/{id}/sos", async (
    string id, SosRequest body,
    HttpRequest req, AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var booking = await db.GetBookingByIdAsync(id);
    if (booking is null) return Results.NotFound(new { error = "Booking not found." });

    booking.SosTriggered = true;
    booking.SosByRole    = body.ByRole;
    booking.UpdatedAt    = DateTime.UtcNow;
    await db.ReplaceBookingAsync(booking);
    return Results.Ok(new { message = "SOS alert triggered.", bookingId = id });
});

// ══════════════════════════════════════════════════════════════
//  BOOKINGS — FEEDBACK
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/bookings/{id}/feedback", async (
    string id, SubmitFeedbackRequest body,
    HttpRequest req, AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var booking = await db.GetBookingByIdAsync(id);
    if (booking is null) return Results.NotFound(new { error = "Booking not found." });

    booking.FeedbackSubmitted = true;
    booking.FeedbackText      = body.FeedbackText;
    booking.RideRating        = body.RideRating;
    booking.CaptainRating     = body.CaptainRating;
    booking.LovedRide         = body.LovedRide;
    booking.LovedCaptain      = body.LovedCaptain;
    booking.UpdatedAt         = DateTime.UtcNow;
    await db.ReplaceBookingAsync(booking);
    return Results.Ok(new { message = "Feedback submitted. Thank you!" });
});

// ══════════════════════════════════════════════════════════════
//  SUPPORT — SUBMIT COMPLAINT / BUG
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/support/complaints", async (
    SubmitComplaintRequest body,
    HttpRequest req,
    AppDbContext db,
    GitHubService github) =>
{
    if (string.IsNullOrWhiteSpace(body.Type)    ||
        string.IsNullOrWhiteSpace(body.Subject) ||
        string.IsNullOrWhiteSpace(body.Description))
        return Results.BadRequest(new { error = "type, subject, and description are required." });

    var sessionToken = req.Headers["x-session-token"].FirstOrDefault();
    var session      = string.IsNullOrEmpty(sessionToken)
        ? null
        : await db.GetSessionAsync(sessionToken);

    var complaint = new SupportComplaint
    {
        Type        = body.Type.Trim(),
        Subject     = body.Subject.Trim(),
        Name        = body.Name?.Trim()    ?? string.Empty,
        Contact     = body.Contact?.Trim() ?? string.Empty,
        Description = body.Description.Trim(),
        UserId      = session?.UserId   ?? string.Empty,
        Username    = session?.Username ?? string.Empty
    };

    if (body.Type.Equals("bug", StringComparison.OrdinalIgnoreCase))
    {
        var issueBody = $"""
            ## Bug Report from RouteX App

            | Field | Value |
            |-------|-------|
            | **Reported by** | {complaint.Name} (@{complaint.Username}) |
            | **Contact** | {complaint.Contact} |
            | **Submitted** | {DateTime.UtcNow:u} |

            ## Description
            {complaint.Description}
            """;

        var (created, url, number) = await github.CreateBugIssueAsync(
            $"[Bug] {complaint.Subject}", issueBody);

        complaint.GitHubIssueUrl    = url;
        complaint.GitHubIssueNumber = number;

        await db.InsertComplaintAsync(complaint);

        return Results.Created("/api/support/complaints", created
            ? (object)new { message = $"Bug submitted and GitHub issue #{number} created.", issueUrl = url, issueNumber = number }
            : new { message = "Bug submitted. GitHub issue creation skipped (not configured)." });
    }

    await db.InsertComplaintAsync(complaint);
    return Results.Created("/api/support/complaints", new { message = "Complaint submitted successfully." });
});

// ══════════════════════════════════════════════════════════════
//  SUPPORT — GET COMPLAINTS (admin)
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/support/complaints", async (HttpRequest req, AppDbContext db) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();

    var complaints = session.Role == "admin"
        ? await db.GetComplaintsAsync()
        : await db.GetComplaintsAsync(userId: session.UserId);

    return Results.Ok(complaints);
});

// ══════════════════════════════════════════════════════════════
//  PAYMENT
// ══════════════════════════════════════════════════════════════

// ─ moved below app.Run() as local functions are not allowed in top-level statements
// So we register them above app.Run() ──────────────────────────

// GET /api/payment
app.MapGet("/api/payment", async (HttpRequest req, AppDbContext db, ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var doc = await ext.GetOrCreatePaymentAsync(session.UserId);
    return Results.Ok(doc);
});

// PATCH /api/payment
app.MapPatch("/api/payment", async (
    HttpRequest req, AppDbContext db, ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var doc = await ext.GetOrCreatePaymentAsync(session.UserId);
    doc.UpdatedAt = DateTime.UtcNow.ToString("o");
    await ext.ReplacePaymentAsync(doc);
    return Results.Ok(doc);
});

// POST /api/payment/wallet/add
app.MapPost("/api/payment/wallet/add", async (
    AddWalletRequest body, HttpRequest req,
    AppDbContext db, ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    if (body.Amount < 1) return Results.BadRequest(new { error = "amount must be >= 1" });

    var doc = await ext.GetOrCreatePaymentAsync(session.UserId);
    doc.WalletBalance += body.Amount;
    var txn = new { label = $"Added ₹{body.Amount} to wallet", date = DateTime.UtcNow.ToString("dd MMM yyyy"), amount = body.Amount, type = "credit" };
    doc.WalletTxns.Insert(0, txn);
    doc.UpdatedAt = DateTime.UtcNow.ToString("o");
    await ext.ReplacePaymentAsync(doc);
    return Results.Ok(new { message = "Wallet topped up.", wallet_balance = doc.WalletBalance, txn });
});

// ══════════════════════════════════════════════════════════════
//  PROMO CODES
// ══════════════════════════════════════════════════════════════
var promoCatalog = new[]
{
    new { code = "RIDER50",     type = "percent", value = 50,  minAmount = 100, maxDiscount = 200 },
    new { code = "FAST25",      type = "percent", value = 25,  minAmount = 80,  maxDiscount = 100 },
    new { code = "PARCELPLUS",  type = "flat",    value = 30,  minAmount = 150, maxDiscount = 30  },
    new { code = "FRESHNOW",    type = "flat",    value = 20,  minAmount = 100, maxDiscount = 20  },
    new { code = "CARE10",      type = "percent", value = 10,  minAmount = 50,  maxDiscount = 50  },
    new { code = "FIRST100",    type = "flat",    value = 100, minAmount = 200, maxDiscount = 100 },
    new { code = "WKND20",      type = "percent", value = 20,  minAmount = 120, maxDiscount = 80  },
    new { code = "SAFENOW",     type = "percent", value = 15,  minAmount = 60,  maxDiscount = 60  },
};

app.MapPost("/api/promos/validate", (ValidatePromoRequest body) =>
{
    var code = (body.Code ?? string.Empty).Trim().ToUpperInvariant();
    if (string.IsNullOrEmpty(code))
        return Results.BadRequest(new { valid = false, error = "Promo code is required." });

    if (body.Amount <= 0)
        return Results.BadRequest(new { valid = false, error = "Valid amount is required." });

    var promo = promoCatalog.FirstOrDefault(p => p.code == code);
    if (promo is null)
        return Results.NotFound(new { valid = false, error = "Invalid promo code." });

    if (body.Amount < promo.minAmount)
        return Results.BadRequest(new { valid = false, error = $"Promo requires minimum ₹{promo.minAmount}.", minAmount = promo.minAmount });

    var discount = promo.type == "flat"
        ? Math.Min(promo.value, (int)body.Amount)
        : (int)Math.Min(body.Amount * promo.value / 100, promo.maxDiscount);

    var payable = Math.Max(0, body.Amount - discount);

    return Results.Ok(new
    {
        valid = true,
        code  = promo.code,
        discount,
        payableAmount = payable,
        promo = new { promo.code, promo.type, promo.value, promo.minAmount, promo.maxDiscount }
    });
});

// ══════════════════════════════════════════════════════════════
//  BOOKING EXTRAS
// ══════════════════════════════════════════════════════════════

// POST /api/bookings/{id}/verify-otp
app.MapPost("/api/bookings/{id}/verify-otp", async (
    string id, VerifyBookingOtpRequest body,
    HttpRequest req, AppDbContext db, ExtendedDbContext ext, IConfiguration config) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var booking = await db.GetBookingByIdAsync(id);
    if (booking is null) return Results.NotFound(new { error = "Booking not found." });
    if (booking.Otp != body.Otp) return Results.BadRequest(new { error = "Invalid OTP." });
    booking.OtpVerified = true;
    booking.Status      = "in_transit";
    booking.UpdatedAt   = DateTime.UtcNow;
    await db.ReplaceBookingAsync(booking);
    await SendPushToUsersAsync(
        new[] { booking.UserId, booking.CaptainId ?? string.Empty },
        "Ride started",
        $"Booking {booking.Id} OTP verified and ride started.",
        "/tracking",
        ext,
        config);
    return Results.Ok(new { message = "OTP verified. Ride started!", booking = MapBooking(booking) });
});

// POST /api/bookings/{id}/approve
app.MapPost("/api/bookings/{id}/approve", async (
    string id, HttpRequest req, AppDbContext db, ExtendedDbContext ext, IConfiguration config) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var booking = await db.GetBookingByIdAsync(id);
    if (booking is null) return Results.NotFound();
    booking.Status    = "assigned";
    booking.CaptainId = session.UserId;
    booking.DriverName= session.Username;
    booking.UpdatedAt = DateTime.UtcNow;
    await db.ReplaceBookingAsync(booking);
    await SendPushToUsersAsync(
        new[] { booking.UserId },
        "Captain accepted your ride",
        $"Booking {booking.Id} has been accepted by {booking.DriverName}.",
        "/tracking/" + booking.Id,
        ext,
        config);
    return Results.Ok(MapBooking(booking));
});

// POST /api/bookings/{id}/cancel
app.MapPost("/api/bookings/{id}/cancel", async (
    string id, CancelBookingRequest body,
    HttpRequest req, AppDbContext db, ExtendedDbContext ext, IConfiguration config) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var booking = await db.GetBookingByIdAsync(id);
    if (booking is null) return Results.NotFound();
    booking.Status    = "cancelled";
    booking.RideNotes = (booking.RideNotes + $" | Cancelled: {body.Reason}").Trim();
    booking.UpdatedAt = DateTime.UtcNow;
    await db.ReplaceBookingAsync(booking);
    await SendPushToUsersAsync(
        new[] { booking.UserId, booking.CaptainId ?? string.Empty },
        "Ride cancelled",
        $"Booking {booking.Id} was cancelled.",
        "/activity",
        ext,
        config);
    return Results.Ok(new { message = "Booking cancelled.", booking = MapBooking(booking) });
});

// POST /api/bookings/{id}/pay
app.MapPost("/api/bookings/{id}/pay", async (
    string id, PayBookingRequest body,
    HttpRequest req, AppDbContext db, ExtendedDbContext ext, IConfiguration config) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var booking = await db.GetBookingByIdAsync(id);
    if (booking is null) return Results.NotFound();
    booking.PaymentDone   = true;
    booking.PaymentDoneAt = DateTime.UtcNow;
    booking.FinalAmount   = body.Amount;
    booking.UpdatedAt     = DateTime.UtcNow;
    await db.ReplaceBookingAsync(booking);
    await SendPushToUsersAsync(
        new[] { booking.UserId, booking.CaptainId ?? string.Empty },
        "Payment received",
        $"Payment for booking {booking.Id} is completed.",
        "/tracking/" + booking.Id,
        ext,
        config);
    return Results.Ok(new { message = "Payment recorded.", booking = MapBooking(booking) });
});

// POST /api/bookings/{id}/close-tracking
app.MapPost("/api/bookings/{id}/close-tracking", async (
    string id, HttpRequest req, AppDbContext db, ExtendedDbContext ext, IConfiguration config) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var booking = await db.GetBookingByIdAsync(id);
    if (booking is null) return Results.NotFound();
    booking.TrackingClosed = true;
    booking.Status         = "completed";
    booking.UpdatedAt      = DateTime.UtcNow;
    await db.ReplaceBookingAsync(booking);
    await SendPushToUsersAsync(
        new[] { booking.UserId, booking.CaptainId ?? string.Empty },
        "Ride completed",
        $"Booking {booking.Id} is marked as completed.",
        "/activity",
        ext,
        config);
    return Results.Ok(new { message = "Tracking closed.", booking = MapBooking(booking) });
});

// ══════════════════════════════════════════════════════════════
//  APP FEEDBACK
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/support/app-feedback", async (
    SubmitAppFeedbackRequest body,
    HttpRequest req,
    AppDbContext db, ExtendedDbContext ext) =>
{
    var sessionToken = req.Headers["x-session-token"].FirstOrDefault();
    var session = string.IsNullOrEmpty(sessionToken) ? null : await db.GetSessionAsync(sessionToken);

    var fb = new AppFeedback
    {
        FeedbackType  = body.FeedbackType,
        FeedbackLabel = body.FeedbackLabel,
        AppVersion    = body.AppVersion,
        Route         = body.Route,
        Rating        = body.Rating,
        Note          = body.Note,
        UserId        = session?.UserId   ?? string.Empty,
        Username      = session?.Username ?? string.Empty,
        SubmittedAt   = body.SubmittedAt
    };
    await ext.InsertFeedbackAsync(fb);
    return Results.Ok(new { message = "Feedback recorded. Thank you!" });
});

// ══════════════════════════════════════════════════════════════
//  KYC
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/auth/kyc/status", async (HttpRequest req, AppDbContext db, ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var kyc = await ext.GetOrCreateKycAsync(session.UserId);
    return Results.Ok(new { kycStatus = kyc.Status, documentType = kyc.DocumentType, referenceId = kyc.ReferenceId, updatedAt = kyc.UpdatedAt });
});

app.MapPost("/api/auth/kyc/submit", async (
    SubmitKycRequest body, HttpRequest req,
    AppDbContext db, ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var kyc = await ext.GetOrCreateKycAsync(session.UserId);
    kyc.Status       = "pending";
    kyc.DocumentType = body.DocumentType;
    kyc.ReferenceId  = body.ReferenceId;
    kyc.Notes        = body.Notes;
    kyc.UpdatedAt    = DateTime.UtcNow;
    await ext.ReplaceKycAsync(kyc);
    return Results.Ok(new { message = "KYC submitted. Under review.", kycStatus = "pending" });
});

// ══════════════════════════════════════════════════════════════
//  CAPTAIN FEEDBACK
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/auth/captain-feedback", async (
    SubmitCaptainFeedbackRequest body,
    HttpRequest req, AppDbContext db, ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var user = await db.FindByIdAsync(session.UserId);
    var fb = new CaptainFeedback
    {
        BookingId        = body.BookingId,
        CaptainUserId    = body.CaptainId ?? string.Empty,
        CaptainName      = body.CaptainName,
        SubmittedByUserId= session.UserId,
        SubmittedByName  = user?.DisplayName ?? session.Username,
        RideRating       = body.RideRating,
        CaptainRating    = body.CaptainRating,
        FeedbackText     = body.FeedbackText,
        LovedRide        = body.LovedRide,
        LovedCaptain     = body.LovedCaptain
    };
    await ext.InsertCaptainFeedbackAsync(fb);
    return Results.Ok(new { message = "Captain feedback submitted." });
});

app.MapGet("/api/auth/captain-feedback/stats", async (
    HttpRequest req, string? captainId,
    AppDbContext db, ExtendedDbContext ext) =>
{
    var session = await ResolveSession(req, db);
    if (session is null) return Results.Unauthorized();
    var id = captainId ?? session.UserId;
    var stats = await ext.GetCaptainFeedbackStatsAsync(id);
    return Results.Ok(stats);
});

// ══════════════════════════════════════════════════════════════
//  USER ACTION LOG (fire-and-forget)
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/auth/user-action", async (
    HttpRequest req, AppDbContext db,
    ILogger<Program> logger) =>
{
    var session = await ResolveSession(req, db);
    // Best-effort: log and return ok
    logger.LogInformation("[UserAction] user={User}", session?.Username ?? "anon");
    return Results.Ok(new { message = "Action recorded." });
});

app.MapGet("/api/auth/actions", (HttpRequest req) =>
    Results.Ok(Array.Empty<object>()));

// ══════════════════════════════════════════════════════════════
//  VOICE CHALLENGE (stub — returns phrase for voice verification)
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/auth/voice-challenge", (HttpRequest req) =>
{
    var phrases = new[] { "blue elephant", "sunny morning", "green river", "open window", "silver cloud" };
    var phrase  = phrases[Random.Shared.Next(phrases.Length)];
    return Results.Ok(new { phrase, expiresAt = DateTime.UtcNow.AddMinutes(2) });
});

app.MapPost("/api/auth/voice-verify", () =>
    Results.Ok(new { message = "Voice verified successfully." }));

// ══════════════════════════════════════════════════════════════
//  NEARBY HOTELS (mock — returns static list near pickup)
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/places/nearby-hotels", (double? lat, double? lng, string? preference) =>
{
    var hotels = new[]
    {
        new { id = "h1", name = "Spice Garden", category = "nonveg", locationLabel = "Hitech City", distanceKm = 0.8, etaMinutes = 5, rating = 4.5, openNow = true, cuisine = "Indian", priceForTwo = 300, lat = 17.4489, lng = 78.3811 },
        new { id = "h2", name = "Green Bites",  category = "veg",    locationLabel = "Madhapur",    distanceKm = 1.2, etaMinutes = 7, rating = 4.2, openNow = true, cuisine = "South Indian", priceForTwo = 200, lat = 17.4515, lng = 78.3925 },
        new { id = "h3", name = "Royal Dhaba",  category = "nonveg", locationLabel = "Gachibowli",  distanceKm = 1.8, etaMinutes = 9, rating = 4.7, openNow = true, cuisine = "Punjabi", priceForTwo = 450, lat = 17.4401, lng = 78.3489 },
        new { id = "h4", name = "Veg Delight",  category = "veg",    locationLabel = "Kondapur",    distanceKm = 2.1, etaMinutes = 11, rating = 4.0, openNow = false, cuisine = "Continental", priceForTwo = 350, lat = 17.4603, lng = 78.3620 },
    };

    var filtered = string.IsNullOrEmpty(preference) || preference == "all"
        ? hotels
        : hotels.Where(h => h.category == preference).ToArray();

    return Results.Ok(filtered);
});

// GET /api/menu/hotels/{hotelId}/items
app.MapGet("/api/menu/hotels/{hotelId}/items", (string hotelId) =>
{
    var menus = new Dictionary<string, object[]>
    {
        ["h1"] = [
            new { id = "m1", name = "Chicken Biryani",  category = "nonveg", price = 180, isTop = true,  description = "Hyderabadi dum style" },
            new { id = "m2", name = "Mutton Curry",      category = "nonveg", price = 220, isTop = false, description = "Rich spicy gravy"     },
            new { id = "m3", name = "Raita",             category = "veg",    price = 40,  isTop = false, description = "Yogurt side dish"     },
        ],
        ["h2"] = [
            new { id = "m4", name = "Masala Dosa",       category = "veg",    price = 80,  isTop = true,  description = "Crispy with potato filling" },
            new { id = "m5", name = "Idli Sambar",        category = "veg",    price = 60,  isTop = false, description = "Soft idli with sambar"      },
        ],
        ["h3"] = [
            new { id = "m6", name = "Butter Chicken",    category = "nonveg", price = 250, isTop = true,  description = "Creamy tomato gravy" },
            new { id = "m7", name = "Dal Makhani",       category = "veg",    price = 160, isTop = false, description = "Slow cooked black lentils" },
        ],
        ["h4"] = [
            new { id = "m8", name = "Veg Burger",        category = "veg",    price = 120, isTop = true,  description = "Crispy patty with veggies" },
            new { id = "m9", name = "Pasta Arabiata",    category = "veg",    price = 180, isTop = false, description = "Spicy tomato pasta"        },
        ],
    };

    if (!menus.TryGetValue(hotelId, out var items))
        return Results.NotFound(new { error = "Hotel not found." });

    return Results.Ok(items);
});

// ══════════════════════════════════════════════════════════════
//  LIVE FARE (pricing)
// ══════════════════════════════════════════════════════════════
app.MapPost("/api/pricing/live-fare", (LiveFareRequest body) =>
{
    if (body.Pickup is null || body.Drop is null)
        return Results.BadRequest(new { error = "pickup and drop are required." });

    // Haversine distance
    const double R = 6371;
    var dLat = (body.Drop.Lat - body.Pickup.Lat) * Math.PI / 180;
    var dLng = (body.Drop.Lng - body.Pickup.Lng) * Math.PI / 180;
    var a    = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
               Math.Cos(body.Pickup.Lat * Math.PI / 180) *
               Math.Cos(body.Drop.Lat  * Math.PI / 180) *
               Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
    var distKm = R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    distKm = Math.Max(1, Math.Round(distKm, 1));

    var farePerKm = body.VehicleType?.ToLower() switch
    {
        "bike"   => 8,
        "auto"   => 12,
        "car"    => 18,
        "prime"  => 24,
        _        => 12
    };

    var baseFare     = 20;
    var distFare     = (int)(distKm * farePerKm);
    var trafficMult  = 1.1;
    var weatherMult  = 1.0;
    var total        = (int)(( baseFare + distFare) * trafficMult * weatherMult);

    return Results.Ok(new
    {
        distanceKm                = distKm,
        durationInTrafficMinutes  = (int)(distKm / 25 * 60),
        trafficCondition          = "medium",
        weatherCondition          = "clear",
        weatherSummary            = "Clear skies",
        source                    = new { googleTraffic = false, openWeather = false },
        breakdown = new
        {
            baseFare,
            distanceFare    = distFare,
            vehicleMultiplier = 1.0,
            trafficMultiplier = trafficMult,
            weatherMultiplier = weatherMult,
            total
        },
        suggestedMessage = $"₹{total} estimated for {distKm}km"
    });
});

// ══════════════════════════════════════════════════════════════
//  INTEGRATIONS HEALTH
// ══════════════════════════════════════════════════════════════
app.MapGet("/api/integrations/health", (IConfiguration config) => Results.Ok(new
{
    service   = "routex-dotnet",
    status    = "ok",
    checkedAt = DateTime.UtcNow,
    integrations = new object[]
    {
        new { key = "googleMaps", name = "Google Maps", healthy = false, statusLabel = "Not configured", details = "Set GOOGLE_MAPS_API_KEY to enable" },
        new { key = "openWeather", name = "OpenWeather", healthy = false, statusLabel = "Not configured", details = "Set OPENWEATHER_API_KEY to enable" },
        new { key = "otpDelivery", name = "Email OTP", healthy = !string.IsNullOrEmpty(config["Email:SmtpHost"]) || config["App:OtpDebugMode"] == "true", statusLabel = config["App:OtpDebugMode"] == "true" ? "Debug Mode" : "Live", details = "OTP delivery via SMTP" },
        new { key = "authService", name = "Auth Service", healthy = true, statusLabel = "Live", details = ".NET Core auth service running" }
    }
}));

app.Run();

// ── Mapping helpers ───────────────────────────────────────────
static object MapUser(UserAccount u) => new
{
    id             = u.Id,
    username       = u.Username,
    displayName    = u.DisplayName,
    role           = u.Role,
    email          = u.Email,
    mobile         = u.Mobile,
    captainVehicle = u.CaptainVehicle,
    profileImageUrl= u.ProfileImageUrl
};

static object MapBooking(RideBooking b) => new
{
    id                 = b.Id,
    userId             = b.UserId,
    userName           = b.UserName,
    bookingFor         = b.BookingFor,
    recipientName      = b.RecipientName,
    recipientPhone     = b.RecipientPhone,
    isScheduled        = b.IsScheduled,
    scheduledAt        = b.ScheduledAt,
    serviceType        = b.ServiceType,
    paymentMethod      = b.PaymentMethod,
    vehicleType        = b.VehicleType,
    pickup             = b.Pickup,
    drop               = b.Drop,
    currentLocation    = b.CurrentLocation,
    status             = b.Status,
    otp                = b.Otp,
    otpVerified        = b.OtpVerified,
    driverName         = b.DriverName,
    driverPhone        = b.DriverPhone,
    captainId          = b.CaptainId,
    notificationTarget = b.NotificationTarget,
    notification       = b.Notification,
    estimatedFare      = b.EstimatedFare,
    rideNotes          = b.RideNotes,
    sosTriggered       = b.SosTriggered,
    sosByRole          = b.SosByRole,
    feedbackSubmitted  = b.FeedbackSubmitted,
    feedbackText       = b.FeedbackText,
    rideRating         = b.RideRating,
    captainRating      = b.CaptainRating,
    lovedRide          = b.LovedRide,
    lovedCaptain       = b.LovedCaptain,
    finalAmount        = b.FinalAmount,
    paymentDone        = b.PaymentDone,
    trackingClosed     = b.TrackingClosed,
    createdAt          = b.CreatedAt,
    updatedAt          = b.UpdatedAt
};
