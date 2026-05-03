using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolManagement.Application.Abstractions;
using SchoolManagement.Infrastructure.Configuration;

namespace SchoolManagement.Infrastructure.Services;

public class MetaWhatsAppSender : IWhatsAppSender
{
    private readonly IOptions<WhatsAppOptions> _options;
    private readonly ILogger<MetaWhatsAppSender> _logger;

    public MetaWhatsAppSender(
        IOptions<WhatsAppOptions> options,
        ILogger<MetaWhatsAppSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendTextAsync(string toPhoneNumber, string message)
    {
        var opts = _options.Value;
        if (!opts.Enabled
            || string.IsNullOrWhiteSpace(opts.AccessToken)
            || string.IsNullOrWhiteSpace(opts.PhoneNumberId))
        {
            _logger.LogDebug("WhatsApp send skipped (disabled or not configured).");
            return;
        }

        if (string.IsNullOrWhiteSpace(toPhoneNumber))
            return;

        var normalized = new string(toPhoneNumber.Trim().Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (normalized.StartsWith('+'))
            normalized = normalized[1..];

        var url =
            $"https://graph.facebook.com/{opts.ApiVersion.Trim('/')}/{opts.PhoneNumberId}/messages";

        var payload = JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            to = normalized,
            type = "text",
            text = new { preview_url = false, body = message }
        });

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.AccessToken);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("WhatsApp API error {Status}: {Body}", resp.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp send failed.");
        }
    }
}
