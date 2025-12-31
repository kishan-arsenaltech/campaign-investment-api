using Investment.Core.Entities;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Investment.Service.Services
{
    public class EmailJobService : IEmailJobService
    {
        private readonly IMailService _mailService;
        private readonly RepositoryContext _context;
        private readonly string baseUrl;
        private const string Day3 = "Day3";
        private const string Week2 = "Week2";

        public EmailJobService(IMailService mailService, RepositoryContext context, IConfiguration config)
        {
            _mailService = mailService;
            _context = context;

            var envName = config["environment:name"]?.ToLower();
            baseUrl = envName switch
            {
                "qa" => "",
                "prod" => "",
                _ => ""
            };
        }

        public async Task SendDafReminderEmailsAsync()
        {
            var logEntry = new SchedulerLogs
            {
                StartTime = DateTime.Now
            };

            int day3Count = 0;
            int week2Count = 0;

            try
            {
                var pendingGrants = await _context.PendingGrants
                                                .Where(x => x.status!.ToLower().Trim() == "pending"
                                                            && x.DAFProvider.ToLower().Trim() != "foundation grant"
                                                            && !string.IsNullOrWhiteSpace(x.DAFProvider)
                                                            && x.CreatedDate.HasValue
                                                            && (
                                                                    EF.Functions.DateDiffDay(x.CreatedDate.Value, DateTime.Now) == 3 ||
                                                                    EF.Functions.DateDiffDay(x.CreatedDate.Value, DateTime.Now) == 14
                                                               )
                                                            )
                                                .Include(x => x.User)
                                                .Include(x => x.Campaign)
                                                .Select(x => new
                                                {
                                                    Grant = x,
                                                    ReminderType = EF.Functions.DateDiffDay(x.CreatedDate!.Value, DateTime.Now) == 3
                                                                    ? Day3
                                                                    : Week2
                                                })
                                                .ToListAsync();

                var allEmailTasks = new List<Task>();

                foreach (var item in pendingGrants)
                {
                    var grant = item.Grant;
                    var reminderType = item.ReminderType;

                    ScheduledEmailLog? log = null;

                    if (reminderType == Day3 || reminderType == Week2)
                    {
                        log = new ScheduledEmailLog
                        {
                            PendingGrantId = grant.Id,
                            UserId = grant.UserId,
                            ReminderType = reminderType,
                            SentDate = DateTime.Now
                        };

                        await _context.AddAsync(log);
                        await _context.SaveChangesAsync();
                    }

                    try
                    {
                        if (reminderType == Day3)
                        {
                            day3Count++;
                            allEmailTasks.Add(
                                SendThreeDaysDAFEmail(
                                    grant.User.Email!,
                                    grant.User.FirstName!,
                                    grant.DAFProvider,
                                    Convert.ToDecimal(grant.Amount),
                                    grant.Campaign?.Name ?? string.Empty));
                        }

                        if (reminderType == Week2)
                        {
                            week2Count++;
                            allEmailTasks.Add(
                                SendTwoWeeksDAFEmail(
                                          grant.User.Email!,
                                          grant.User.FirstName!,
                                          grant.Campaign?.ContactInfoFullName ?? string.Empty,
                                          grant.DAFProvider,
                                          Convert.ToDecimal(grant.Amount),
                                          grant.Campaign?.Name ?? string.Empty,
                                          grant.Campaign?.Property ?? string.Empty));
                        }
                    }
                    catch (Exception ex)
                    {
                        if (log != null)
                        {
                            log.ErrorMessage = ex.Message;
                            _context.Update(log);
                            await _context.SaveChangesAsync();
                        }
                    }
                }

                _ = Task.Run(async () =>
                {
                    await Task.WhenAll(allEmailTasks);
                });
            }
            catch (Exception ex)
            {
                logEntry.ErrorMessage = ex.Message;
            }
            finally
            {
                logEntry.EndTime = DateTime.Now;
                logEntry.Day3EmailCount = day3Count;
                logEntry.Week2EmailCount = week2Count;

                await _context.AddAsync(logEntry);
                await _context.SaveChangesAsync();
            }
        }

        public async Task SendThreeDaysDAFEmail(string email, string firstName, string dafProviderName, decimal amount, string investmentName)
        {
            string formattedAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", amount);

            string investmentScenarios = !string.IsNullOrEmpty(investmentName)
                                            ? $"to <b>{investmentName}</b>"
                                            : "";

            string? dafLink = await GetDafLink(dafProviderName);

            var dafLinkScenarios = await GetDafLink(dafProviderName) != null
                                            ? $@"<a href='{dafLink}' target='_blank'>{dafProviderName}</a>"
                                            : dafProviderName;

            string subject = "⏳ A Quick Nudge – Your Grant Is Still Pending";

            var body = @$"
                        <p>Hi {firstName},</p>
                        <p>Thanks again for your generous <b>{formattedAmount}</b> commitment {investmentScenarios} — we’re excited to help move your capital to work!</p>  
                        <p>We noticed your donation is still marked as <b>pending</b>, so here’s a quick reminder on how to complete it:</p>
                        <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>                             
                        <p><div style='font-size: 20px;'><b>✅ How to Complete Your Grant</b></div></p>
                        <ol>
                            <li><b>Log in </b>to your {dafLinkScenarios} account</li>
                            <li><b>Initiate a grant </b>using the details below:</li>
                            <ul style='list-style-type:disc;'>
                                <li><b>Donation Recipient:</b> Donation Recipient</li>
                                <li><b>Amount:</b> {formattedAmount}</li>
                                <li><b>EIN:</b> 12-3456789</li>
                                <li><b>Email:</b> Email</li>
                                <li><b>Address:</b> Address</li>
                            </ul>
                            <li><b>Forward your grant confirmation</b> to <b>Email</b> so we can apply your investment without delay.</li>
                        </ol>
                        <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                        <p>We’re honored to work alongside you to fuel the future. Let’s unlock your impact — together. 💥</p>
                        <p style='margin-top: 0px;'><a href='{baseUrl}/settings' target='_blank'>Unsubscribe from notifications</a></p>
                        ";

            await _mailService.SendMailAsync(email, subject, "", body);
        }

        public async Task SendTwoWeeksDAFEmail(string email, string firstName, string investmentOwnerName, string dafProviderName, decimal amount, string investmentName, string investmentSlug)
        {
            string formattedAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", amount);

            string investmentScenarios = !string.IsNullOrEmpty(investmentName)
                                            ? $"in <b>{investmentName}</b>"
                                            : "";

            string investmentURL = $"{baseUrl}/invest/{investmentSlug}";

            string investmentFooterScenarios = !string.IsNullOrEmpty(investmentName)
                             ? @$"<p style='margin-bottom: 0px; margin-top: 0px;'><b>{investmentOwnerName}</b></p>
                                  <p style='margin-bottom: 0px; margin-top: 0px;'><a href='{investmentURL}' target='_blank'>{investmentURL}</a></p>"
                             : "";

            string? dafLink = await GetDafLink(dafProviderName);

            var dafLinkScenarios = await GetDafLink(dafProviderName) != null
                                                ? $@"<a href='{dafLink}' target='_blank'>{dafProviderName}</a>"
                                                : dafProviderName;

            string subject = "⏳ Still Pending – Help Us Activate Your Donation-to-Invest";

            var body = @$"
                        <p>Hi {firstName},</p>
                        <p>We’re honored by your commitment to invest <b>{formattedAmount}</b> {investmentScenarios} — thank you for being part of this movement.</p>  
                        <p>Your <b>donation to invest</b> is still marked as <b>pending</b>, so here’s a quick reminder on how to complete it:</p>
                        <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>                             
                        <p><div style='font-size: 20px;'><b>✅ How to Complete Your Donation</b></div></p>
                        <ol>
                            <li><b>Log in </b>to your {dafLinkScenarios} account</li>
                            <li><b>Initiate a donation </b>using the following details:</li>
                            <ul style='list-style-type:disc;'>
                                <li><b>Donation Recipient:</b> Donation Recipient</li>
                                <li><b>Amount:</b> {formattedAmount}</li>
                                <li><b>EIN:</b> 12-3456789</li>
                                <li><b>Email:</b> Email</li>
                                <li><b>Address:</b> Address</li>
                            </ul>
                            <li><b>Forward the confirmation email</b> to <b>Email</b> so we can apply your investment right away.</li>
                        </ol>
                        <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                        <p style='margin-bottom: 0px; margin-top: 0px;'>With gratitude,</p>
                        {investmentFooterScenarios}
                        <p style='margin-top: 0px;'><a href='{baseUrl}/settings' target='_blank'>Unsubscribe from notifications</a></p>
                        ";

            await _mailService.SendMailAsync(email, subject, "", body);
        }

        public async Task<string?> GetDafLink(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return null;

            var key = providerName.Trim().ToLowerInvariant();

            return await _context.DAFProviders
                                 .Where(x => x.ProviderName != null
                                            && x.IsActive
                                            && x.ProviderName.ToLower().Trim() == key)
                                 .Select(x => x.ProviderURL)
                                 .FirstOrDefaultAsync();
        }
    }
}
