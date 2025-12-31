using AutoMapper;
using ClosedXML.Excel;
using Invest.Core.Dtos;
using Invest.Core.Entities;
using Investment.Core.Dtos;
using Investment.Core.Entities;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Investment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RecommendationsController : ControllerBase
    {
        private readonly RepositoryContext _context;
        protected readonly IRepositoryManager _repository;
        private readonly IMapper _mapper;
        private readonly IMailService _mailService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RecommendationsController(RepositoryContext context, IRepositoryManager repositoryManager, IMapper mapper, IMailService mailService, IHttpContextAccessor httpContextAccessors)
        {
            _context = context;
            _repository = repositoryManager;
            _mapper = mapper;
            _mailService = mailService;
            _httpContextAccessor = httpContextAccessors;
        }

        [HttpPost("get-recommendations")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllRecommendations([FromBody] PaginationDto pagination)
        {
            bool isAsc = pagination?.SortDirection?.ToLower() == "asc";

            var statusList = pagination?.Status?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLower()).ToList();

            var query = _context.Recommendations
                                .Include(r => r.Campaign)
                                .Include(r => r.RejectedByUser)
                                .Select(r => new
                                {
                                    Id = r.Id,
                                    UserEmail = r.UserEmail,
                                    UserFullName = r.UserFullName,
                                    Status = r.Status,
                                    Amount = r.Amount,
                                    CampaignId = r.Campaign!.Id,
                                    CampaignName = r.Campaign.Name,
                                    RejectionMemo = r.RejectionMemo!,
                                    RejectedBy = r.RejectedByUser!.FirstName,
                                    DateCreated = r.DateCreated
                                })
                                .AsQueryable();

            if (pagination?.InvestmentId != null)
            {
                query = query.Where(x => x.CampaignId == pagination.InvestmentId);
            }
            if (statusList != null && statusList.Count > 0)
            {
                query = query.Where(x => !string.IsNullOrEmpty(x.Status) && statusList.Contains(x.Status.ToLower()));
            }

            var orderedQuery = pagination?.SortField?.ToLower() switch
            {
                "id" => isAsc ? query.OrderBy(r => r.Id) : query.OrderByDescending(r => r.Id),
                "userfullname" => isAsc ? query.OrderBy(r => r.UserFullName) : query.OrderByDescending(r => r.UserFullName),
                "status" => isAsc ? query.OrderBy(r => r.Status) : query.OrderByDescending(r => r.Status),
                "campaignname" => isAsc ? query.OrderBy(r => r.CampaignName) : query.OrderByDescending(r => r.CampaignName),
                "datecreated" => isAsc ? query.OrderBy(r => r.DateCreated) : query.OrderByDescending(r => r.DateCreated),
                _ => query.OrderByDescending(r => r.DateCreated)
            };

            var finalQuery = orderedQuery.ThenBy(r => r.Id);

            int page = pagination?.CurrentPage ?? 1;
            int pageSize = pagination?.PerPage ?? 50;

            int totalCount = await query.CountAsync();

            var pagedData = await finalQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            if (pagedData.Any())
                return Ok(new
                {
                    items = pagedData,
                    totalCount = totalCount
                });

            return Ok(new { Success = false, Message = "Data not found." });
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<RecommendationsDto>> GetRecommendation(string id)
        {
            int recommendationId = Int32.Parse(id);
            var recommendation = await _context.Recommendations
                                                .Where(item => item.Campaign != null)
                                                .Include(item => item.Campaign)
                                                .FirstOrDefaultAsync(item => item.Id == recommendationId);

            if (recommendation == null)
            {
                return BadRequest();
            }

            return Ok(new RecommendationsDto
            {
                Id = recommendation.Id,
                Amount = recommendation.Amount,
                CampaignId = recommendation.Campaign!.Id,
                CampaignName = recommendation.Campaign.Name,
                Status = recommendation.Status,
                UserEmail = recommendation.UserEmail,
                DateCreated = recommendation.DateCreated
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateRecommendation([FromBody] AddRecommendationDto addRecommendation)
        {
            CommonResponse response = new();
            try
            {
                var allEmailTasks = new List<Task>();

                User? user = null;

                if (addRecommendation.User != null)
                    user = addRecommendation.User!;
                else
                    user = await _context.Users.FirstOrDefaultAsync(i => i.Email == addRecommendation.UserEmail);

                var userId = user!.Id;
                var userFirstName = user?.FirstName;
                var userLastName = user?.LastName;

                CampaignDto campaign = addRecommendation.Campaign!;
                var campaignId = campaign?.Id;
                string? campaignName = campaign?.Name;
                string? campaignProperty = campaign?.Property;
                string? campaignDescription = campaign?.Description;
                var campaignAddedTotalAdminRaised = campaign?.AddedTotalAdminRaised;
                string? campaignContactInfoFullName = campaign?.ContactInfoFullName;
                string? campaignContactInfoEmailAddress = campaign?.ContactInfoEmailAddress;

                string investmentAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", Convert.ToDecimal(addRecommendation.Amount));

                var recommendation = _mapper.Map<AddRecommendationDto, Recommendation>(addRecommendation);
                recommendation.Status = "pending";
                recommendation.CampaignId = campaign!.Id;
                recommendation.UserId = userId;
                recommendation.DateCreated = DateTime.Now;
                if (user?.AccountBalance < recommendation.Amount && !addRecommendation.IsGroupAccountBalance)
                {
                    recommendation.Amount = user?.AccountBalance;
                }
                await _context.Recommendations.AddAsync(recommendation);
                await _context.SaveChangesAsync();

                decimal originalInvestmentAmount = Convert.ToDecimal(recommendation.Amount);

                var groupAccountBalances = await _context.GroupAccountBalance
                                            .Include(gab => gab.Group)
                                            .Where(gab => gab.User.Id == user!.Id)
                                            .OrderBy(gab => gab.Id)
                                            .ToListAsync();

                decimal totalGroupBalance = groupAccountBalances.Sum(gab => gab.Balance);
                decimal amountToDeduct = Convert.ToDecimal(recommendation.Amount);

                if (!addRecommendation.IsGroupAccountBalance)
                {
                    await AddPersonalDeduction(user!, amountToDeduct, campaignName!);
                }
                else
                {
                    amountToDeduct = await DeductFromGroupAccounts(user!, groupAccountBalances, amountToDeduct, campaignName!);

                    if (amountToDeduct > 0)
                    {
                        if (user!.AccountBalance < amountToDeduct)
                        {
                            decimal shortfall = Convert.ToDecimal(amountToDeduct) - Convert.ToDecimal(user.AccountBalance);
                            recommendation.Amount -= shortfall;
                            amountToDeduct = Convert.ToDecimal(user.AccountBalance);
                        }

                        await AddPersonalDeduction(user, amountToDeduct, campaignName!);
                    }
                }

                var usersToSendNotifications = await _context.Requests
                                                    .Where(i => i.UserToFollow != null
                                                                && i.UserToFollow.Id == userId
                                                                && i.Status == "accepted")
                                                    .Select(i => i.RequestOwner)
                                                    .ToListAsync();

                var notifications = usersToSendNotifications.Select(userToSend => new UsersNotification
                {
                    Title = "Recommendation created",
                    Description = $"Recommendation is created by user: {userFirstName} {userLastName}",
                    isRead = false,
                    PictureFileName = user?.PictureFileName != null ? user?.PictureFileName : null,
                    TargetUser = userToSend!,
                    UrlToRedirect = $"/investment/{campaignId}"
                }).ToList();

                await _context.UsersNotifications.AddRangeAsync(notifications);
                await _context.SaveChangesAsync();

                await _repository.UserAuthentication.UpdateUser(user!);
                await _repository.SaveAsync();


                var requestHeader = HttpContext?.Request.Headers["Origin"].ToString() == null ? _httpContextAccessor.HttpContext?.Request.Headers["Origin"].ToString() : HttpContext?.Request.Headers["Origin"].ToString();

                var userEmailsToSendEmailMessageCase = await _context.Requests
                                                        .Where(i => i.UserToFollow != null
                                                                    && i.RequestOwner != null
                                                                    && i.UserToFollow.Id == userId)
                                                        .Select(i => new UserEmailInfo
                                                        {
                                                            Email = i.RequestOwner!.Email,
                                                            FirstName = i.RequestOwner.FirstName!,
                                                            LastName = i.RequestOwner.LastName!
                                                        }).ToListAsync();
                var usersToSendEmailCase = await GetUsersForEmailsAsync(userEmailsToSendEmailMessageCase, null, true);
                var emailsForGroupAndUser = usersToSendEmailCase.Select(i => new { i.Email, i.FirstName, i.LastName }).Distinct().ToList();


                var userEmailsToSendEmailMessage = await _context.Requests
                                                        .Where(i => i.RequestOwner != null
                                                                    && i.UserToFollow != null
                                                                    && i.RequestOwner.Id == userId)
                                                        .Select(i => new { i.UserToFollow!.Email, i.UserToFollow.FirstName, i.UserToFollow.LastName })
                                                        .ToListAsync();
                var emails = userEmailsToSendEmailMessage.Select(i => i.Email);
                var usersToSendEmail = await _context.Users
                                            .Where(u => emails.Contains(u.Email) && (u.OptOutEmailNotifications == null || !(bool)u.OptOutEmailNotifications))
                                            .Select(u => u.Email)
                                            .ToListAsync();
                var rec = await _context.Recommendations.Where(i => i.Campaign != null && i.Campaign.Id == addRecommendation.Campaign!.Id && usersToSendEmail.Contains(i.UserEmail!)).ToListAsync();
                var uniqueRec = rec.DistinctBy(x => new { x.UserEmail }).ToList();


                var recommendationsQuery = _context.Recommendations
                                                .AsNoTracking()
                                                .Where(r =>
                                                    (r.Status == "approved" || r.Status == "pending") &&
                                                    r.Campaign != null &&
                                                    r.Campaign.Id == campaignId &&
                                                    r.Amount > 0 &&
                                                    r.UserEmail != null);

                var totalDonationAmount = await recommendationsQuery.SumAsync(r => r.Amount ?? 0) + (campaignAddedTotalAdminRaised ?? 0);
                var totalInvestors = await recommendationsQuery.Select(r => r.UserEmail).Distinct().CountAsync();

                _ = Task.Run(async () =>
                {
                    string emailLogoUrl = $"{requestHeader}/logo-for-email.png";
                    string emailLogo = $@"
                                    <div style='text-align: center;'>
                                        <a href='https://investment-campaign.org' target='_blank'>
                                            <img src='{emailLogoUrl}' alt='Logo' width='300' height='150' />
                                        </a>
                                    </div>";

                    var campaignIdentifier = campaignProperty ?? campaignId?.ToString();
                    string conditionalUserName = user?.IsAnonymousInvestment == true ? "Someone" : $"{userFirstName} {userLastName}";
                    string conditionalDonorName = user?.IsAnonymousInvestment == true ? "An anonymous Investment Campaign donor" : $"{userFirstName} {userLastName}";

                    var emailTaskForFollowing = emailsForGroupAndUser.Select(email =>
                    {
                        var subject = $"👀 {conditionalUserName} Just Invested in {campaignName}";

                        var body = emailLogo + $@"
                                            <p><b>Hi {email.FirstName},</b></p>
                                            <p>{conditionalDonorName} just made an investment in <b>{campaignName}</b> through Investment Campaign — and we thought you’d want to know.</p>
                                            <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                                            <p><div style='font-size: 20px;'><b>🌱 About {campaignName}</b></div></p>
                                            <p>{campaignDescription}</p>
                                            <p><b>🔗 Learn more here <a href='{requestHeader}/invest/{campaignIdentifier}'>Check it out</a>!</b></p>
                                            <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                                            <p>Thanks for being part of the growing Investment Campaign community — and for your commitment to moving capital toward real, scalable solutions.</p>
                                            <p style='margin-bottom: 0px;'><b>Onward,</b></p>
                                            <p style='margin-bottom: 0px; margin-top: 0px;'>The Investment Campaign Team</p>
                                            <p style='margin-top: 0px;'>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                            <p><a href='{requestHeader}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                        ";

                        return _mailService.SendMailAsync(email.Email, subject, "", body);

                    }).ToList();

                    allEmailTasks.AddRange(emailTaskForFollowing);


                    var emailTaskForMyFollowes = uniqueRec.Select(email =>
                    {
                        var subject = $"🎉 {conditionalUserName} Just Followed Your Lead on Investment Campaign!";
                        var body = emailLogo + $@"
                                        <p>Hi {email.UserFullName},</p>
                                        <p>Big news—you’re inspiring change!</p>
                                        <p>{conditionalUserName} who follows you on Investment Campaign, just made the same impact investment you did in {campaignName}. That’s right—your leadership is sparking action.</p>
                                        <p>By investing in solutions that advance <b>Gender Equity</b>,<b> Racial Justice</b>, and more, you're not just backing bold ideas—you’re motivating others to do the same. That’s real influence.</p>
                                        <p style='margin: 0px;'>Want to keep the momentum going?</p>
                                        <p>Share the love and invite others to join you in investing in <b>{campaignName}</b>:</p>
                                        <p>👉 <a href='{requestHeader}/invest/{campaignIdentifier}'>{campaignName}</a></p>
                                        <p>Thanks for being a changemaker. We’re lucky to have you in the Investment Campaign community.</p>
                                        <p style='margin-bottom: 0px;'>Onward,</p>
                                        <p style='margin-bottom: 0px; margin-top: 0px;'>The Investment Campaign Team</p>
                                        <p style='margin-top: 0px;'>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                        <p><a href='{requestHeader}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                        ";

                        return _mailService.SendMailAsync(email.UserEmail!, subject, "", body);

                    }).ToList();

                    allEmailTasks.AddRange(emailTaskForMyFollowes);


                    if (!addRecommendation.IsRequestForInTransit)
                    {
                        if (user!.OptOutEmailNotifications == null || !(bool)user.OptOutEmailNotifications)
                        {
                            var subject = "Thank You for Fueling Impact with Your Donation";
                            var body = emailLogo + $@"
                                                <p><b>Hi {user.FirstName},</b></p>
                                                <p>Thank you for your generous <b>contribution of {investmentAmount}</b> to Investment Campaign and your recommendation that it be allocated toward <b>{campaignName}</b>.</p>
                                                <p>Your donation doesn’t just sit still — it goes to work, helping unlock capital for the most innovative and underfunded solutions on the planet. You’re helping drive real change by putting your capital where it counts.</p>
                                                <p>Please keep this message for your tax records.</p>
                                                <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                                                <p><div style='font-size: 17px;'><b>Donation/Investment Summary</b></div></p>
                                                <p style='margin-bottom: 0px;'><b>Recipient:</b> Investment Campaign</p>
                                                <p style='margin-bottom: 0px; margin-top: 0px;'><b>EIN:</b> 86-2370923<br/></p>
                                                <p style='margin-top: 0px;'><b>Address:</b> 213 West 85th Street, New York, NY 10024</p>
                                                <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                                                <p>Thank you for being part of this movement — and for backing the future we all want to live in.</p>
                                                <p style='margin-bottom: 0px;'><b>Let’s keep building, together.</b></p>
                                                <p style='margin-bottom: 0px; margin-top: 0px;'>Warmly,</p>
                                                <p style='margin-bottom: 0px; margin-top: 0px;'>Shabbir Bharmal</p>
                                                <p style='margin-top: 0px;'>Co-Founder, Investment Campaign</p>
                                                <p style='margin-bottom: 0px;'>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                                <p style='margin-top: 0px;'><a href='{requestHeader}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                            ";

                            allEmailTasks.Add(_mailService.SendMailAsync(user.Email, subject, "", body));
                        }
                    }

                    if (!string.IsNullOrEmpty(campaignContactInfoEmailAddress))
                    {
                        string formattedOriginalInvestmentAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", originalInvestmentAmount);
                        string formattedtotalDonationAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", totalDonationAmount);
                        string investorName = user?.IsAnonymousInvestment == true ? "a donor-investor" : $"{userFirstName} {userLastName}";

                        var subject = "You Got Funded! Your Investment Campaign Is Growing";

                        var body = emailLogo + $@"
						                    <p><b>Hi {campaignContactInfoFullName?.Split(' ')[0]},</b></p>
						                    <p>Great news — <b>{investorName}</b> just contributed <b>{formattedOriginalInvestmentAmount}</b> to your investment on Investment Campaign!</p>
						                    <p>Your total raised is now <b>{formattedtotalDonationAmount}</b> from <b>{totalInvestors} incredible supporters</b> who believe in your mission. Every dollar is a vote of confidence in the impact you’re creating — and momentum is building.</p>
						                    <p>🔗 <a href='{requestHeader}/invest/{campaignIdentifier}'>View your live investment page</a></p>
						                    <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
						                    <p><div style='font-size: 20px;'><b>📣 Keep the Momentum Flowing</b></div></p>
						                    <p>This is the perfect time to <b>share your page</b> with your network and invite others to join you. The more visibility your campaign has, the more catalytic it becomes. Check out your Investment Success Toolkit here.</p>
						                    <p>We’re here to help — whether you need:</p>
						                    <ul style='list-style-type:disc;'>
							                    <li>Messaging support for an update</li>
							                    <li>Share graphics or templates</li>
							                    <li>Ideas to activate new networks</li>
						                    </ul>
						                    <p style='margin-top: 8px'><b>Let’s keep it going. Your impact deserves the spotlight.</b></p>
                                            <p style='margin-bottom: 0px;'>With deep gratitude,</p>
						                    <p style='margin-top: 0px;'>— The Investment Campaign Team</p>
						                    <p>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a><br/>
						                    <p><a href='{requestHeader}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
						                ";

                        allEmailTasks.Add(_mailService.SendMailAsync(campaignContactInfoEmailAddress, subject, "", body));
                    }
                    await Task.WhenAll(allEmailTasks);
                });

                response.Success = true;
                response.Message = "Recommendation created successfully.";

                if (user != null)
                    response.Data = _mapper.Map<UserDetailsDto>(user);

                return Ok(response);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"An error occurred: {ex.Message}";
                return BadRequest(response);
            }
        }

        private async Task AddPersonalDeduction(User user, decimal amount, string investmentName)
        {
            var identity = HttpContext?.User.Identity as ClaimsIdentity == null ? _httpContextAccessor.HttpContext?.User.Identity as ClaimsIdentity : HttpContext.User.Identity as ClaimsIdentity;
            var loginUserId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            var loginUser = await _repository.UserAuthentication.GetUserById(loginUserId!);
            bool isAdmin = identity?.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "Admin") == true;

            var log = new AccountBalanceChangeLog
            {
                UserId = user.Id,
                PaymentType = isAdmin ? $"Manually, {loginUser.UserName.Trim().ToLower()}" : "Manually",
                OldValue = user.AccountBalance,
                UserName = user.UserName,
                NewValue = user.AccountBalance - amount,
                InvestmentName = investmentName
            };

            user.AccountBalance -= amount;
            await _context.AccountBalanceChangeLogs.AddAsync(log);
        }

        private async Task<decimal> DeductFromGroupAccounts(User user, List<GroupAccountBalance> balances, decimal amount, string investmentName)
        {
            var identity = HttpContext?.User.Identity as ClaimsIdentity == null ? _httpContextAccessor.HttpContext?.User.Identity as ClaimsIdentity : HttpContext.User.Identity as ClaimsIdentity;
            var loginUserId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            var loginUser = await _repository.UserAuthentication.GetUserById(loginUserId!);
            bool isAdmin = identity?.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "Admin") == true;

            foreach (var gab in balances)
            {
                if (amount <= 0) break;
                if (gab.Balance <= 0) continue;

                decimal deduction = Math.Min(gab.Balance, amount);

                var log = new AccountBalanceChangeLog
                {
                    UserId = user.Id,
                    PaymentType = isAdmin ? $"Manually, {loginUser.UserName.Trim().ToLower()}" : "Manually",
                    OldValue = gab.Balance,
                    UserName = user.UserName,
                    NewValue = gab.Balance - deduction,
                    InvestmentName = investmentName,
                    GroupId = gab.Group.Id
                };

                gab.Balance -= deduction;
                amount -= deduction;

                await _context.AccountBalanceChangeLogs.AddAsync(log);
            }

            return amount;
        }

        [HttpPost("feedback")]
        //[Authorize(Roles = "User, Admin")]
        public async Task<IActionResult> CreateFeedback([FromBody] InvestmentFeedbackDto investmentFeedback)
        {
            string userId = string.Empty;

            if (!string.IsNullOrEmpty(investmentFeedback.Email))
            {
                userId = _context.Users.Where(x => x.Email == investmentFeedback.Email).Select(x => x.Id).FirstOrDefault()!;
            }
            else
            {
                if (!string.IsNullOrEmpty(investmentFeedback.Username))
                {
                    var user = await _repository.UserAuthentication.GetUserByUserName(investmentFeedback.Username);
                    userId = user.Id;
                }
                else
                {
                    var identity = HttpContext.User.Identity as ClaimsIdentity;
                    if (identity != null)
                    {
                        userId = identity.Claims.FirstOrDefault(i => i.Type == "id")?.Value!;
                    }
                }
            }
            if (!string.IsNullOrEmpty(userId))
            {
                var userData = await _context.Users.FirstOrDefaultAsync(i => i.Id == userId);

                investmentFeedback.UserId = userId;
                var result = _mapper.Map<InvestmentFeedbackDto, InvestmentFeedback>(investmentFeedback);

                await _context.InvestmentFeedback.AddAsync(result);
                await _context.SaveChangesAsync();
                return Ok();
            }
            else
            {
                return Unauthorized();
            }
        }

        [HttpPut("update-recommendation")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateRecommendation([FromBody] RecommendationsDto data)
        {
            try
            {
                if (data == null)
                    return BadRequest(new { Success = false, Message = "Data type is invalid" });

                var recommendation = await _context.Recommendations
                                                    .Include(item => item.Campaign)
                                                    .Include(item => item.RejectedByUser)
                                                    .FirstOrDefaultAsync(item => item.Id == data.Id);
                if (recommendation == null)
                    return Ok(new { Success = false, Message = "Recommendation data not found" });

                var user = await _repository.UserAuthentication.GetUserByEmail(recommendation?.UserEmail!);
                if (user == null)
                    return Ok(new { Success = false, Message = "Recommendation cannot be rejected because the user does not exist" });

                var identity = HttpContext.User.Identity as ClaimsIdentity;
                var loginUserId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;

                recommendation!.Amount = data.Amount;
                recommendation.Status = data.Status;
                recommendation.UserEmail = data.UserEmail;

                if (recommendation.Status == "rejected")
                {
                    var log = new AccountBalanceChangeLog
                    {
                        UserId = user.Id,
                        PaymentType = $"Reverted Recommendation Amount, Recommendation Id= {recommendation?.Id}",
                        InvestmentName = recommendation?.Campaign?.Name,
                        OldValue = user.AccountBalance,
                        UserName = user.UserName,
                        NewValue = user.AccountBalance + recommendation?.Amount
                    };
                    await _context.AccountBalanceChangeLogs.AddAsync(log);

                    user.AccountBalance += recommendation?.Amount;
                    await _repository.UserAuthentication.UpdateUser(user);

                    recommendation!.RejectionMemo = data.RejectionMemo != string.Empty ? data.RejectionMemo?.Trim() : null;
                    recommendation.RejectedBy = loginUserId!;
                    recommendation.RejectionDate = DateTime.Now;
                }
                await _repository.SaveAsync();

                var rejectingUser = await _repository.UserAuthentication.GetUserById(loginUserId!);

                return Ok(new
                {
                    Success = true,
                    Message = "Recommendation status updated successfully.",
                    Data = new
                    {
                        Status = data.Status,
                        RejectedBy = rejectingUser.UserName.Trim().ToLower(),
                        RejectionMemo = recommendation?.RejectionMemo
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpGet("Export")]
        public async Task<IActionResult> GetExportRecommendations()
        {
            var recommendations = await _context.Recommendations
                                                .Include(r => r.Campaign)
                                                .Include(r => r.RejectedByUser)
                                                .ToListAsync();

            recommendations = recommendations.OrderByDescending(d => d.Id).ToList();

            string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            string fileName = "Recommendations.xlsx";

            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet worksheet = workbook.Worksheets.Add("Recommendations");

                worksheet.Cell(1, 1).Value = "Id";
                worksheet.Cell(1, 2).Value = "UserFullName";
                worksheet.Cell(1, 3).Value = "UserEmail";
                worksheet.Cell(1, 4).Value = "InvestmentName";
                worksheet.Cell(1, 5).Value = "Amount";
                worksheet.Cell(1, 6).Value = "DateCreated";
                worksheet.Cell(1, 7).Value = "Status";
                worksheet.Cell(1, 8).Value = "RejectionMemo";
                worksheet.Cell(1, 9).Value = "RejectedBy";
                worksheet.Cell(1, 10).Value = "RejectionDate";

                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                worksheet.Columns().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                for (int index = 0; index < recommendations.Count; index++)
                {
                    worksheet.Cell(index + 2, 1).Value = recommendations[index].Id;
                    worksheet.Cell(index + 2, 2).Value = recommendations[index].UserFullName;
                    worksheet.Cell(index + 2, 3).Value = recommendations[index].UserEmail;
                    worksheet.Cell(index + 2, 4).Value = recommendations[index].Campaign!.Name;
                    worksheet.Cell(index + 2, 5).Value = recommendations[index].Amount;
                    worksheet.Cell(index + 2, 6).Value = recommendations[index].DateCreated;
                    worksheet.Cell(index + 2, 7).Value = recommendations[index].Status;
                    worksheet.Cell(index + 2, 8).Value = recommendations[index].RejectionMemo;
                    worksheet.Cell(index + 2, 9).Value = recommendations[index].RejectedByUser?.FirstName != null ? recommendations[index].RejectedByUser!.FirstName : null;
                    worksheet.Cell(index + 2, 10).Value = recommendations[index].RejectionDate;
                    worksheet.Cell(index + 2, 10).Style.DateFormat.Format = "MM/dd/yyyy";
                }

                worksheet.Columns().AdjustToContents();

                foreach (var column in worksheet.Columns())
                {
                    column.Width += 10;
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, contentType, fileName);
                }
            }
        }

        [HttpGet("investment-recommendations-export")]
        public async Task<IActionResult> ExportInvestmentRecommendations(int investmentId)
        {
            try
            {
                var recommendations = await _context.Recommendations
                                                .Include(x => x.Campaign)
                                                .Include(x => x.PendingGrants)
                                                .Where(x => x.CampaignId == investmentId
                                                            && (x.Status!.ToLower().Trim() == "pending"
                                                                || x.Status!.ToLower().Trim() == "approved"))
                                                .OrderByDescending(x => x.Id)
                                                .ToListAsync();

                if (!recommendations.Any())
                    return Ok(new { Success = false, Message = "There are no recommendations to export for your investment." });

                string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                string fileName = "Recommendations.xlsx";

                using (var workbook = new XLWorkbook())
                {
                    IXLWorksheet worksheet = workbook.Worksheets.Add("Recommendations");

                    worksheet.Cell(1, 1).Value = "UserFullName";
                    worksheet.Cell(1, 2).Value = "InvestmentName";
                    worksheet.Cell(1, 3).Value = "Amount";
                    worksheet.Cell(1, 4).Value = "DateCreated";
                    worksheet.Cell(1, 5).Value = "PendingGrant?";

                    var headerRow = worksheet.Row(1);
                    headerRow.Style.Font.Bold = true;
                    worksheet.Columns().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    for (int index = 0; index < recommendations.Count; index++)
                    {
                        var dto = recommendations[index];
                        int row = index + 2;
                        int col = 1;

                        worksheet.Cell(row, col++).Value = dto.UserFullName;
                        worksheet.Cell(row, col++).Value = dto.Campaign!.Name;

                        var amountCell = worksheet.Cell(row, col++);
                        amountCell.Value = dto.Amount;
                        amountCell.Style.NumberFormat.Format = "$#,##0.00";

                        var dateCreatedCell = worksheet.Cell(row, col++);
                        dateCreatedCell.Value = dto.DateCreated;
                        dateCreatedCell.Style.DateFormat.Format = "MM/dd/yy HH:mm";

                        worksheet.Cell(row, col++).Value = dto.PendingGrants != null ? "Yes" : "";
                    }

                    worksheet.Columns().AdjustToContents();

                    foreach (var column in worksheet.Columns())
                    {
                        column.Width += 5;
                    }

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();
                        return File(content, contentType, fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        private async Task<List<UserEmailInfo>> GetUsersForEmailsAsync(
            IEnumerable<UserEmailInfo> users,
            bool? emailFromGroupsOn,
            bool? emailFromUsersOn)
        {
            var emails = users.Select(u => u.Email).ToList();

            return await _context.Users
                                .Where(u => emails.Contains(u.Email) &&
                                            (u.OptOutEmailNotifications == null || !(u.OptOutEmailNotifications)) &&
                                            (
                                                (emailFromGroupsOn == true && (u.EmailFromGroupsOn)) ||
                                                (emailFromUsersOn == true && (u.EmailFromUsersOn))
                                            ))
                                .Select(u => new UserEmailInfo
                                {
                                    Email = u.Email,
                                    FirstName = u.FirstName!,
                                    LastName = u.LastName!
                                })
                                .ToListAsync();
        }
    }
}
