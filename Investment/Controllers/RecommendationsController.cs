using AutoMapper;
using ClosedXML.Excel;
using Invest.Authorization.Enums;
using Invest.Core.Dtos;
using Invest.Core.Entities;
using Invest.Core.Settings;
using Investment.Authorization.Attributes;
using Investment.Authorization.Enums;
using Investment.Core.Constants;
using Investment.Core.Dtos;
using Investment.Core.Entities;
using Investment.Core.Extensions;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Investment.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Investment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Module(Modules.Recommendations)]
    public class RecommendationsController : ControllerBase
    {
        private readonly RepositoryContext _context;
        protected readonly IRepositoryManager _repository;
        private readonly IMapper _mapper;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly EmailQueue _emailQueue;
        private readonly AppSecrets _appSecrets;

        public RecommendationsController(RepositoryContext context, IRepositoryManager repositoryManager, IMapper mapper, IHttpContextAccessor httpContextAccessors, EmailQueue emailQueue, AppSecrets appSecrets)
        {
            _context = context;
            _repository = repositoryManager;
            _mapper = mapper;
            _httpContextAccessor = httpContextAccessors;
            _emailQueue = emailQueue;
            _appSecrets = appSecrets;
        }

        [HttpGet]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> Get([FromQuery] PaginationDto pagination)
        {
            bool isAsc = pagination?.SortDirection?.ToLower() == "asc";
            bool? isDeleted = pagination?.IsDeleted;

            var statusList = pagination?.Status?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLower()).ToList();

            var usersQuery = _context.Users
                                     .Join(_context.UserRoles,
                                         u => u.Id,
                                         ur => ur.UserId,
                                         (u, ur) => new { u, ur })
                                     .Join(_context.Roles,
                                         x => x.ur.RoleId,
                                         r => r.Id,
                                         (x, r) => new { x.u, r })
                                     .Where(x => x.r.Name == UserRoles.User)
                                     .Select(x => x.u);

            var query = _context.Recommendations
                                .ApplySoftDeleteFilter(isDeleted)
                                .Join(usersQuery,
                                    r => r.UserEmail,
                                    u => u.Email,
                                    (r, u) => r)
                                .Include(r => r.Campaign)
                                .Include(r => r.RejectedByUser)
                                .AsQueryable();

            if (pagination?.InvestmentId != null)
                query = query.Where(x => x.CampaignId == pagination.InvestmentId);

            if (statusList != null && statusList.Count > 0)
                query = query.Where(x => !string.IsNullOrEmpty(x.Status) && statusList.Contains(x.Status.ToLower()));

            var orderedQuery = pagination?.SortField?.ToLower() switch
            {
                "id" => isAsc ? query.OrderBy(r => r.Id) : query.OrderByDescending(r => r.Id),
                "userfullname" => isAsc ? query.OrderBy(r => r.UserFullName) : query.OrderByDescending(r => r.UserFullName),
                "status" => isAsc ? query.OrderBy(r => r.Status) : query.OrderByDescending(r => r.Status),
                "campaignname" => isAsc ? query.OrderBy(r => r.Campaign!.Name) : query.OrderByDescending(r => r.Campaign!.Name),
                "amount" => isAsc ? query.OrderBy(r => r.Amount) : query.OrderByDescending(r => r.Amount),
                "datecreated" => isAsc ? query.OrderBy(r => r.DateCreated) : query.OrderByDescending(r => r.DateCreated),
                _ => query.OrderByDescending(r => r.DateCreated)
            };

            var finalQuery = orderedQuery.ThenBy(r => r.Id);

            int page = pagination?.CurrentPage ?? 1;
            int pageSize = pagination?.PerPage ?? 50;

            int totalCount = await query.CountAsync();

            var pagedData = await finalQuery
                                 .Skip((page - 1) * pageSize)
                                 .Take(pageSize)
                                 .Select(r => new
                                 {
                                     r.Id,
                                     r.UserEmail,
                                     r.UserFullName,
                                     r.Status,
                                     r.Amount,
                                     CampaignId = r.Campaign!.Id,
                                     CampaignName = r.Campaign.Name,
                                     r.RejectionMemo,
                                     RejectedBy = r.RejectedByUser != null ? r.RejectedByUser.FirstName : null,
                                     r.DateCreated,
                                     r.DeletedAt,
                                     DeletedBy = r.DeletedByUser != null
                                                ? $"{r.DeletedByUser.FirstName} {r.DeletedByUser.LastName}"
                                                : null
                                 })
                                 .ToListAsync();

            var recommendationStats = await query
                                        .GroupBy(r => 1)
                                        .Select(g => new
                                        {
                                            Pending = g.Where(r => r.Status!.ToLower().Trim() == "pending")
                                                            .Sum(r => (decimal?)r.Amount) ?? 0m,
                                            Approved = g.Where(r => r.Status!.ToLower().Trim() == "approved")
                                                            .Sum(r => (decimal?)r.Amount) ?? 0m,
                                        })
                                        .FirstOrDefaultAsync();

            if (pagedData.Any())
                return Ok(new
                {
                    items = pagedData,
                    totalCount,
                    recommendationStats!.Pending,
                    recommendationStats.Approved,
                    total = recommendationStats.Pending + recommendationStats.Approved
                });

            return Ok(new { Success = false, Message = "Data not found." });
        }

        [HttpGet("{id}")]
        [ModuleAuthorize(PermissionType.View)]
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
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> Create([FromBody] AddRecommendationDto addRecommendation)
        {
            CommonResponse response = new();
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
            recommendation.CampaignId = campaignId;
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
                await AddPersonalDeduction(user!, amountToDeduct, campaignName!, campaignId, addRecommendation.PendingGrants?.DAFProvider);
            }
            else
            {
                amountToDeduct = await DeductFromGroupAccounts(user!, groupAccountBalances, amountToDeduct, campaignName!, campaignId);

                if (amountToDeduct > 0)
                {
                    if (user!.AccountBalance < amountToDeduct)
                    {
                        decimal shortfall = Convert.ToDecimal(amountToDeduct) - Convert.ToDecimal(user.AccountBalance);
                        recommendation.Amount -= shortfall;
                        amountToDeduct = Convert.ToDecimal(user.AccountBalance);
                    }

                    await AddPersonalDeduction(user, amountToDeduct, campaignName!, campaignId, addRecommendation.PendingGrants?.DAFProvider);
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
                PictureFileName = user!.ConsentToShowAvatar ? user?.PictureFileName : null,
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

            var totalDonationAmount = await recommendationsQuery.SumAsync(r => r.Amount ?? 0);
            var totalInvestors = await recommendationsQuery.Select(r => r.UserEmail).Distinct().CountAsync();

            var campaignIdentifier = campaignProperty ?? campaignId?.ToString();

            string conditionalUserName = user?.IsAnonymousInvestment == true
                ? "Someone"
                : $"{userFirstName} {userLastName}";

            string conditionalDonorName = user?.IsAnonymousInvestment == true
                ? "An anonymous CataCap donor"
                : $"{userFirstName} {userLastName}";

            var commonVariables = new Dictionary<string, string>
            {
                { "campaignName", campaignName! },
                { "campaignDescription", campaignDescription! },
                { "campaignUrl", $"{_appSecrets.RequestOrigin}/investments/{campaignIdentifier}" },
                { "unsubscribeUrl", $"{_appSecrets.RequestOrigin}/settings" },
                { "investorDisplayName", conditionalUserName },
                { "donorName", conditionalDonorName }
            };

            foreach (var email in emailsForGroupAndUser)
            {
                var variables = new Dictionary<string, string>(commonVariables)
                {
                    { "firstName", email.FirstName }
                };

                _emailQueue.QueueEmail(async (sp) =>
                {
                    var emailService = sp.GetRequiredService<IEmailTemplateService>();

                    await emailService.SendTemplateEmailAsync(
                        EmailTemplateCategory.CampaignInvestmentNotification,
                        email.Email,
                        variables
                    );
                });
            }

            foreach (var email in uniqueRec)
            {
                var variables = new Dictionary<string, string>(commonVariables)
                {
                    { "userFullName", email.UserFullName! }
                };

                _emailQueue.QueueEmail(async (sp) =>
                {
                    var emailService = sp.GetRequiredService<IEmailTemplateService>();

                    await emailService.SendTemplateEmailAsync(
                        EmailTemplateCategory.FollowerInfluenceNotification,
                        email.UserEmail!,
                        variables
                    );
                });
            }

            if (!addRecommendation.IsRequestForInTransit && (user!.OptOutEmailNotifications == null || !(bool)user.OptOutEmailNotifications))
            {
                var variables = new Dictionary<string, string>(commonVariables)
                {
                    { "firstName", user.FirstName! },
                    { "investmentAmount", investmentAmount }
                };

                _emailQueue.QueueEmail(async (sp) =>
                {
                    var emailService = sp.GetRequiredService<IEmailTemplateService>();

                    await emailService.SendTemplateEmailAsync(
                        EmailTemplateCategory.DonationConfirmation,
                        user.Email,
                        variables
                    );
                });
            }

            if (!string.IsNullOrEmpty(campaignContactInfoEmailAddress))
            {
                string formattedOriginalInvestmentAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", originalInvestmentAmount);
                string formattedtotalDonationAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", totalDonationAmount);

                string investorName = user?.IsAnonymousInvestment == true
                    ? "a donor-investor"
                    : $"{userFirstName} {userLastName}";

                var variables = new Dictionary<string, string>(commonVariables)
                {
                    { "campaignFirstName", campaignContactInfoFullName?.Split(' ')[0] ?? "" },
                    { "investorName", investorName },
                    { "investmentAmount", formattedOriginalInvestmentAmount },
                    { "totalRaised", formattedtotalDonationAmount },
                    { "totalInvestors", totalInvestors.ToString() },
                    { "campaignPageUrl", $"{_appSecrets.RequestOrigin}/investments/{campaignIdentifier}" }
                };

                _emailQueue.QueueEmail(async (sp) =>
                {
                    var emailService = sp.GetRequiredService<IEmailTemplateService>();

                    await emailService.SendTemplateEmailAsync(
                        EmailTemplateCategory.CampaignOwnerFundingNotification,
                        campaignContactInfoEmailAddress,
                        variables
                    );
                });
            }

            response.Success = true;
            response.Message = "Recommendation created successfully.";

            if (user != null)
                response.Data = _mapper.Map<UserDetailsDto>(user);

            return Ok(response);
        }

        [HttpPost("feedback")]
        [ModuleAuthorize(PermissionType.Manage)]
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

        [HttpPut("{id}")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] RecommendationsDto data)
        {
            if (data == null)
                return BadRequest(new { Success = false, Message = "Data type is invalid" });

            var recommendation = await _context.Recommendations
                                                .Include(item => item.Campaign)
                                                .Include(item => item.RejectedByUser)
                                                .FirstOrDefaultAsync(item => item.Id == id);
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
                    PaymentType = $"Recommendation reverted, Id = {recommendation?.Id}",
                    InvestmentName = recommendation?.Campaign?.Name,
                    CampaignId = recommendation?.CampaignId,
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
                    data.Status,
                    RejectedBy = rejectingUser.FirstName!.Trim().ToLower(),
                    recommendation?.RejectionMemo
                }
            });
        }

        [HttpGet("export")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetExportRecommendations()
        {
            var usersQuery = _context.Users
                                     .Join(_context.UserRoles,
                                         u => u.Id,
                                         ur => ur.UserId,
                                         (u, ur) => new { u, ur })
                                     .Join(_context.Roles,
                                         x => x.ur.RoleId,
                                         r => r.Id,
                                         (x, r) => new { x.u, r })
                                     .Where(x => x.r.Name == UserRoles.User)
                                     .Select(x => x.u);

            var recommendations = await _context.Recommendations
                                                .Join(usersQuery,
                                                      r => r.UserEmail,
                                                      u => u.Email,
                                                      (r, u) => r)
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
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> ExportInvestmentRecommendations(int investmentId)
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
                worksheet.Cell(1, 5).Value = "InTransitGrant?";

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

                    worksheet.Cell(row, col++).Value = dto.PendingGrants != null
                                                            ? dto.PendingGrants.status!.ToLower().Trim() == "in transit"
                                                                ? "Yes"
                                                                : ""
                                                            : "";
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

        private async Task AddPersonalDeduction(User user, decimal amount, string investmentName, int? campaignId, string? grantType)
        {
            var identity = HttpContext?.User.Identity as ClaimsIdentity == null ? _httpContextAccessor.HttpContext?.User.Identity as ClaimsIdentity : HttpContext.User.Identity as ClaimsIdentity;
            var loginUserId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            var loginUser = await _repository.UserAuthentication.GetUserById(loginUserId!);

            bool isAdmin = identity?.Claims.Any(c => c.Type == ClaimTypes.Role && (c.Value == UserRoles.Admin || c.Value == UserRoles.SuperAdmin)) == true;

            if (!string.IsNullOrWhiteSpace(grantType))
            {
                grantType = grantType.ToLower() == "foundation grant"
                            ? "Foundation grant"
                            : "DAF grant";
            }

            string? paymentType = !string.IsNullOrWhiteSpace(grantType)
                                    ? grantType
                                        : null;

            if (string.IsNullOrWhiteSpace(paymentType))
                paymentType = "Recommendation created using account balance";
            else if (isAdmin)
                paymentType = $"{paymentType}, {loginUser?.UserName?.Trim().ToLower()}";

            var log = new AccountBalanceChangeLog
            {
                UserId = user.Id,
                PaymentType = paymentType,
                OldValue = user.AccountBalance,
                UserName = user.UserName,
                NewValue = user.AccountBalance - amount,
                InvestmentName = investmentName,
                CampaignId = campaignId
            };

            user.AccountBalance -= amount;
            await _context.AccountBalanceChangeLogs.AddAsync(log);
        }

        private async Task<decimal> DeductFromGroupAccounts(User user, List<GroupAccountBalance> balances, decimal amount, string investmentName, int? campaignId)
        {
            var identity = HttpContext?.User.Identity as ClaimsIdentity == null ? _httpContextAccessor.HttpContext?.User.Identity as ClaimsIdentity : HttpContext.User.Identity as ClaimsIdentity;
            var loginUserId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            var loginUser = await _repository.UserAuthentication.GetUserById(loginUserId!);

            bool isAdmin = identity?.Claims.Any(c => c.Type == ClaimTypes.Role && (c.Value == UserRoles.Admin || c.Value == UserRoles.SuperAdmin)) == true;

            foreach (var gab in balances)
            {
                if (amount <= 0) break;
                if (gab.Balance <= 0) continue;

                decimal deduction = Math.Min(gab.Balance, amount);

                var log = new AccountBalanceChangeLog
                {
                    UserId = user.Id,
                    PaymentType = isAdmin ? $"Recommendation created using group balance, {loginUser.UserName!.Trim().ToLower()}" : "Recommendation created using group balance",
                    OldValue = gab.Balance,
                    UserName = user.UserName,
                    NewValue = gab.Balance - deduction,
                    InvestmentName = investmentName,
                    CampaignId = campaignId,
                    GroupId = gab.Group.Id
                };

                gab.Balance -= deduction;
                amount -= deduction;

                await _context.AccountBalanceChangeLogs.AddAsync(log);
            }

            return amount;
        }

        private async Task<List<UserEmailInfo>> GetUsersForEmailsAsync(
            IEnumerable<UserEmailInfo> users,
            bool? emailFromGroupsOn,
            bool? emailFromUsersOn)
        {
            var emails = users.Select(u => u.Email).ToList();

            return await _context.Users
                                .Where(u => emails.Contains(u.Email) &&
                                            (u.OptOutEmailNotifications == null || !(u.OptOutEmailNotifications ?? false)) &&
                                            (
                                                (emailFromGroupsOn == true && (u.EmailFromGroupsOn ?? false)) ||
                                                (emailFromUsersOn == true && (u.EmailFromUsersOn ?? false))
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
