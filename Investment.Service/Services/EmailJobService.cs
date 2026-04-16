using Invest.Core.Settings;
using Investment.Core.Constants;
using Investment.Core.Entities;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Investment.Service.Services
{
    public class EmailJobService : IEmailJobService
    {
        private readonly IMailService _mailService;
        private readonly RepositoryContext _context;
        private readonly AppSecrets _appSecrets;
        private readonly EmailQueue _emailQueue;
        private const string Day3 = "Day3";
        private const string Week2 = "Week2";

        public EmailJobService(IMailService mailService, RepositoryContext context, AppSecrets appSecrets, EmailQueue emailQueue)
        {
            _mailService = mailService;
            _context = context;
            _appSecrets = appSecrets;
            _emailQueue = emailQueue;
        }

        public async Task SendReminderEmailsAsync(string jobName)
        {
            int day3Count = 0;
            int week2Count = 0;

            var logEntry = new SchedulerLogs
            {
                StartTime = DateTime.Now,
                JobName = jobName
            };

            try
            {
                var pendingGrants = new List<PendingGrants>();

                if (_appSecrets.IsProduction)
                {
                    pendingGrants = await _context.PendingGrants
                                                  .Where(x =>
                                                      x.status == "pending" &&
                                                      x.CreatedDate.HasValue &&
                                                      (
                                                        EF.Functions.DateDiffDay(x.CreatedDate.Value, DateTime.Now) == 3 ||
                                                        EF.Functions.DateDiffDay(x.CreatedDate.Value, DateTime.Now) == 14)
                                                      )
                                                  .Include(x => x.User)
                                                  .Include(x => x.Campaign)
                                                  .ToListAsync();
                }
                else
                {
                    var emails = _appSecrets.EmailListForScheduler
                                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                            .Select(e => e.Trim().ToLower())
                                            .ToList();

                    pendingGrants = await _context.PendingGrants
                                                  .Where(x =>
                                                      x.status == "pending" &&
                                                      x.CreatedDate.HasValue &&
                                                      (
                                                          EF.Functions.DateDiffDay(x.CreatedDate.Value, DateTime.Now) == 3 ||
                                                          EF.Functions.DateDiffDay(x.CreatedDate.Value, DateTime.Now) == 14
                                                      ) &&
                                                      x.User.Email != null &&
                                                      emails.Contains(x.User.Email.ToLower())
                                                  )
                                                  .Include(x => x.User)
                                                  .Include(x => x.Campaign)
                                                  .ToListAsync();
                }

                var allEmailTasks = new List<Task>();
                var emailLogs = new List<ScheduledEmailLog>();

                foreach (var grant in pendingGrants)
                {
                    var daysDiff = (DateTime.Now.Date - grant.CreatedDate!.Value.Date).Days;
                    string reminderType = daysDiff == 3 ? Day3 : Week2;

                    if (reminderType == Day3) day3Count++;
                    if (reminderType == Week2) week2Count++;

                    var log = new ScheduledEmailLog
                    {
                        PendingGrantId = grant.Id,
                        UserId = grant.UserId,
                        ReminderType = reminderType,
                        SentDate = DateTime.Now
                    };
                    emailLogs.Add(log);

                    try
                    {
                        var dafProvider = grant.DAFProvider?.Trim().ToLower();

                        if (!string.IsNullOrWhiteSpace(dafProvider) && dafProvider != "foundation grant")
                        {
                            string? dafProviderLink = await GetDafLink(dafProvider!);

                            await SendDAFEmail(
                                reminderType,
                                grant.User.Email!,
                                grant.User.FirstName!,
                                Convert.ToDecimal(grant.Amount),
                                grant.DAFProvider?.Trim()!,
                                grant.DAFName,
                                dafProviderLink,
                                grant.Campaign?.Name ?? string.Empty,
                                grant.Campaign?.ContactInfoFullName ?? string.Empty,
                                grant.Campaign?.Property ?? string.Empty);
                        }
                        else if (dafProvider == "foundation grant")
                        {
                            allEmailTasks.Add(
                                    SendFoundationEmail(
                                        reminderType,
                                        grant.User.Email!,
                                        grant.User.FirstName!,
                                        Convert.ToDecimal(grant.Amount),
                                        grant.Campaign?.Name ?? string.Empty,
                                        grant.Campaign?.ContactInfoFullName ?? string.Empty,
                                        grant.Campaign?.Property ?? string.Empty));
                        }
                    }
                    catch (Exception ex)
                    {
                        log.ErrorMessage = ex.Message;
                    }
                }

                await _context.AddRangeAsync(emailLogs);
                await _context.SaveChangesAsync();

                _ = Task.Run(async () =>
                {
                    await Task.WhenAll(allEmailTasks);
                });
            }
            catch (Exception ex)
            {
                logEntry.ErrorMessage = ex.ToString();
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

        public async Task SendDAFEmail(
            string reminderType,
            string email,
            string firstName,
            decimal amount,
            string dafProviderName,
            string? dafName,
            string? dafProviderLink,
            string investmentName,
            string investmentOwnerName,
            string investmentSlug)
        {
            EmailTemplateCategory category;

            if (dafProviderName == "ImpactAssets")
            {
                category = reminderType == Day3
                    ? EmailTemplateCategory.DAFReminderImpactAssetsDay3
                    : EmailTemplateCategory.DAFReminderImpactAssetsWeek2;
            }
            else
            {
                category = reminderType == Day3
                    ? EmailTemplateCategory.DAFReminderDay3
                    : EmailTemplateCategory.DAFReminderWeek2;
            }

            string formattedAmount = string.Format(CultureInfo.GetCultureInfo("en-US"), "${0:N2}", amount);

            var variables = new Dictionary<string, string>
            {
                { "firstName", firstName },
                { "amount", formattedAmount },
                { "investmentScenario", investmentName },
                { "dafProviderName", dafProviderName },
                { "dafProviderLink", dafProviderLink ?? "" },
                { "dafName", dafName ?? dafProviderName },
                { "investmentOwnerName", investmentOwnerName },
                { "investmentUrl", $"{_appSecrets.RequestOrigin}/investments/{investmentSlug}" },
                { "unsubscribeUrl", $"{_appSecrets.RequestOrigin}/settings" }
            };

            _emailQueue.QueueEmail(async (sp) =>
            {
                var emailService = sp.GetRequiredService<IEmailTemplateService>();

                await emailService.SendTemplateEmailAsync(
                    category,
                    email,
                    variables
                );
            });
        }

        public async Task SendFoundationEmail(string reminderType, string email, string firstName, decimal amount, string investmentName, string investmentOwnerName, string investmentSlug)
        {
            string formattedAmount = string.Format(CultureInfo.GetCultureInfo("en-US"), "${0:N2}", amount);

            string investmentScenario = !string.IsNullOrEmpty(investmentName)
                                        ? $"to <b>{investmentName}</b>"
                                        : "to Investment";

            var variables = new Dictionary<string, string>
            {
                { "firstName", firstName },
                { "amount", formattedAmount },
                { "investmentScenario", investmentScenario },
                { "investmentOwnerName", investmentOwnerName },
                { "investmentUrl", $"{_appSecrets.RequestOrigin}/investments/{investmentSlug}" },
                { "unsubscribeUrl", $"{_appSecrets.RequestOrigin}/settings" }
            };

            EmailTemplateCategory category = reminderType == Day3
                                                ? EmailTemplateCategory.FoundationReminderDay3
                                                : EmailTemplateCategory.FoundationReminderWeek2;

            _emailQueue.QueueEmail(async (sp) =>
            {
                var emailService = sp.GetRequiredService<IEmailTemplateService>();

                await emailService.SendTemplateEmailAsync(
                    category,
                    email,
                    variables
                );
            });
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
