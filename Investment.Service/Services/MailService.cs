using Azure;
using Azure.Communication.Email;
using Investment.Core.Dtos;
using Investment.Service.Interfaces;
using System.Net;
using System.Net.Mail;

namespace Investment.Service.Services
{
    public class MailService : IMailService
    {
        private string _communicationServiceConnectionString;
        private string _senderAddress;
        private string _gmailSMTPUser;
        private string _gmailSMTPPassword;
        private bool _isProduction;

        public Dictionary<string, VerificationCodeDto> ResetCodes { get; set; } = new();

        public MailService(string connectionString, string senderAddress, string gmailSMTPUser, string gmailSMTPPassword, bool isProduction)
        {
            _communicationServiceConnectionString = connectionString;
            _senderAddress = senderAddress;
            _gmailSMTPUser = gmailSMTPUser;
            _gmailSMTPPassword = gmailSMTPPassword;
            _isProduction = isProduction;
        }

        public int GenerateCode(string email)
        {
            Random rnd = new Random();
            int code = rnd.Next(111111, 999999);
            ResetCodes[email] = new VerificationCodeDto
            {
                Code = code,
                Expiry = DateTime.Now.AddMinutes(5)
            };

            return code;
        }

        public bool IsCodeCorrect(int code, string email)
        {
            if (ResetCodes.TryGetValue(email, out var storedCode))
            {
                if (storedCode.Expiry < DateTime.Now)
                {
                    ResetCodes.Remove(email);
                    return false;
                }

                if (storedCode.Code == code)
                {
                    ResetCodes.Remove(email);
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> SendMailAsync(string emailTo, string subject, string plainText, string html, IEnumerable<EmailAttachment>? attachments = null, IEnumerable<string>? cc = null)
        {
            if (_isProduction)
                return await SendUsingAzure(emailTo, subject, plainText, html, attachments, cc);

            return await SendUsingGmail(emailTo, subject, html, cc);
        }

        private async Task<bool> SendUsingGmail(string emailTo, string subject, string html, IEnumerable<string>? cc)
        {
            var message = new MailMessage();
            message.From = new MailAddress("testing@gmail.com", "QA");
            message.To.Add(emailTo);
            message.Subject = subject;
            message.Body = html;
            message.IsBodyHtml = true;

            var smtp = new SmtpClient("smtp.gmail.com", 000);
            smtp.Credentials = new NetworkCredential(_gmailSMTPUser, _gmailSMTPPassword);
            smtp.EnableSsl = true;
            smtp.UseDefaultCredentials = false;

            if (cc != null)
            {
                foreach (var ccAddress in cc)
                    message.CC.Add(ccAddress);
            }

            await smtp.SendMailAsync(message);

            return true;
        }

        private async Task<bool> SendUsingAzure(string emailTo, string subject, string plainText, string html, IEnumerable<EmailAttachment>? attachments, IEnumerable<string>? cc)
        {
            var emailContent = new EmailContent(subject)
            {
                PlainText = plainText,
                Html = html
            };

            var emailMessage = new EmailMessage(senderAddress: _senderAddress, recipientAddress: emailTo, content: emailContent);

            if (cc != null)
            {
                foreach (var ccAddress in cc)
                    emailMessage.Recipients.CC.Add(new EmailAddress(ccAddress));
            }

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                    emailMessage.Attachments.Add(attachment);
            }

            var emailClient = new EmailClient(_communicationServiceConnectionString);

            EmailSendOperation operation = await emailClient.SendAsync(WaitUntil.Completed, emailMessage);

            return operation.HasCompleted;
        }
    }
}
