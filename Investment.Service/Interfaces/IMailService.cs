using Azure.Communication.Email;

namespace Investment.Service.Interfaces
{
    public interface IMailService
    {
        Dictionary<string, int> ResetCodes { get; }
        Task<bool> SendResetMailAsync(string emailTo, string subject, string htmlContent);
        Task<bool> SendMailAsync(string emailTo, string subject, string plainText, string html, IEnumerable<EmailAttachment>? attachments = null);
        bool IsCodeCorrect(int code, string email);
    }
}
