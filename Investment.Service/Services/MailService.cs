using Azure;
using Azure.Communication.Email;
using Investment.Service.Interfaces;

namespace Investment.Service.Services
{
    public class MailService : IMailService
    {
        private string _communicationServiceConnectionString;
        private string _senderAddress;

        public Dictionary<string, int> ResetCodes { get; set; } = new Dictionary<string, int>();

        public MailService(string connectionString, string senderAddress)
        {
            _communicationServiceConnectionString = connectionString;
            _senderAddress = senderAddress;
        }

        private int GenerateCode()
        {
            Random rnd = new Random();
            return rnd.Next(111111, 999999);
        }

        public bool IsCodeCorrect(int code, string email)
        {
            if (ResetCodes.TryGetValue(email, out int storedCode))
            {
                return storedCode == code;
            }
            return false;
        }

        public async Task<bool> SendResetMailAsync(string emailTo, string subject, string htmlContent)
        {
            int code = GenerateCode();
            ResetCodes[emailTo] = code;

            var emailClient = new EmailClient(_communicationServiceConnectionString);

            EmailSendOperation emailSendOperation = await emailClient.SendAsync(WaitUntil.Started, _senderAddress,
                emailTo, subject + code.ToString(), htmlContent.Replace("CODE", code.ToString()), "");

            return emailSendOperation.HasCompleted;
        }

        public async Task<bool> SendMailAsync(string emailTo, string subject, string plainText, string html, IEnumerable<EmailAttachment>? attachments = null)
        {
            var emailContent = new EmailContent(subject)
            {
                PlainText = plainText,
                Html = html
            };
            var emailMessage = new EmailMessage(senderAddress: _senderAddress, recipientAddress: emailTo, content: emailContent);

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    emailMessage.Attachments.Add(attachment);
                }
            }

            var emailClient = new EmailClient(_communicationServiceConnectionString);

            EmailSendOperation emailSendOperation = await emailClient.SendAsync(WaitUntil.Completed, emailMessage);

            return emailSendOperation.HasCompleted;
        }
    }
}
