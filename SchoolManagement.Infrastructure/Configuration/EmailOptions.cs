namespace SchoolManagement.Infrastructure.Configuration;

public class EmailOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    /// <summary>SMTP connect/send timeout in seconds (default 30).</summary>
    public int TimeoutSeconds { get; set; } = 30;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "School Management";
    public bool Enabled { get; set; } = false;
}
