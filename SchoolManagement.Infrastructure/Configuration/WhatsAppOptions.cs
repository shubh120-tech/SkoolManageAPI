namespace SchoolManagement.Infrastructure.Configuration;

public class WhatsAppOptions
{
    /// <summary>When false, <see cref="MetaWhatsAppSender"/> does not call the API.</summary>
    public bool Enabled { get; set; }

    /// <summary>Meta Graph API phone number ID.</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>Meta Graph API access token.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Graph API version segment, e.g. v21.0.</summary>
    public string ApiVersion { get; set; } = "v21.0";
}
