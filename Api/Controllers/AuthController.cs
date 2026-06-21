using DotnetRoutix.Server.Application.Contracts;
using DotnetRoutix.Server.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace DotnetRoutix.Server.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet("demo-user")]
    public async Task<IActionResult> GetDemoUser()
    {
        var demoUser = await _authService.GetDemoUserAsync();
        if (demoUser is null)
        {
            return NotFound(new { message = "User was not found." });
        }

        return Ok(demoUser);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginStartRequest request)
    {
        try
        {
            var response = await _authService.LoginStartAsync(request);
            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var response = await _authService.RegisterAsync(request);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        try
        {
            var response = await _authService.VerifyOtpAsync(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("verify-pin")]
    public async Task<IActionResult> VerifyPin([FromBody] VerifyPinRequest request)
    {
        var verificationResult = await _authService.VerifyPinAsync(request);
        if (verificationResult is null)
        {
            return NotFound(new { message = "User was not found." });
        }

        return Ok(verificationResult);
    }

    // Stub endpoints for frontend compatibility
    [HttpPost("voice-challenge")]
    public IActionResult VoiceChallenge()
    {
        return Ok(new { phrase = "Please say hello", expiresAt = DateTime.UtcNow.AddMinutes(5).ToString("O") });
    }

    [HttpPost("voice-verify")]
    public IActionResult VoiceVerify([FromBody] object request)
    {
        return Ok(new { message = "Voice verified successfully" });
    }

    [HttpPost("user-action")]
    public IActionResult RecordUserAction([FromBody] object request)
    {
        return Ok(new { message = "Action recorded" });
    }

    [HttpGet("actions")]
    public IActionResult GetActionLogs()
    {
        return Ok(new object[] { });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpDelete("account")]
    public IActionResult DeleteAccount()
    {
        return Ok(new { message = "Account deleted" });
    }

    [HttpGet("captain-feedback/stats")]
    public IActionResult GetCaptainFeedbackStats()
    {
        return Ok(new { totalRatings = 0, averageRating = 0 });
    }

    [HttpGet("captains")]
    public IActionResult GetCaptains()
    {
        return Ok(new object[] { });
    }

    [HttpGet("users/stats")]
    public IActionResult GetUserStats()
    {
        return Ok(new { totalUsers = 0, activeUsers = 0 });
    }

    [HttpGet("users")]
    public IActionResult GetUsers()
    {
        return Ok(new object[] { });
    }
}
