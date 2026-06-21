namespace DotnetRoutix.Server.Infrastructure.Configuration;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = "gmail";

    public string GmailUser { get; set; } = string.Empty;

    public string GmailAppPassword { get; set; } = string.Empty;

    public string GmailFromEmail { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = "smtp.gmail.com";

    public int SmtpPort { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;
}
