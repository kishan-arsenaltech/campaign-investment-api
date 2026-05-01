using Azure.Communication.Email;
using Investment.Core.Constants;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace Invest.Service.Services
{
    public class EmailTemplateService : IEmailTemplateService
    {
        private readonly RepositoryContext _context;
        private readonly IMailService _mailService;

        public EmailTemplateService(RepositoryContext context, IMailService mailService)
        {
            _context = context;
            _mailService = mailService;
        }

        private string ReplaceVariables(string content, Dictionary<string, string> variables)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            content = WebUtility.HtmlDecode(content);

            if (variables == null || !variables.Any())
                return content;

            foreach (var variable in variables)
                content = content.Replace($"{{{{{variable.Key}}}}}", variable.Value ?? "");

            return content;
        }

        public async Task SendTemplateEmailAsync(EmailTemplateCategory category, string toEmail, Dictionary<string, string> variables, string subjectPrefix = "", List<EmailAttachment>? attachments = null)
        {
            var template = await _context.EmailTemplate.FirstOrDefaultAsync(x => x.Category == category);

            if (template == null)
                throw new Exception("Email template not found.");

            string subject = subjectPrefix + ReplaceVariables(template.Subject, variables);
            string body = ReplaceVariables(template.BodyHtml, variables);

            await _mailService.SendMailAsync(toEmail, subject, "", body, attachments);
        }
    }
}
