using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Infrastructure.Configuration;

namespace SchoolManagement.Infrastructure.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, IReadOnlyCollection<EmailAttachment>? attachments = null)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning(
                "Email not sent (Email:Enabled is false). Set Email:Enabled to true in appsettings or appsettings.Development.json. To: {To}, subject: {Subject}",
                toEmail,
                subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            _logger.LogWarning("Email not sent: Email:FromEmail is empty. To: {To}", toEmail);
            throw new InvalidOperationException("Email:FromEmail is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogWarning("Email enabled but Host is empty. Skipping send to {Email}.", toEmail);
            throw new InvalidOperationException("Email:Host is not configured.");
        }

        var timeoutMs = Math.Clamp(_options.TimeoutSeconds, 5, 120) * 1000;

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = timeoutMs,
            UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(_options.Username))
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(toEmail));
        if (attachments is not null)
        {
            foreach (var a in attachments)
            {
                if (a?.Content is null || a.Content.Length == 0 || string.IsNullOrWhiteSpace(a.FileName)) continue;
                var stream = new MemoryStream(a.Content);
                var attachment = new Attachment(stream, a.FileName, a.ContentType ?? "application/octet-stream");
                message.Attachments.Add(attachment);
            }
        }

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex) when (ex is SmtpException or SocketException or IOException)
        {
            _logger.LogError(
                ex,
                "SMTP send failed to {To} via {Host}:{Port} (SSL={Ssl}). Inner: {Inner}",
                toEmail,
                _options.Host,
                _options.Port,
                _options.UseSsl,
                ex.InnerException?.Message ?? ex.Message);
            var hint = ex.InnerException is SocketException
                ? " Check Host/Port, firewall, VPN, and that the machine can reach the SMTP server on that port."
                : " Check Host, Port, UseSsl (587 + STARTTLS vs 465), username/password, and provider requirements.";
            throw new InvalidOperationException($"Could not send email via SMTP ({_options.Host}:{_options.Port}).{hint}", ex);
        }
    }
}
