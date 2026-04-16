using Azure.Communication.Email;
using Investment.Core.Constants;

namespace Investment.Service.Interfaces
{
    public interface IEmailTemplateService
    {
        Task SendTemplateEmailAsync(EmailTemplateCategory category, string toEmail, Dictionary<string, string> variables, string subjectPrefix = "", List<EmailAttachment>? attachments = null);
    }
}
