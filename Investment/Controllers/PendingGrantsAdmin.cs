using AutoMapper;
using Azure.Storage.Blobs;
using ClosedXML.Excel;
using Invest.Core.Entities;
using Investment.Core.Dtos;
using Investment.Core.Entities;
using Investment.Extensions;
using Investment.Repo.Context;
using Investment.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Investment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PendingGrantsAdmin : ControllerBase
    {
        private readonly RepositoryContext _context;
        private readonly IMapper _mapper;
        private readonly IMailService _mailService;
        private readonly BlobContainerClient _blobContainerClient;
        protected readonly IRepositoryManager _repository;
        private readonly IConfiguration _configuration;
        private readonly KeyVaultConfigService _keyVaultConfigService;
        private readonly IHttpContextAccessor _httpContextAccessors;
        private readonly HttpClient _httpClient;
        private readonly IWebHostEnvironment _environment;
        private readonly string defaultPassword = "SEcurE!Pa$$w0rd_#2025";

        public PendingGrantsAdmin(RepositoryContext context, IMapper mapper, IMailService mailService, BlobContainerClient blobContainerClient, IRepositoryManager repository, IConfiguration configuration, KeyVaultConfigService keyVaultConfigService, IHttpContextAccessor httpContextAccessors, HttpClient httpClient, IWebHostEnvironment environment)
        {
            _context = context;
            _mapper = mapper;
            _mailService = mailService;
            _blobContainerClient = blobContainerClient;
            _repository = repository;
            _configuration = configuration;
            _keyVaultConfigService = keyVaultConfigService;
            _httpContextAccessors = httpContextAccessors;
            _httpClient = httpClient;
            _environment = environment;
        }

        [HttpPost("get-pending-grants")]
        public async Task<IActionResult> GetAll([FromBody] PaginationDto pagination)
        {
            try
            {
                bool isAsc = pagination?.SortDirection?.ToLower() == "asc";
                var statusList = pagination?.Status?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(s => s.Trim().ToLower())
                                                    .ToList();
                var now = DateTime.UtcNow;

                var query = _context.PendingGrants
                    .Where(i => statusList == null || statusList.Count == 0 ||
                                (statusList.Contains("pending")
                                    ? (string.IsNullOrEmpty(i.status) && statusList.Contains("pending")) ||
                                      (!string.IsNullOrEmpty(i.status) && statusList.Contains(i.status.ToLower()))
                                    : (!string.IsNullOrEmpty(i.status) && statusList.Contains(i.status.ToLower()))
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
                        Reference = i.Reference,
                        Status = string.IsNullOrEmpty(i.status) ? "Pending" : i.status,
                        i.RejectionMemo,
                        RejectedBy = i.RejectedByUser!.FirstName,
                        i.CreatedDate
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
                                                            : (string.IsNullOrEmpty(i.Status)
                                                            ? 2 : 1))
                                           .ThenBy(i => i.CreatedDate ?? DateTime.MaxValue)
                                    : query.OrderBy(i => i.Status.ToLower() == "pending" ? 0
                                                            : (string.IsNullOrEmpty(i.Status)
                                                            ? 2 : 1))
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
                    InvestmentName = i.InvestmentName,
                    Reference = i.Reference,
                    Status = string.IsNullOrEmpty(i.Status) ? "Pending" : i.Status,
                    i.RejectionMemo,
                    i.RejectedBy,
                    i.CreatedDate,
                    DaysCount = (!string.IsNullOrEmpty(i.Status) && i.Status.ToLower() == "pending" && i.CreatedDate != null)
                                    ? GetReadableDuration(i.CreatedDate.Value, now)
                                    : null
                }).ToList();

                if (pagedData.Any())
                    return Ok(new { items = pagedData, totalCount });

                return Ok(new { Success = false, Message = "Data not found." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpGet("get-daf-providers")]
        public async Task<IActionResult> GetDAFProviders()
        {
            try
            {
                var dafProviders = await _context.DAFProviders
                                                 .Select(x => new
                                                 {
                                                     Value = x.ProviderName,
                                                     Link = x.ProviderURL
                                                 })
                                                 .ToListAsync();

                if (dafProviders.Any())
                    return Ok(dafProviders);

                return Ok(new { Success = false, Message = "Data not found." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
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

        [HttpPut("update-pending-grant")]
        public async Task<IActionResult> UpdatePendingGrant([FromBody] UpdatePendingGrantsDto pendingGrantsData)
        {
            try
            {
                var pendingGrant = await _context.PendingGrants
                                                    .Include(p => p.Campaign)
                                                    .Include(p => p.User)
                                                    .FirstOrDefaultAsync(i => i.Id == pendingGrantsData.Id);
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

                    bool isFreeUser = user.IsFreeUser;
                    decimal totalInvestmentCampaignFee = (pendingGrandAmount * 0.05m); //Investment Campaign Fee
                    decimal amount = pendingGrandAmount - totalInvestmentCampaignFee;

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

                    await new UsersController(_context, _blobContainerClient, _repository, _mailService, _configuration, _mapper, _httpContextAccessors).
                        UpdateAccountBalance(pendingGrant.User.Email, amount, pendingGrandAmount, pendingGrant.Reference, null, pendingGrant.Id);

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

                        await new RecommendationsController(_context, _repository, _mapper, _mailService, _httpContextAccessors).
                            CreateRecommendation(recommendation);
                    }

                    if (_environment.EnvironmentName == "Production" && isFreeUser && !string.IsNullOrEmpty(user.KlaviyoProfileId))
                    {
                        await UpdateKlaviyoUserStatus(user.KlaviyoProfileId);
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
                                                    .Where(x => x.UserId == pendingGrant!.UserId && x.PendingGrantsId == pendingGrant.Id)
                                                    .OrderByDescending(x => x.Id)
                                                    .FirstOrDefaultAsync();

                            if (existingLog?.NewValue <= user.AccountBalance)
                            {
                                await AccountBalanceChangeLog(user, -existingLog.NewValue.Value, $"Reverted Pending Grant, Pending Grant Id= {pendingGrant?.Id}", pendingGrant!.Id, existingLog.Reference);
                                await _context.SaveChangesAsync();
                            }
                            else
                            {
                                return Ok(new { Success = false, Message = "User do not have sufficient wallet balance to revert the pending grant" });
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
                                decimal totalInvestmentCampaignFee = (pendingGrandAmount * 0.05m); //Investment Campaign Fee
                                decimal amount = pendingGrandAmount - totalInvestmentCampaignFee;

                                if (recommendation?.Status != "rejected")
                                    await AccountBalanceChangeLog(user, recommendation?.Amount ?? 0, $"Reverted Recommendation Amount, Recommendation Id= {recommendation?.Id}", pendingGrant.Id, existingLog.Reference, recommendation?.Campaign?.Name);

                                await AccountBalanceChangeLog(user, -amount, $"Reverted Pending Grant, Pending Grant Id= {pendingGrant.Id}", pendingGrant.Id, existingLog.Reference);
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
                    Message = $"Grant set {pendingGrantsData.Status}",
                    Data = new
                    {
                        RejectedBy = loginUser.UserName.Trim().ToLower(),
                        RejectionMemo = pendingGrant?.RejectionMemo
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost("create")]
        public async Task<IActionResult> AddPendingGrants([FromBody] PendingGrantsDto pendingGrants)
        {
            if (pendingGrants == null)
            {
                return BadRequest(new { Success = false, Message = "Data type is invalid" });
            }
            if (pendingGrants.Amount <= 0)
            {
                return BadRequest(new { Success = false, Message = "Amount must be greater than zero." });
            }

            CommonResponse response = new();
            try
            {
                var allEmailTasks = new List<Task>();

                var requestOrigin = HttpContext?.Request.Headers["Origin"].ToString();
                string adminEmail = _keyVaultConfigService.GetAdminEmail();

                var email = pendingGrants?.Email ?? string.Empty;
                bool isAnonymous = pendingGrants!.IsAnonymous;
                decimal amount = Convert.ToDecimal(pendingGrants?.Amount);
                decimal amountAfterFees = amount - (amount * 0.05m);

                if (isAnonymous)
                {
                    var existingEmail = _context.Users.Where(u => u.Email.ToLower() == email.ToLower().Trim()).Any();
                    if (existingEmail)
                    {
                        response.Success = false;
                        response.Message = $"Email '{email}' is already taken.";
                        return Ok(response);
                    }
                }

                var user = isAnonymous ? await RegisterAnonymousUser(pendingGrants!) : await GetUserFromContext(email);

                if (!string.IsNullOrEmpty(pendingGrants!.Reference))
                    user.IsActive = true;

                decimal investedAmount = pendingGrants?.InvestedSum > 0 ? Convert.ToDecimal(pendingGrants.InvestedSum) : Convert.ToDecimal(pendingGrants!.Amount);
                int? investmentId = !string.IsNullOrEmpty(pendingGrants.InvestmentId) ? Convert.ToInt32(pendingGrants.InvestmentId) : null;
                var campaign = await _context.Campaigns.FirstOrDefaultAsync(i => i.Id == investmentId);
                var investmentName = campaign?.Name;

                var pendingGrant = new PendingGrants
                {
                    UserId = user.Id,
                    Amount = amount.ToString(),
                    AmountAfterFees = amountAfterFees,
                    DAFProvider = pendingGrants.DAFProvider,
                    DAFName = pendingGrants.DAFName,
                    Campaign = campaign,
                    InvestedSum = investedAmount.ToString(),
                    status = "Pending",
                    Reference = !string.IsNullOrWhiteSpace(pendingGrants.Reference) ? pendingGrants.Reference : null,
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now
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

                _ = Task.Run(async () =>
                {
                    string email = user.Email;
                    string? userName = user.UserName;
                    string? firstName = user.FirstName;
                    string? lastName = user.LastName;

                    if (isAnonymous)
                    {
                        allEmailTasks.Add(SendWelcomeEmail(requestOrigin!, email, userName, firstName!));
                    }

                    if (user?.OptOutEmailNotifications == null || !(bool)user.OptOutEmailNotifications)
                    {
                        allEmailTasks.Add(pendingGrants.DAFProvider == "foundation grant"
                                            ? SendFoundationEmail(requestOrigin!, investmentName!, investedAmount, email, firstName!)
                                            : SendDAFEmail(requestOrigin!, email, firstName!, dafProviderURL, pendingGrants.DAFProvider, investedAmount, investmentName!));
                    }

                    string paymentMethod = pendingGrants.DAFProvider == "foundation grant"
                                            ? "Foundation Grant"
                                            : !string.IsNullOrEmpty(pendingGrants.DAFName)
                                                ? $"{pendingGrants.DAFProvider} - {pendingGrants.DAFName}"
                                                : pendingGrants.DAFProvider ?? string.Empty;

                    allEmailTasks.Add(SendPendingGrantEmailToAdmin(adminEmail, amount, firstName!, lastName!, paymentMethod));

                    await Task.WhenAll(allEmailTasks);
                });

                response.Success = true;
                response.Message = "Grant created successful.";
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"An error occurred: {ex.Message}";
                return BadRequest(response);
            }
        }

        [HttpGet("Export")]
        public async Task<IActionResult> ExportPendingGrants()
        {
            var data = await _context.PendingGrants
                                        .Include(i => i.Campaign)
                                        .Include(i => i.User)
                                        .Include(i => i.RejectedByUser)
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
                    "Investment Name", "Grant Source", "Status", "Date Created", "Rejection Memo", "Rejected By", "Rejection Date", "Day Count"
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
                    worksheet.Cell(row, col++).Value = dto.CreatedDate?.ToString("MM-dd-yyyy HH:mm");
                    worksheet.Cell(row, col++).Value = dto.RejectionMemo;
                    worksheet.Cell(row, col++).Value = dto.RejectedByUser?.FirstName != null ? dto.RejectedByUser?.FirstName : null;

                    var rejectionDateCell = worksheet.Cell(row, col++);
                    rejectionDateCell.Value = dto.RejectionDate;
                    rejectionDateCell.Style.DateFormat.Format = "MM/dd/yyyy";

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

        private async Task<User> RegisterAnonymousUser(PendingGrantsDto dto)
        {
            var userName = $"{dto.FirstName}{dto.LastName}".Trim().ToLower();
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
                Password = defaultPassword,
                Email = dto.Email
            };

            await _repository.UserAuthentication.RegisterUserAsync(registrationDto, UserRoles.User);

            var user = await _repository.UserAuthentication.GetUserByUserName(userName);
            user.IsFreeUser = true;

            if (_environment.EnvironmentName == "Production")
            {
                var profileId = await CreateKlaviyoProfile(user);
                if (!string.IsNullOrEmpty(profileId))
                    user.KlaviyoProfileId = profileId;
            }
            await _repository.UserAuthentication.UpdateUser(user);
            await _repository.SaveAsync();

            return user;
        }

        private async Task<User> GetUserFromContext(string email)
        {
            var identity = _httpContextAccessors.HttpContext?.User.Identity as ClaimsIdentity;
            var userId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                return await _repository.UserAuthentication.GetUserById(userId);
            }
            else
            {
                return await _repository.UserAuthentication.GetUserByEmail(email);
            }
        }

        private async Task<string?> CreateKlaviyoProfile(User user)
        {
            string klaviyoApiKey = _keyVaultConfigService.GetKlaviyoApiKey();
            string klaviyoListKey = _keyVaultConfigService.GetKlaviyoListKey();

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Klaviyo-API-Key {klaviyoApiKey}");
            _httpClient.DefaultRequestHeaders.Add("revision", "2024-05-15");

            var payload = new
            {
                data = new
                {
                    type = "profile",
                    attributes = new
                    {
                        email = user.Email,
                        first_name = user.FirstName,
                        last_name = user.LastName,
                        properties = new
                        {
                            isFreeUser = true,
                            name = $"{user.FirstName} {user.LastName}"
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var klaviyoResponse = await _httpClient.PostAsync("https://a.klaviyo.com/api/profiles/", content);

            if (!klaviyoResponse.IsSuccessStatusCode)
                return null;

            var responseContent = await klaviyoResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var profileId = doc.RootElement.GetProperty("data").GetProperty("id").GetString();

            var listPayload = new
            {
                data = new[]
                {
                    new { type = "profile", id = profileId }
                }
            };
            var listJson = JsonSerializer.Serialize(listPayload);
            var listContent = new StringContent(listJson, Encoding.UTF8, "application/json");
            string url = $"https://a.klaviyo.com/api/lists/{klaviyoListKey}/relationships/profiles/";

            await _httpClient.PostAsync(url, listContent);
            return profileId;
        }

        private async Task UpdateKlaviyoUserStatus(string klaviyoId)
        {
            string apiKey = _keyVaultConfigService.GetKlaviyoApiKey();

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Klaviyo-API-Key {apiKey}");
            _httpClient.DefaultRequestHeaders.Add("revision", "2024-05-15");

            var payload = new
            {
                data = new
                {
                    type = "profile",
                    id = klaviyoId,
                    attributes = new
                    {
                        properties = new { isFreeUser = false }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await _httpClient.PatchAsync($"https://a.klaviyo.com/api/profiles/{klaviyoId}/", content);
        }

        private async Task AccountBalanceChangeLog(User user, decimal amount, string type, int pendingGrandId, string? reference = null, string? investmentName = null)
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
                InvestmentName = investmentName
            };

            await _context.AccountBalanceChangeLogs.AddAsync(log);
            user.AccountBalance = log.NewValue;
        }

        private async Task SendDAFEmail(string request, string email, string firstName, string? dafProviderURL, string dafProviderName, decimal amount, string investmentName)
        {
            string logoUrl = $"{request}/logo-for-email.png";
            string logoHtml = $@"
                                <div style='text-align: center;'>
                                    <a href='https://investment-campaign.org' target='_blank'>
                                        <img src='{logoUrl}' alt='Logo' width='300' height='150' />
                                    </a>
                                </div>";

            string formattedAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", amount);

            string investmentScenarios = !string.IsNullOrEmpty(investmentName)
                                            ? $"to support <b>{investmentName}</b> through Investment Campaign"
                                            : "through Investment Campaign";

            var dafLinkScenarios = !string.IsNullOrEmpty(dafProviderURL)
                                        ? $@"<a href='{dafProviderURL}' target='_blank'>{dafProviderName}</a>"
                                        : dafProviderName;

            var subject = "Thank You for Your Commitment – Next Steps for Your DAF Donation";
            var body = logoHtml + @$"
                                <p><b>Hi {firstName},</b></p>
                                <p>Thank you for your generous <b>{formattedAmount}</b> commitment {investmentScenarios}!.🙌</p>  
                                <p>We’re thrilled to have you on this journey of catalytic impact.</p>
                                <p>Since you’re donating through your <b>Donor-Advised Fund (DAF)</b> — in this case, <b>{dafProviderName}</b> — here’s how to complete your grant:</p>
                                <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>                             
                                <p><div style='font-size: 20px;'><b>✅ How to Make Your Donation</b></div></p>
                                <ol>
                                    <li><b>Log in </b>to your {dafLinkScenarios} account.</li>
                                    <li><b>Initiate a grant </b>using the following details:</li>
                                    <ul style='list-style-type:disc;'>
                                        <li><b>Donation Recipient:</b> Investment Campaign Foundation</li>
                                        <li><b>Amount:</b> {formattedAmount}</li>
                                        <li><b>EIN:</b> 86-2370923</li>
                                        <li><b>Email:</b> <a href='mailto:investment.campaign@mailinator.com'>investment.campaign@mailinator.com</a></li>
                                        <li><b>Address:</b> 213 West 85th Street, New York, NY 10024</li>
                                    </ul>
                                    <li><b>Forward your confirmation</b> email to <b><a href='mailto:investment.campaign@mailinator.com'>investment.campaign@mailinator.com</a></b> so we can apply your investment as soon as possible.</li>
                                </ol>
                                <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                                <p>Your contribution helps move capital to where it’s most needed — thank you for being a part of this powerful movement. We’re honored to work alongside you to fund bold solutions for a better world.</p>
                                <p style='margin-bottom: 0px;'>Let’s get your capital to work.🚀</p>
                                <p style='margin-bottom: 0px; margin-top: 0px;'>Warmly,</p>
                                <p style='margin-bottom: 0px; margin-top: 0px;'><b>Shabbir Bharmal + The Investment Campaign Team</b></p>
                                <p style='margin-bottom: 0px; margin-top: 0px;'>Powered by the Investment Campaign</p>
                                <p style='margin-bottom: 0px; margin-top: 0px;'>🌐 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                <p style='margin-top: 0px;'><a href='{request}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                ";

            await _mailService.SendMailAsync(email, subject, "", body);
        }

        private async Task SendFoundationEmail(string request, string investmentName, decimal amount, string email, string firstName)
        {
            string logoUrl = $"{request}/logo-for-email.png";
            string logoHtml = $@"
                                <div style='text-align: center;'>
                                    <a href='https://investment-campaign.org' target='_blank'>
                                        <img src='{logoUrl}' alt='Logo' width='300' height='150' />
                                    </a>
                                </div>";

            string formattedAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", amount);

            string investmentScenarios = !string.IsNullOrEmpty(investmentName)
                                            ? $"to invest in {investmentName} through Investment Campaign"
                                            : "through Investment Campaign";

            var subject = $"You're In! Next Step to Complete Your {formattedAmount} Commitment to Foundation Grant";
            var body = logoHtml + @$"
                                <p><b>Hi {firstName},</b></p>
                                <p>Thank you for your generous <b>{formattedAmount} commitment </b> {investmentScenarios}.</p>  
                                <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                                <p><div style='font-size: 20px;'><b>✅ How to Make Your Donation</b></div></p>
                                <ol>
                                    <li><b>Initiate a grant </b>to the following recipient:</li>
                                    <ul style='list-style-type:disc;'>
                                        <li><b>Donation Recipient:</b> Investment Campaign</li>
                                        <li><b>Amount:</b> {formattedAmount}</li>
                                        <li><b>EIN:</b> 86-2370923</li>
                                        <li><b>Email:</b> <a href='mailto:investment.campaign@mailinator.com'>investment.campaign@mailinator.com</a></li>
                                        <li><b>Address:</b> 213 West 85th Street, New York, NY 10024</li>
                                    </ul>
                                    <li><b>Forward confirmation:</b></li>
                                    <p style='margin-top: 0px;'>Once your grant is submitted, please forward the confirmation email to <a href='mailto:investment.campaign@mailinator.com'>investment.campaign@mailinator.com</a> so we can apply your investment immediately.</p>
                                </ol>
                                <div style='margin-bottom: 20px; margin-top: 20px;'><hr></div>
                                <p>We’re honored to partner with you in moving catalytic capital to where it can make the greatest difference. Thank you for helping fund what the world truly needs.</p>
                                <p style='margin-bottom: 0px;'><b>Let’s get your capital to work.</b></p>
                                <p style='margin-bottom: 0px; margin-top: 0px;'>Warmly,</p>
                                <p style='margin-bottom: 0px; margin-top: 0px;'>Shabbir Bharmal + The Investment Campaign Team</p>
                                <p style='margin-top: 0px;'><i>Powered by the Investment Campaign</i></p>
                                <p>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                <p><a href='{request}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                ";

            await _mailService.SendMailAsync(email, subject, "", body);
        }

        private async Task SendWelcomeEmail(string request, string emailTo, string userName, string firstName)
        {
            string logoUrl = $"{request}/logo-for-email.png";
            string logoHtml = $@"
                                <div style='text-align: center;'>
                                    <a href='https://investment-campaign.org' target='_blank'>
                                        <img src='{logoUrl}' alt='Logo' width='300' height='150' />
                                    </a>
                                </div>";

            string resetPasswordUrl = $"{request}/forgot-password";
            string userSettingsUrl = $"{request}/settings";
            string subject = "Welcome to Investment Campaign - Let’s Move Capital That Matters 💥";

            var body = logoHtml + $@"
                                    <html>
                                        <body>
                                            <p><b>Hi {firstName},</b></p>
                                            <p>Welcome to <b>Investment Campaign</b> - the movement turning philanthropic dollars into <b>powerful, catalytic investments</b> that fuel real change.</p>
                                            <p>You’ve just joined what we believe will become the <b>largest community of catalytic capital champions</b> on the planet. Whether you're a donor, funder, or impact-curious investor - you're in the right place.</p>
                                            <p>Your Investment Campaign username: <b>{userName}</b></p>
                                            <p>To set your password: <a href='{resetPasswordUrl}' target='_blank'>Click here</a></p>
                                            <p>Here’s what you can do right now on Investment Campaign:</p>
                                            <p>🔎 <b>1. Discover Investments Aligned with Your Values</b></p>
                                            <p style='margin-bottom: 0px;'>Use your <b>DAF, foundation, or donation capital</b> to fund vetted companies, VC funds, and loan structures — not just nonprofits.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='{request}/find'>Browse live investment opportunities</a></p>
                                            <p>🤝 <b>2. Connect with Like-Minded Peers</b></p>
                                            <p style='margin-bottom: 0px;'>Follow friends and colleagues, share opportunities, or keep your giving private — you’re in control.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='{request}/community'>Explore the Investment Campaign community</a></p>
                                            <p>🗣️ <b>3. Join or Start a Group</b></p>
                                            <p style='margin-bottom: 0px;'>Find (or create!) groups around shared causes and funding themes — amplify what matters to you.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='{request}/community'>See active groups and start your own</a></p>
                                            <p>🚀 <b>4. Recommend Deals You Believe In</b></p>
                                            <p style='margin-bottom: 0px;'>Champion investments that should be seen — and funded — by others in the community.</p>
                                            <p style='margin-top: 0px;'>➡️ <a href='https://investment-campaign.org/lead-investor/'>Propose an opportunity</a></p>
                                            <p>We’re here to help you put your capital to work — boldly, effectively, and in community.</p>
                                            <p>Thanks for joining us. Let’s fund what we wish existed — together.</p>
                                            <p style='margin-bottom: 0px;'><b>The Investment Campaign Team</b></p>
                                            <p style='margin-top: 0px;'>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                            <p>Have questions? Email Ken at <a href='mailto:investment.campaign@mailinator.com'>investment.campaign@mailinator.com</a></p>
                                            <p><a href='{request}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                        </body>
                                    </html>";

            await _mailService.SendMailAsync(emailTo, subject, "", body);
        }

        private async Task SendPendingGrantEmailToAdmin(string adminEmail, decimal amount, string firstName, string lastName, string paymentMethod)
        {
            string formattedAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", amount);
            string subject = "New pending grant on production";

            var body = $@"
                        <html>
                            <body>
                                Hello Team!<br/>
                                We have a new pending grant on Production:<br/><br/>
                                <b>Amount:</b> {formattedAmount}<br/>
                                <b>Name:</b> {firstName} {lastName}<br/>
                                <b>Method of payment:</b> {paymentMethod}</p><br/>
                                Thanks!
                            </body>
                        </html>";

            await _mailService.SendMailAsync(adminEmail, subject, "", body);
        }
    }
}
