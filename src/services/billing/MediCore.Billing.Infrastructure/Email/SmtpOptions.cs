namespace MediCore.Billing.Infrastructure.Email;

public sealed class SmtpOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1025;
    public bool EnableSsl { get; init; }
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public string FromAddress { get; init; } = "noreply@medicore.local";
    public string FromName { get; init; } = "MediCore";
    public string? Username { get; init; }
    public string? Password { get; init; }
}
