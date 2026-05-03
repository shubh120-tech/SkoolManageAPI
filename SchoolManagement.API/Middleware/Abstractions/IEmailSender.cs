using System.Threading.Tasks;
using System.Collections.Generic;

namespace SchoolManagement.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        IReadOnlyCollection<EmailAttachment>? attachments = null);
}
