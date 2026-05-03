using System.Threading.Tasks;

namespace SchoolManagement.Application.Abstractions;

public interface IWhatsAppSender
{
    Task SendTextAsync(string toPhoneNumber, string message);
}
