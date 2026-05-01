using AutoMapper;
using ClosedXML.Excel;
using Invest.Authorization.Enums;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Investment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Module(Modules.PendingGrants)]
    public class PendingGrantsAdmin : ControllerBase
    {
        private readonly RepositoryContext _context;
        private readonly IMapper _mapper;
        protected readonly IRepositoryManager _repository;
        private readonly AppSecrets _appSecrets;
        private readonly IHttpContextAccessor _httpContextAccessors;
        private readonly EmailQueue _emailQueue;

        public PendingGrantsAdmin(RepositoryContext context, IMapper mapper, IRepositoryManager repository, AppSecrets appSecrets, IHttpContextAccessor httpContextAccessors, EmailQueue emailQueue)
        {
            _context = context;
            _mapper = mapper;
            _repository = repository;
            _appSecrets = appSecrets;
            _httpContextAccessors = httpContextAccessors;
            _emailQueue = emailQueue;
        }

        [HttpGet]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> Get([FromQuery] PaginationDto pagination, string? dafProvider)
        {
            bool isAsc = pagination?.SortDirection?.ToLower() == "asc";
            bool? isDeleted = pagination?.IsDeleted;

            var statusList = pagination?.Status?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(s => s.Trim().ToLower())
                                                .ToList();
            var now = DateTime.UtcNow;

            var dafProviders = await _context.DAFProviders
                                             .Select(x => x.ProviderName!.ToLower().Trim())
                                             .ToListAsync();

            var providerList = string.IsNullOrEmpty(dafProvider)
                                ? new List<string>()
                                : dafProvider.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                             .Select(x => x.Trim().ToLower())
                                             .ToList();

            var hasOther = providerList.Contains("other");

            var selectedProviders = providerList
                                    .Where(p => p != "other")
                                    .ToList();

            var query = _context.PendingGrants
                                .ApplySoftDeleteFilter(isDeleted)
                                .Where(i => (statusList == null || statusList.Count == 0 ||
                                                (statusList.Contains("pending")
                                                    ? string.IsNullOrEmpty(i.status) && statusList.Contains("pending") ||
                                                        !string.IsNullOrEmpty(i.status) && statusList.Contains(i.status.ToLower())
                                                    : !string.IsNullOrEmpty(i.status) && statusList.Contains(i.status.ToLower())
                                                )
                                            )
                                            && (string.IsNullOrEmpty(pagination!.SearchValue)
                                                || (i.User.FirstName + " " + i.User.LastName).ToLower().Contains(pagination.SearchValue.ToLower())
                                                || i.User.Email.ToLower().Contains(pagination.SearchValue.ToLower())
                                            )
                                            && (
                                                providerList.Count == 0
                                                || selectedProviders.Contains(i.DAFProvider.ToLower().Trim())

                                                || (
                                                    hasOther
                                                    && !dafProviders.Contains(i.DAFProvider.ToLower().Trim())
                                                    && i.DAFProvider.ToLower().Trim() != "foundation grant"
                                                )
                                            )
                                )
                                .Select(i => new
                                {
                                    i.Id,
                                    i.User.FirstName,
                                    i.User.LastName,
                                    i.User.Email,
                                    i.Amount,
                                    i.AmountAfterFees,
                                    i.DAFName,
                                    i.DAFProvider,
                                    InvestmentName = i.Campaign!.Name,
                                    i.Reference,
                                    Status = string.IsNullOrEmpty(i.status) ? "Pending" : i.status,
                                    i.CreatedDate,
                                    i.DeletedAt,
                                    i.DeletedByUser
                                });

            switch (pagination?.SortField?.ToLower())
            {
                case "fullname":
                    query = isAsc
                                ? query.OrderBy(i => i.Status.ToLower() == "rejected" ? 1 : 0)
                                        .ThenBy(i => i.FirstName)
                                        .ThenBy(i => i.LastName)
                                : query.OrderBy(i => i.Status.ToLower() == "rejected" ? 1 : 0)
                                        .ThenByDescending(i => i.FirstName)
                                        .ThenByDescending(i => i.LastName);
                    break;

                case "createddate":
                    query = isAsc
                                ? query.OrderBy(i => i.Status.ToLower() == "rejected" ? 1 : 0)
                                        .ThenBy(i => i.CreatedDate ?? DateTime.MaxValue)
                                : query.OrderBy(i => i.Status.ToLower() == "rejected" ? 1 : 0)
                                        .ThenByDescending(i => i.CreatedDate ?? DateTime.MinValue);
                    break;

                case "status":
                    query = isAsc
                                ? query.OrderBy(i => i.Status)
                                : query.OrderByDescending(i => i.Status);
                    break;

                case "dayscount":
                    query = isAsc
                                ? query.OrderBy(i => i.Status.ToLower() == "pending" ? 0
                                                        : string.IsNullOrEmpty(i.Status)
                                                        ? 2 : 1)
                                        .ThenBy(i => i.CreatedDate ?? DateTime.MaxValue)
                                : query.OrderBy(i => i.Status.ToLower() == "pending" ? 0
                                                        : string.IsNullOrEmpty(i.Status)
                                                        ? 2 : 1)
                                        .ThenByDescending(i => i.CreatedDate ?? DateTime.MinValue);
                    break;

                default:
                    query = query.OrderBy(i => i.Status.ToLower() == "rejected")
                                    .ThenByDescending(i => i.CreatedDate);
                    break;
            }

            int page = pagination?.CurrentPage ?? 1;
            int pageSize = pagination?.PerPage ?? 50;
            int totalCount = await query.CountAsync();

            var results = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var pagedData = results.Select(i => new
            {
                i.Id,
                i.FirstName,
                i.LastName,
                FullName = i.FirstName + " " + i.LastName,
                i.Email,
                i.Amount,
                i.AmountAfterFees,
                i.DAFName,
                i.DAFProvider,
                i.InvestmentName,
                i.Reference,
                Status = string.IsNullOrEmpty(i.Status) ? "Pending" : i.Status,
                i.CreatedDate,
                DaysCount = !string.IsNullOrEmpty(i.Status) && i.Status.ToLower() == "pending" && i.CreatedDate != null
                                ? GetReadableDuration(i.CreatedDate.Value, now)
                                : null,
                i.DeletedAt,
                DeletedBy = i.DeletedByUser != null
                            ? $"{i.DeletedByUser.FirstName} {i.DeletedByUser.LastName}"
                            : null
            }).ToList();

            if (pagedData.Any())
                return Ok(new { items = pagedData, totalCount });

            return Ok(new { Success = false, Message = "Data not found." });
        }

        [HttpGet("daf-providers")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetDAFProviders()
        {
            var dafProviders = await _context.DAFProviders
                                             .Select(x => new
                                             {
                                                 x.Id,
                                                 Value = x.ProviderName,
                                                 Link = x.ProviderURL
                                             })
                                             .ToListAsync();

            if (dafProviders.Any())
                return Ok(dafProviders);

            return Ok(new { Success = false, Message = "Data not found." });
        }

        [HttpPut("{id}")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdatePendingGrantsDto pendingGrantsData)
        {
            var pendingGrant = await _context.PendingGrants
                                             .Include(p => p.Campaign)
                                             .Include(p => p.User)
                                             .FirstOrDefaultAsync(i => i.Id == id);
            if (pendingGrant == null)
                return BadRequest(new { Success = false, Message = "Wrong pending grand id." });

            pendingGrant.ModifiedDate = DateTime.Now;

            string currentStatus = pendingGrant.status ?? "Pending";
            decimal pendingGrandAmount = Convert.ToDecimal(pendingGrant.Amount);

            var identity = HttpContext.User.Identity as ClaimsIdentity;
            var loginUserId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            var loginUser = await _repository.UserAuthentication.GetUserById(loginUserId!);

            if (pendingGrantsData.Status == "In Transit" && currentStatus == "Pending")
            {
                var user = await _context.Users.FirstOrDefaultAsync(i => i.Email == pendingGrant.User.Email);
                if (user == null)
                    return BadRequest(new { Success = false, Message = "User not found." });

                bool isFreeUser = user.IsFreeUser.GetValueOrDefault();
                decimal totalCataCapFee = pendingGrandAmount * 0.05m; //CataCap Fee
                decimal amount = pendingGrant.AmountAfterFees > 0 ? pendingGrant.AmountAfterFees ?? 0m : (pendingGrandAmount - totalCataCapFee);

                var groupAccountBalance = await _context.GroupAccountBalance
                                                .Include(gab => gab.Group)
                                                .Where(gab => gab.User.Id == user.Id)
                                                .OrderBy(gab => gab.Id)
                                                .ToListAsync();

                decimal totalGroupBalance = groupAccountBalance.Sum(gab => gab.Balance);
                decimal fromWallet = Convert.ToDecimal(pendingGrant.InvestedSum) - (pendingGrandAmount + totalGroupBalance);

                if (user.AccountBalance < fromWallet)
                    return Ok(new { Success = false, Message = "User do not have sufficient wallet balance." });

                pendingGrant.status = "In Transit";

                var grantType = pendingGrant.DAFProvider.ToLower().Trim() == "foundation grant" ? "Foundation grant" : "DAF grant";

                string? zipCode = null;
                if (!string.IsNullOrWhiteSpace(pendingGrant.Address))
                {
                    var address = JsonSerializer.Deserialize<AddressDto>(pendingGrant.Address);
                    zipCode = address?.ZipCode;
                }

                decimal fees = pendingGrant.GrantAmount - pendingGrant.AmountAfterFees ?? 0m;

                var balanceResult = await UpdateAccountBalance(pendingGrant.User.Email, amount, pendingGrandAmount, fees, grantType, pendingGrant.Id, pendingGrant.TotalInvestedAmount ?? 0m, pendingGrant.Reference, pendingGrant.Campaign?.Name, zipCode);
                if (!balanceResult.Success)
                    return Ok(new { Success = false, balanceResult.Message });

                decimal totalAvailable = Convert.ToDecimal(user.AccountBalance + amount + totalGroupBalance);
                decimal finalInvestmentAmount = Math.Min(totalAvailable, Convert.ToDecimal(pendingGrant.InvestedSum));

                if (pendingGrant.Campaign != null)
                {
                    var recommendation = new AddRecommendationDto
                    {
                        Amount = finalInvestmentAmount,
                        IsGroupAccountBalance = true,
                        IsRequestForInTransit = true,
                        Campaign = pendingGrant.Campaign,
                        User = pendingGrant.User,
                        UserEmail = pendingGrant.User.Email,
                        UserFullName = pendingGrant.User.FirstName + " " + pendingGrant.User.LastName,
                        PendingGrants = pendingGrant
                    };

                    await new RecommendationsController(
                        _context,
                        _repository,
                        _mapper,
                        _httpContextAccessors,
                        _emailQueue,
                        _appSecrets)
                        .Create(recommendation);
                }
                user.IsActive = true;
                user.IsFreeUser = false;
                await _context.SaveChangesAsync();
            }
            else if (pendingGrantsData.Status == "Rejected")
            {
                if (currentStatus == "In Transit")
                {
                    var user = await _context.Users.FirstOrDefaultAsync(i => i.Email == pendingGrant.User.Email);

                    if (user == null)
                        return BadRequest(new { Success = false, Message = "User not found" });

                    pendingGrant.status = "Rejected";
                    pendingGrant.RejectedBy = loginUserId!;
                    pendingGrant.RejectionMemo = pendingGrantsData.RejectionMemo.Trim();
                    pendingGrant.RejectionDate = DateTime.Now;

                    if (pendingGrant?.Campaign?.Id == null)
                    {
                        var existingLog = await _context.AccountBalanceChangeLogs
                                                .Include(x => x.PendingGrants)
                                                .Where(x => x.UserId == pendingGrant!.UserId && x.PendingGrantsId == pendingGrant.Id)
                                                .OrderByDescending(x => x.Id)
                                                .FirstOrDefaultAsync();

                        if (existingLog != null)
                        {
                            await AccountBalanceChangeLog(user, -existingLog!.PendingGrants!.AmountAfterFees!.Value, $"Pending grant reverted, id = {pendingGrant?.Id}", pendingGrant!.Id, existingLog.Reference);
                            await _context.SaveChangesAsync();
                        }
                    }
                    else
                    {
                        var recommendation = await _context.Recommendations.FirstOrDefaultAsync(x =>
                                                        x.Campaign != null &&
                                                        x.UserEmail == user.Email &&
                                                        x.Campaign.Id == pendingGrant.Campaign.Id &&
                                                        x.PendingGrantsId == pendingGrant.Id);

                        var existingLog = await _context.AccountBalanceChangeLogs
                                                .Where(x => x.UserId == pendingGrant.UserId)
                                                .OrderByDescending(x => x.Id)
                                                .FirstOrDefaultAsync();

                        if (existingLog != null)
                        {
                            decimal amount = pendingGrant.AmountAfterFees ?? pendingGrandAmount - (pendingGrandAmount * 0.05m);

                            if (recommendation?.Status != "rejected")
                                await AccountBalanceChangeLog(user, recommendation?.Amount ?? 0, $"Recommendation reverted due to pending grant rollback, id = {recommendation?.Id}", pendingGrant.Id, existingLog.Reference, recommendation?.Campaign?.Name, recommendation?.Campaign?.Id);

                            await AccountBalanceChangeLog(user, -amount, $"Pending grant reverted, id = {pendingGrant.Id}", pendingGrant.Id, existingLog.Reference);
                        }

                        if (recommendation != null)
                            recommendation.Status = "rejected";

                        await _context.SaveChangesAsync();
                    }
                }
                else if (currentStatus == "Pending")
                {
                    pendingGrant.status = "Rejected";
                    pendingGrant.RejectedBy = loginUserId!;
                    pendingGrant.RejectionMemo = pendingGrantsData.RejectionMemo.Trim();
                    pendingGrant.RejectionDate = DateTime.Now;

                    await _context.SaveChangesAsync();
                }
            }
            else if (pendingGrantsData.Status == "Received" && currentStatus == "In Transit")
            {
                pendingGrant.status = "Received";
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                Success = true,
                Message = $"Grant set {pendingGrantsData.Status}"
            });
        }

        [HttpPost("create")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> AddPendingGrants([FromBody] PendingGrantsDto pendingGrants)
        {
            if (pendingGrants == null)
                return BadRequest(new { Success = false, Message = "Data type is invalid" });
            if (pendingGrants.Amount <= 0)
                return BadRequest(new { Success = false, Message = "Amount must be greater than zero." });

            var allEmailTasks = new List<Task>();

            var requestOrigin = HttpContext?.Request.Headers["Origin"].ToString();

            var email = pendingGrants?.Email ?? string.Empty;
            bool isAnonymous = pendingGrants!.IsAnonymous;
            decimal amount = pendingGrants.CoverFees ? pendingGrants.InvestmentAmountWithFees : pendingGrants.Amount;
            decimal amountAfterFees = pendingGrants.CoverFees ? pendingGrants.Amount : amount - (amount * 0.05m);

            if (isAnonymous)
            {
                var existingEmail = _context.Users.Where(u => u.Email.ToLower() == email.ToLower().Trim()).Any();
                if (existingEmail)
                    return Ok(new { Success = false, Message = $"Email '{email}' is already taken." });
            }

            var user = isAnonymous ? await RegisterAnonymousUser(pendingGrants!) : await GetUserFromContext(email);

            if (pendingGrants!.Reference?.ToLower().Trim() == "catacap.org" || pendingGrants!.Reference?.ToLower().Trim() == "champions deals")
                user.IsActive = true;

            decimal investedAmount = pendingGrants?.InvestedSum > 0 ? Convert.ToDecimal(pendingGrants.InvestedSum) : (pendingGrants!.CoverFees ? pendingGrants.InvestmentAmountWithFees : pendingGrants!.Amount);
            int? investmentId = !string.IsNullOrEmpty(pendingGrants.InvestmentId) ? Convert.ToInt32(pendingGrants.InvestmentId) : null;
            var campaign = await _context.Campaigns.FirstOrDefaultAsync(i => i.Id == investmentId);
            var investmentName = campaign?.Name;

            var addressObj = new
            {
                pendingGrants.Address?.Street,
                pendingGrants.Address?.City,
                pendingGrants.Address?.State,
                pendingGrants.Address?.Country,
                pendingGrants.Address?.ZipCode
            };
            var addressJson = JsonSerializer.Serialize(addressObj);

            if (string.IsNullOrWhiteSpace(user.ZipCode))
                user.ZipCode = addressObj.ZipCode;

            var pendingGrant = new PendingGrants
            {
                UserId = user.Id,
                Amount = amount.ToString(),
                GrantAmount = amount,
                AmountAfterFees = amountAfterFees,
                DAFProvider = pendingGrants.DAFProvider,
                DAFName = !string.IsNullOrWhiteSpace(pendingGrants.DAFName) ? pendingGrants.DAFName : null,
                Campaign = campaign,
                InvestedSum = investedAmount.ToString(),
                TotalInvestedAmount = investedAmount,
                status = "Pending",
                Reference = !string.IsNullOrWhiteSpace(pendingGrants.Reference) ? pendingGrants.Reference : null,
                CreatedDate = DateTime.Now,
                ModifiedDate = DateTime.Now,
                Address = addressJson
            };
            await _context.PendingGrants.AddAsync(pendingGrant);
            await _context.SaveChangesAsync();

            string? dafProviderURL = null;

            if (!string.IsNullOrWhiteSpace(pendingGrant.DAFProvider))
            {
                string? dafProvider = pendingGrant.DAFProvider.Trim().ToLowerInvariant();

                dafProviderURL = await _context.DAFProviders
                                                .Where(x => x.ProviderName != null
                                                            && x.IsActive
                                                            && x.ProviderName.ToLower().Trim() == dafProvider)
                                                .Select(x => x.ProviderURL)
                                                .FirstOrDefaultAsync();
            }

            string formattedAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", amount);

            var commonVariables = new Dictionary<string, string>
            {
                { "firstName", user.FirstName! },
                { "siteUrl", _appSecrets.RequestOrigin }
            };

            if (isAnonymous)
            {
                var welcomeVariables = new Dictionary<string, string>(commonVariables)
                {
                    { "userName", user.UserName },
                    { "resetPasswordUrl", $"{_appSecrets.RequestOrigin}/forgotpassword" }
                };

                _emailQueue.QueueEmail(async (sp) =>
                {
                    var emailService = sp.GetRequiredService<IEmailTemplateService>();

                    await emailService.SendTemplateEmailAsync(
                        EmailTemplateCategory.WelcomeAnonymousUser,
                        email,
                        welcomeVariables
                    );
                });
            }

            if (user?.OptOutEmailNotifications == null || !(bool)user.OptOutEmailNotifications)
            {
                if (pendingGrants.DAFProvider == "foundation grant")
                {
                    string investmentScenarios = !string.IsNullOrEmpty(investmentName)
                                                    ? $"to invest in {investmentName} through investment"
                                                    : "through investment";

                    var foundationVariables = new Dictionary<string, string>(commonVariables)
                    {
                        { "formattedAmount", formattedAmount },
                        { "investmentScenarios", investmentScenarios }
                    };

                    _emailQueue.QueueEmail(async (sp) =>
                    {
                        var emailService = sp.GetRequiredService<IEmailTemplateService>();

                        await emailService.SendTemplateEmailAsync(
                            EmailTemplateCategory.FoundationDonationInstructions,
                            email,
                            foundationVariables
                        );
                    });
                }
                else
                {
                    string investmentScenario = !string.IsNullOrEmpty(investmentName)
                                                ? $"to support <b>{investmentName}</b> through CataCap"
                                                : "through CataCap";

                    var dafProviderLink = !string.IsNullOrEmpty(dafProviderURL)
                                            ? $@"<a href='{dafProviderURL}' target='_blank'>{pendingGrants.DAFProvider}</a>"
                                            : pendingGrants.DAFProvider;

                    string donationRecipient = pendingGrants.DAFProvider == "DAFgiving360: Charles Schwab" ? "CataCap" : "Impactree Foundation";

                    var dafVariables = new Dictionary<string, string>(commonVariables)
                    {
                        { "formattedAmount", formattedAmount },
                        { "investmentScenario", investmentScenario },
                        { "dafProviderName", pendingGrants.DAFProvider },
                        { "dafProviderLink", dafProviderLink },
                        { "donationRecipient", donationRecipient }
                    };

                    _emailQueue.QueueEmail(async (sp) =>
                    {
                        var emailService = sp.GetRequiredService<IEmailTemplateService>();

                        EmailTemplateCategory templateCategory =
                            pendingGrants.DAFProvider == "ImpactAssets"
                                ? EmailTemplateCategory.DAFDonationInstructionsImpactAssets
                                : EmailTemplateCategory.DAFDonationInstructions;

                        await emailService.SendTemplateEmailAsync(
                            templateCategory,
                            email,
                            dafVariables
                        );
                    });
                }
            }

            string paymentMethod = pendingGrants.DAFProvider == "foundation grant"
                                        ? "Foundation Grant"
                                        : !string.IsNullOrEmpty(pendingGrants.DAFName)
                                            ? $"{pendingGrants.DAFProvider} - {pendingGrants.DAFName}"
                                            : pendingGrants.DAFProvider ?? string.Empty;

            var variables = new Dictionary<string, string>
            {
                { "formattedAmount", formattedAmount },
                { "firstName", user!.FirstName! },
                { "lastName", user.LastName! },
                { "paymentMethod", paymentMethod }
            };

            _emailQueue.QueueEmail(async (sp) =>
            {
                var emailService = sp.GetRequiredService<IEmailTemplateService>();

                await emailService.SendTemplateEmailAsync(
                    EmailTemplateCategory.PendingGrantNotification,
                    _appSecrets.AdminEmail,
                    variables
                );
            });

            return Ok(new { Success = true, Message = "Grant created successful." });
        }

        [HttpGet("export")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> ExportPendingGrants()
        {
            var data = await _context.PendingGrants
                                        .Include(i => i.Campaign)
                                        .Include(i => i.User)
                                        .OrderByDescending(i => i.Id)
                                        .ToListAsync();

            string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            string fileName = "PendingGrants.xlsx";

            var now = DateTime.UtcNow;

            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet worksheet = workbook.Worksheets.Add("PendingGrants");

                var headers = new[]
                {
                    "Full Name", "Email", "Original Amount", "Amount After Fees", "DAF Provider", "DAF Name",
                    "Investment Name", "Grant Source", "Status", "Address", "Date Created", "Day Count"
                };

                for (int col = 0; col < headers.Length; col++)
                {
                    worksheet.Cell(1, col + 1).Value = headers[col];
                }

                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;

                worksheet.Columns().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                for (int index = 0; index < data.Count; index++)
                {
                    var dto = data[index];
                    int row = index + 2;
                    int col = 1;

                    worksheet.Cell(row, col++).Value = dto.User.FirstName + " " + dto.User.LastName;
                    worksheet.Cell(row, col++).Value = dto.User.Email;

                    var amountCell = worksheet.Cell(row, col++);
                    amountCell.Value = $"${Convert.ToDecimal(dto.Amount):N2}";
                    amountCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    var amountAfterFeesCell = worksheet.Cell(row, col++);
                    amountAfterFeesCell.Value = $"${Convert.ToDecimal(dto.AmountAfterFees):N2}";
                    amountAfterFeesCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    worksheet.Cell(row, col++).Value = dto.DAFProvider;
                    worksheet.Cell(row, col++).Value = dto.DAFName;
                    worksheet.Cell(row, col++).Value = dto.Campaign?.Name;
                    worksheet.Cell(row, col++).Value = dto.Reference;
                    worksheet.Cell(row, col++).Value = string.IsNullOrEmpty(dto.status) ? "Pending" : dto.status;
                    worksheet.Cell(row, col++).Value = dto.Address;
                    worksheet.Cell(row, col++).Value = dto.CreatedDate?.ToString("MM-dd-yyyy HH:mm");

                    var createdDateCell = worksheet.Cell(row, col++);
                    if (string.IsNullOrEmpty(dto.status) || dto.status.ToLower() == "pending")
                    {
                        createdDateCell.Value = dto.CreatedDate != null
                                                    ? GetReadableDuration(dto.CreatedDate.Value, now)
                                                    : "";
                    }
                    else
                    {
                        createdDateCell.Value = "";
                    }
                }
                worksheet.Columns().AdjustToContents();

                foreach (var column in worksheet.Columns())
                {
                    column.Width += 10;
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(), contentType, fileName);
                }
            }
        }

        private async Task<(bool Success, string Message)> UpdateAccountBalance(string email, decimal accountBalance, decimal originalAmount, decimal totalCataCapFee, string grantType, int pendingGrantsId, decimal totalInvestmentAmount, string? reference = null, string? investmentName = null, string? zipCode = null)
        {
            var user = await _context.Users.FirstOrDefaultAsync(i => i.Email == email);

            if (user?.AccountBalance + accountBalance < 0)
                return (false, "Insufficient balance in user account.");

            var identity = HttpContext?.User.Identity as ClaimsIdentity == null ? _httpContextAccessors.HttpContext?.User.Identity as ClaimsIdentity : HttpContext.User.Identity as ClaimsIdentity;
            var loginUserId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            var loginUser = await _repository.UserAuthentication.GetUserById(loginUserId!);

            var accountBalanceChangeLog = new AccountBalanceChangeLog
            {
                UserId = user!.Id,
                PaymentType = string.IsNullOrWhiteSpace(grantType)
                                ? $"Manually, {loginUser.UserName.Trim().ToLower()}"
                                : $"{grantType}, {loginUser.UserName.Trim().ToLower()}",
                OldValue = user.AccountBalance,
                UserName = user.UserName,
                NewValue = user.AccountBalance + accountBalance,
                PendingGrantsId = pendingGrantsId,
                Fees = totalCataCapFee,
                GrossAmount = originalAmount,
                NetAmount = accountBalance,
                Reference = !string.IsNullOrWhiteSpace(reference) ? reference.Trim() : null,
                ZipCode = !string.IsNullOrWhiteSpace(zipCode) ? zipCode.Trim() : null,
            };
            await _context.AccountBalanceChangeLogs.AddAsync(accountBalanceChangeLog);

            if (user.IsFreeUser == true)
                user.IsFreeUser = false;

            user.AccountBalance = user.AccountBalance == null ? accountBalance : user.AccountBalance + accountBalance;

            await _context.SaveChangesAsync();

            if (user.OptOutEmailNotifications == null || !user.OptOutEmailNotifications.Value)
            {
                var request = _httpContextAccessors.HttpContext?.Request.Headers["Origin"].ToString();

                decimal newValue = accountBalanceChangeLog.NewValue ?? 0m;
                decimal userBalance = user.AccountBalance ?? 0m;
                decimal amountAfterInvestment = newValue - Math.Min(userBalance, totalInvestmentAmount);
                decimal investmentAmount;

                if (newValue > userBalance!)
                    investmentAmount = newValue;
                else if (newValue < totalInvestmentAmount)
                    investmentAmount = newValue;
                else
                    investmentAmount = originalAmount;

                if (accountBalance > 0 && originalAmount > 0)
                {
                    string formattedOriginalAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", Convert.ToDecimal(originalAmount));
                    string formattedOriginalAmountAfter = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", Convert.ToDecimal(accountBalance));
                    string formattedInvestmentAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", Convert.ToDecimal(investmentAmount));
                    string formattedAmountAfterInvestment = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", Convert.ToDecimal(amountAfterInvestment));

                    string investmentScenario = !string.IsNullOrEmpty(investmentName)
                                                ? $"Based on your investment of <b>{formattedInvestmentAmount}</b> in <b>{investmentName}</b>, your remaining balance is <b>{formattedAmountAfterInvestment}</b>"
                                                : "";

                    var variables = new Dictionary<string, string>
                    {
                        { "firstName", user.FirstName! },
                        { "originalAmount", formattedOriginalAmount },
                        { "originalAmountAfter", formattedOriginalAmountAfter },
                        { "investmentScenario", investmentScenario },
                        { "browseOpportunitiesUrl", $"{_appSecrets.RequestOrigin}/investments" },
                        { "unsubscribeUrl", $"{_appSecrets.RequestOrigin}/settings" }
                    };

                    _emailQueue.QueueEmail(async (sp) =>
                    {
                        var emailService = sp.GetRequiredService<IEmailTemplateService>();

                        await emailService.SendTemplateEmailAsync(
                            EmailTemplateCategory.GrantReceived,
                            user.Email,
                            variables
                        );
                    });
                }
            }

            return (true, "Account balance has been updated successfully!");
        }

        private static string GetReadableDuration(DateTime from, DateTime to)
        {
            int years = to.Year - from.Year;
            int months = to.Month - from.Month;
            int days = to.Day - from.Day;

            if (days < 0)
            {
                months--;
                days += DateTime.DaysInMonth(from.Year, from.Month);
            }

            if (months < 0)
            {
                years--;
                months += 12;
            }

            List<string> parts = new List<string>();
            if (years > 0) parts.Add($"{years} year{(years > 1 ? "s" : "")}");
            if (months > 0) parts.Add($"{months} month{(months > 1 ? "s" : "")}");
            if (days > 0) parts.Add($"{days} day{(days > 1 ? "s" : "")}");

            return parts.Count > 0 ? string.Join(", ", parts) : "0 days";
        }

        private async Task<User> RegisterAnonymousUser(PendingGrantsDto dto)
        {

            var userName = $"{dto.FirstName}{dto.LastName}".Replace(" ", "").Trim().ToLower();
            Random random = new Random();
            while (_context.Users.Any(x => x.UserName == userName))
            {
                userName = $"{dto.FirstName}{dto.LastName}{random.Next(0, 100)}".ToLower();
            }

            UserRegistrationDto registrationDto = new UserRegistrationDto()
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                UserName = userName,
                Password = _appSecrets.DefaultPassword,
                Email = dto.Email
            };

            await _repository.UserAuthentication.RegisterUserAsync(registrationDto, UserRoles.User);

            var user = await _repository.UserAuthentication.GetUserByUserName(userName);
            user.IsFreeUser = true;
            await _repository.UserAuthentication.UpdateUser(user);
            await _repository.SaveAsync();

            return user;
        }

        private async Task<User> GetUserFromContext(string email)
        {
            var identity = _httpContextAccessors.HttpContext?.User.Identity as ClaimsIdentity;
            var userId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (!string.IsNullOrEmpty(userId))
                return await _repository.UserAuthentication.GetUserById(userId);
            else
                return await _repository.UserAuthentication.GetUserByEmail(email);
        }

        private async Task AccountBalanceChangeLog(User user, decimal amount, string type, int pendingGrandId, string? reference = null, string? investmentName = null, int? campaignId = null)
        {
            var log = new AccountBalanceChangeLog
            {
                UserId = user.Id,
                PaymentType = type,
                OldValue = user.AccountBalance,
                UserName = user.UserName,
                NewValue = user.AccountBalance + amount,
                PendingGrantsId = pendingGrandId,
                Reference = !string.IsNullOrWhiteSpace(reference) ? reference : null,
                InvestmentName = investmentName,
                CampaignId = campaignId
            };

            await _context.AccountBalanceChangeLogs.AddAsync(log);
            user.AccountBalance = log.NewValue;
        }
    }
}
