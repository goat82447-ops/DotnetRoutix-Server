using System.Net;
using System.Net.Mail;
using DotnetRoutix.Server.Application.Contracts;
using DotnetRoutix.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DotnetRoutix.Server.Infrastructure.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> emailOptions, ILogger<SmtpEmailSender> logger)
    {
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public async Task<bool> SendOtpEmailAsync(string toEmail, string displayName, string otpCode)
    {
        var smtpUser = FirstNonEmpty(_emailOptions.GmailUser, Environment.GetEnvironmentVariable("GMAIL_USER"));
        var smtpPass = FirstNonEmpty(_emailOptions.GmailAppPassword, Environment.GetEnvironmentVariable("GMAIL_APP_PASSWORD"));
        var fromEmail = FirstNonEmpty(_emailOptions.GmailFromEmail, Environment.GetEnvironmentVariable("GMAIL_FROM_EMAIL"), smtpUser);

        if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPass) || string.IsNullOrWhiteSpace(fromEmail))
        {
            _logger.LogWarning("SMTP email is not configured. Set GMAIL_USER, GMAIL_APP_PASSWORD and GMAIL_FROM_EMAIL or Email section settings.");
            return false;
        }

        try
        {
            using var smtpClient = new SmtpClient(_emailOptions.SmtpHost, _emailOptions.SmtpPort)
            {
                EnableSsl = _emailOptions.EnableSsl,
                Credentials = new NetworkCredential(smtpUser, smtpPass)
            };

            var safeDisplayName = string.IsNullOrWhiteSpace(displayName) ? "there" : displayName;
            using var message = new MailMessage(fromEmail, toEmail)
            {
                Subject = "RouteX Login OTP",
                Body = $"Hi {safeDisplayName},\n\nYour RouteX OTP is: {otpCode}\n\nIt expires in 10 minutes.\n\n- RouteX Team",
                IsBodyHtml = false
            };

            await smtpClient.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email to {ToEmail}", toEmail);
            return false;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
