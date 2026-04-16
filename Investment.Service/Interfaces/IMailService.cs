using Azure.Communication.Email;
using Investment.Core.Dtos;

namespace Investment.Service.Interfaces
{
    public interface IMailService
    {
        Dictionary<string, VerificationCodeDto> ResetCodes { get; }
        Task<bool> SendMailAsync(string emailTo, string subject, string plainText, string html, IEnumerable<EmailAttachment>? attachments = null, IEnumerable<string>? cc = null);
        bool IsCodeCorrect(int code, string email);
        int GenerateCode(string email);
    }
}
