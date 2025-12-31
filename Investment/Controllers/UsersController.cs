using AutoMapper;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using ClosedXML.Excel;
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
    public class UsersController : ControllerBase
    {
        private readonly RepositoryContext _context;
        private readonly BlobContainerClient _blobContainerClient;
        protected readonly IRepositoryManager _repository;
        private readonly IMailService _mailService;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public UsersController(
            RepositoryContext context,
            BlobContainerClient blobContainerClient,
            IRepositoryManager repository,
            IMailService mailService,
            IConfiguration configuration,
            IMapper mapper,
            IHttpContextAccessor httpContextAccessors)
        {
            _context = context;
            _repository = repository;
            _mailService = mailService;
            _configuration = configuration;
            _mapper = mapper;
            _blobContainerClient = blobContainerClient;
            _httpContextAccessor = httpContextAccessors;
        }

        [HttpPost("get-users")]
        public async Task<IActionResult> GetUsers([FromBody] PaginationDto pagination)
        {
            try
            {
                bool isAsc = pagination?.SortDirection?.ToLower() == "asc";
                int page = pagination?.CurrentPage ?? 1;
                int pageSize = pagination?.PerPage ?? 50;

                var adminRoleId = await _context.Roles.Where(i => i.Name == "Admin").Select(i => i.Id).FirstOrDefaultAsync();
                var adminUsers = await _context.UserRoles.Where(i => i.RoleId == adminRoleId).Select(i => i.UserId).ToArrayAsync();

                var query = _context.Users.Where(u => !adminUsers.Contains(u.Id));

                if (!string.IsNullOrWhiteSpace(pagination?.SearchValue))
                {
                    var searchValue = pagination.SearchValue.Trim().ToLower();

                    query = query.Where(u =>
                                        (u.FirstName ?? "").Trim().ToLower().Contains(searchValue) ||
                                        (u.LastName ?? "").Trim().ToLower().Contains(searchValue) ||
                                        ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim().ToLower().Contains(searchValue) ||
                                        (u.Email ?? "").Trim().ToLower().Contains(searchValue));
                }

                query = pagination?.SortField?.ToLower() switch
                {
                    "fullname" => isAsc
                                        ? query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                                        : query.OrderByDescending(u => u.FirstName).ThenByDescending(u => u.LastName),
                    "datecreated" => isAsc
                                        ? query.OrderBy(u => u.DateCreated)
                                        : query.OrderByDescending(u => u.DateCreated),
                    _ => query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                };

                if (pagination?.FilterByGroup == true)
                {
                    query = query.Where(u => u.Requests != null && u.Requests.Any(r => r.GroupToFollow != null));
                }

                var totalCount = await query.CountAsync();

                var users = await query.Skip((page - 1) * pageSize).Take(pageSize)
                                                                    .Include(u => u.Requests!)
                                                                        .ThenInclude(r => r.GroupToFollow!)
                                                                    .Include(u => u.GroupBalances!)
                                                                        .ThenInclude(gb => gb.Group)
                                                                    .ToListAsync();

                var result = users.Select(i => new
                {
                    i.Id,
                    i.FirstName,
                    i.LastName,
                    FullName = i.FirstName + " " + i.LastName,
                    i.UserName,
                    i.AccountBalance,
                    i.Email,
                    i.IsActive,
                    i.DateCreated,

                    GroupNames = string.Join(",", i.Requests!
                                        .Where(r => r.GroupToFollow != null)
                                        .Select(r => r.GroupToFollow!.Name)
                                        .Distinct()),

                    GroupBalances = string.Join(",", i.Requests!
                                            .Where(r => r.GroupToFollow != null)
                                            .Select(r =>
                                            {
                                                var groupId = r.GroupToFollow!.Id;
                                                var balance = i.GroupBalances!.FirstOrDefault(gb => gb.Group.Id == groupId);
                                                return (balance != null ? balance.Balance : 0m).ToString("F2");
                                            })
                                            .Distinct())
                }).ToList();

                if (result.Any())
                    return Ok(new { items = result, totalCount = totalCount });

                return Ok(new { Success = false, Message = "Data not found." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<User>>> GetUsers(int? groupId = null, string? searchValue = null, string? sortField = null, string? sortDirection = null)
        {
            var adminRole = await _context.Roles.FirstOrDefaultAsync(i => i.Name == "Admin");
            var adminUsers = await _context.UserRoles.Where(i => adminRole != null && i.RoleId == adminRole.Id).Select(i => i.UserId).ToArrayAsync();

            var usersQuery = _context.Users.Where(i => !adminUsers.Contains(i.Id));

            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                usersQuery = usersQuery.Where(u =>
                                        (u.FirstName + " " + u.LastName).ToLower().Contains(searchValue) ||
                                        u.Email.ToLower().Contains(searchValue));
            }

            bool isAsc = sortDirection?.ToLower() == "asc";

            usersQuery = sortField?.ToLower() switch
            {
                "fullname" => isAsc
                                    ? usersQuery.OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                                    : usersQuery.OrderByDescending(u => u.FirstName).ThenByDescending(u => u.LastName),
                "datecreated" => isAsc
                                    ? usersQuery.OrderBy(u => u.DateCreated)
                                    : usersQuery.OrderByDescending(u => u.DateCreated),
                _ => usersQuery.OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            };

            if (groupId != null)
            {
                usersQuery = usersQuery
                                    .Where(i => i.Requests != null &&
                                        i.Requests.Any(r =>
                                        r.Status == "accepted" &&
                                        r.GroupToFollow != null &&
                                        r.GroupToFollow.Id == groupId &&
                                        r.RequestOwner != null &&
                                        r.RequestOwner.Id == i.Id
                                    ));
            }

            var users = await usersQuery.ToListAsync();

            var userIds = users.Select(u => u.Id).ToList();
            var groupAccountBalances = await _context.GroupAccountBalance
                                                .Include(g => g.Group)
                                                .Where(gab => userIds.Contains(gab.User.Id))
                                                .ToListAsync();

            var groupAccountBalancesDto = _mapper.Map<List<GroupAccountBalanceDto>>(groupAccountBalances);

            var userIdToGroupBalanceMap = groupId == null ?
                                                        groupAccountBalancesDto
                                                            .GroupBy(gab => gab.UserId)
                                                            .ToDictionary(g => g.Key, g => g.LastOrDefault()) :
                                                        groupAccountBalancesDto
                                                            .GroupBy(gab => gab.UserId)
                                                            .ToDictionary(g => g.Key, g => g.Where(g => g.GroupId == groupId).FirstOrDefault());

            users.ForEach(user =>
            {
                user.Groups = null;
                user.GroupBalances = null;

                if (userIdToGroupBalanceMap.TryGetValue(user.Id, out var groupBalance))
                {
                    user.GroupAccountBalance = groupBalance;
                }
            });

            if (_context.Campaigns == null)
            {
                return NotFound();
            }
            return users;
        }

        [HttpGet("Export")]
        public async Task<IActionResult> GetExportUsers()
        {
            var adminRole = await _context.Roles.FirstOrDefaultAsync(i => i.Name == "Admin");
            var adminUsers = await _context.UserRoles
                                    .Where(i => adminRole != null && i.RoleId == adminRole.Id)
                                    .Select(i => i.UserId)
                                    .ToListAsync();

            var users = await _context.Users
                                .Where(u => !adminUsers.Contains(u.Id))
                                .ToListAsync();

            var userIds = users.Select(u => u.Id).ToList();
            var userEmails = users.Select(u => u.Email).ToList();

            var groups = await _context.Groups
                                        .Where(g => g.Owner != null)
                                        .Select(g => new { g.Id, g.Name, OwnerId = g.Owner!.Id })
                                        .ToListAsync();

            var userInvestmentsDict = _context.Recommendations
                                                .Where(r => userEmails.Contains(r.UserEmail!) &&
                                                            (r.Status == "approved" || r.Status == "pending"))
                                                .AsEnumerable()
                                                .GroupBy(r => r.UserEmail)
                                                .ToDictionary(
                                                    g => g.Key!.ToLower().Trim(),
                                                    g => g.Sum(r => r.Amount)
                                                );

            var userGroupsDict = _context.Requests
                                            .Where(r => r.RequestOwner != null
                                                        && r.GroupToFollow != null
                                                        && userIds.Contains(r.RequestOwner.Id)
                                                        && r.Status == "accepted")
                                            .Select(r => new
                                            {
                                                RequestOwnerId = r.RequestOwner!.Id,
                                                GroupId = r.GroupToFollow!.Id
                                            })
                                            .AsEnumerable()
                                            .GroupBy(r => r.RequestOwnerId)
                                            .ToDictionary(
                                                g => g.Key,
                                                g => g.Select(x => x.GroupId).Distinct().ToList()
                                            );

            var allThemes = await _context.Themes
                                            .Select(t => new { t.Id, t.Name })
                                            .ToListAsync();

            var feedbacks = await _context.InvestmentFeedback
                                            .GroupBy(f => f.UserId)
                                            .Select(g => g.OrderByDescending(f => f.Id).FirstOrDefault())
                                            .ToListAsync();

            var feedbackDict = feedbacks.ToDictionary(f => f!.UserId, f => f);

            var userDtos = users.Select(user =>
            {
                var feedback = feedbackDict.TryGetValue(user.Id, out var fb) ? fb : null;

                decimal amountInvested = userInvestmentsDict != null && userInvestmentsDict.TryGetValue(user.Email.ToLower().Trim(), out var investedAmount) ? investedAmount ?? 0m : 0m;
                decimal accountBalance = user.AccountBalance ?? 0m;

                var followingGroupIds = userGroupsDict.TryGetValue(user.Id, out var groupIds) ? groupIds : new List<int>();
                var followingGroupNames = string.Join(", ", groups
                                                .Where(g => followingGroupIds.Contains(g.Id))
                                                .Select(g => g.Name));

                var ownedGroup = groups.FirstOrDefault(g => g.OwnerId == user.Id);
                var ownerGroupName = ownedGroup?.Name ?? "";

                var themeIds = feedback?.Themes?
                                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                        .Select(int.Parse)
                                        .Distinct()
                                        .ToList() ?? new List<int>();

                var themeNames = string.Join(", ", allThemes
                                        .Where(t => themeIds.Contains(t.Id))
                                        .Select(t => t.Name));

                var investmentTypeIds = feedback?.InterestedInvestmentType?
                                                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(int.Parse)
                                                    .Distinct()
                                                    .ToList() ?? new List<int>();

                var investmentTypes = string.Join(", ", investmentTypeIds.Select(id => Enum.GetName(typeof(InterestedInvestmentType), id)));

                return new UsersExportDto
                {
                    UserName = user.UserName,
                    Id = user.Id,
                    FirstName = user.FirstName!,
                    LastName = user.LastName!,
                    Email = user.Email,
                    IsActive = user.IsActive == true ? "Active" : "Inactive",
                    AmountInvested = amountInvested.ToString("0.00"),
                    AmountInAccount = accountBalance.ToString("0.00"),
                    FollowingGroups = followingGroupNames,
                    GroupOwner = ownerGroupName,
                    SurveyThemes = themeNames,
                    SurveyAdditionalThemes = feedback?.AdditionalThemes,
                    SurveyInvestmentInterest = investmentTypes,
                    SurveyRiskTolerance = feedback?.RiskTolerance.ToString() ?? "",
                    DateCreated = user.DateCreated
                };
            }).ToList();

            string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            string fileName = "users.xlsx";

            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet worksheet = workbook.Worksheets.Add("Users");

                var headers = new[]
                {
                    "UserName", "Id", "FirstName", "LastName", "Email", "IsActive",
                    "AmountInvested", "AmountInAccount", "FollowingGroups",
                    "OwnedGroupName", "SurveyThemes", "SurveyAdditionalThemes", "SurveyInvestmentInterest", "SurveyRiskTolerance", "DateCreated"
                };

                for (int i = 0; i < headers.Length; i++)
                    worksheet.Cell(1, i + 1).Value = headers[i];

                worksheet.Row(1).Style.Font.Bold = true;

                for (int i = 0; i < userDtos.Count; i++)
                {
                    var row = worksheet.Row(i + 2);
                    var dto = userDtos[i];

                    row.Cell(1).Value = dto.UserName;
                    row.Cell(2).Value = dto.Id;
                    row.Cell(3).Value = dto.FirstName;
                    row.Cell(4).Value = dto.LastName;
                    row.Cell(5).Value = dto.Email;
                    row.Cell(6).Value = dto.IsActive;

                    row.Cell(7).Value = decimal.Parse(dto.AmountInvested);
                    row.Cell(7).Style.NumberFormat.Format = "$#,##0.00";

                    row.Cell(8).Value = decimal.Parse(dto.AmountInAccount);
                    row.Cell(8).Style.NumberFormat.Format = "$#,##0.00";

                    row.Cell(9).Value = dto.FollowingGroups;
                    row.Cell(10).Value = dto.GroupOwner;
                    row.Cell(11).Value = dto.SurveyThemes;
                    row.Cell(12).Value = dto.SurveyAdditionalThemes;
                    row.Cell(13).Value = dto.SurveyInvestmentInterest;
                    row.Cell(14).Value = dto.SurveyRiskTolerance;

                    row.Cell(15).Value = dto.DateCreated;
                    row.Cell(15).Style.DateFormat.Format = "MM/dd/yyyy";
                    row.Cell(15).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                }

                worksheet.Columns().AdjustToContents();

                foreach (var column in worksheet.Columns())
                    column.Width += 10;

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, contentType, fileName);
                }
            }
        }

        private async Task SendEmail(string origin, string email, string firstName, string lastName, decimal amountAfterFees, decimal originalAmount)
        {
            string logoUrl = $"{origin}/logo-for-email.png";
            string logoHtml = $@"
                                <div style='text-align: center;'>
                                    <a href='https://investment-campaign.org' target='_blank'>
                                        <img src='{logoUrl}' alt='Logo' width='300' height='150' />
                                    </a>
                                </div>";

            string formattedAmountAfterFees = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", Convert.ToDecimal(amountAfterFees));
            string formattedOriginalAmount = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "${0:N2}", Convert.ToDecimal(originalAmount));

            var subject = "Your Grant Was Received — Let’s Put It to Work";

            var body = logoHtml + $@"
                                    <p><b>Hi {firstName},</b></p>
                                    <p>We’re excited to confirm that your <b>{formattedOriginalAmount} grant</b> has been received. After the up front 5% Investment Campaign fee, you’ll now see {formattedAmountAfterFees} in your account!</p>
                                    <p>Your generosity is now ready to move — fueling bold founders, catalytic funds, and the innovations our future depends on.</p>
                                    <p>Thank you for choosing to <b>activate your donor capital with purpose</b>.</p>
                                    <p style='margin-bottom: 0px;'><b>🔗 Ready to invest in impact?</b></p>
                                    <p style='margin-top: 0px;'><a href='{origin}/find'>Start browsing live opportunities</a></p>
                                    <p>Together, we’re bridging the gap between intention and action — and unlocking a new future for how capital drives change.</p>
                                    <p style='margin-bottom: 0px;'>Let’s get to work.</p>
                                    <p style='margin-top: 0px;'>— The Investment Campaign Team</p>
                                    <p style='margin-bottom: 0px;'>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                    <p style='margin-top: 0px;'>Need help? Email us at <a href='mailto:investment.campaign@mailinator.com'>investment.campaign@mailinator.com</a></p>
                                    <p><a href='{origin}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                    <br/><br/><br/>
                                    <p style='font-size:10px; line-height:1.4;'><b>*Investment Campaign Fees:</b> A one-time 5% fee is charged up front at the time of donation, covering all administrative costs for the first four years. After that, an annual fee of 1.25% applies to the remaining balance and is deducted from the donor’s Impact Liquidity Balance. If that balance is insufficient, the account will show a negative balance until future liquidity covers the fee. All fees are non-refundable once charged. <a href='https://investment-campaign.org/terms-conditions/' target='_blank'>View full terms and conditions</a></p>
                                    ";

            await _mailService.SendMailAsync(email, subject, plainText: "", body);
        }


        [HttpPut("accountBalance/{groupId}")]
        public async Task<IActionResult> UpdateAccountGroupBalance(int groupId, [FromQuery] string email, [FromQuery] decimal accountBalance)
        {
            CommonResponse response = new();
            try
            {
                if (email == null || email == string.Empty)
                {
                    response.Success = false;
                    response.Message = "User email required.";
                    return Ok(response);
                }

                var group = await _context.Groups.FirstOrDefaultAsync(i => i.Id == groupId);

                var allocatedGroupBalanceTotal = await _context.GroupAccountBalance
                                                            .Where(x => group != null && x.Group.Id == group.Id)
                                                            .SumAsync(x => x.Balance);

                var investedGroupBalanceTotal = await _context.AccountBalanceChangeLogs
                                                            .Where(i => group != null && i.GroupId == group.Id && i.InvestmentName != null)
                                                            .SumAsync(i => (decimal?)i.OldValue - (decimal?)i.NewValue);

                decimal? CurrentBalance = group?.OriginalBalance != null ? group.OriginalBalance : 0;
                CurrentBalance = CurrentBalance == 0 ? CurrentBalance : CurrentBalance - (allocatedGroupBalanceTotal + investedGroupBalanceTotal);

                if (CurrentBalance - accountBalance < 0)
                {
                    response.Success = false;
                    response.Message = "Group current balance value can't be less than 0.";
                    return Ok(response);
                }

                var groupBalance = await _context.GroupAccountBalance
                                                .Include(i => i.Group)
                                                .Include(i => i.User)
                                                .FirstOrDefaultAsync(i => i.User.Email == email
                                                                            && i.Group.Id == groupId
                                                                            && i.User.Email == email);

                var user = await _context.Users.FirstOrDefaultAsync(i => i.Email == email);

                if (groupBalance == null && user != null && group != null)
                {
                    groupBalance = new GroupAccountBalance()
                    {
                        User = user,
                        Group = group,
                        Balance = 0
                    };
                    await _context.GroupAccountBalance.AddAsync(groupBalance);
                    await _context.SaveChangesAsync();
                }

                if (groupBalance!.Balance + accountBalance < 0)
                {
                    response.Success = false;
                    response.Message = "Insufficient allocated fund.";
                    return Ok(response);
                }

                var identity = HttpContext?.User.Identity as ClaimsIdentity == null ? _httpContextAccessor.HttpContext?.User.Identity as ClaimsIdentity : HttpContext.User.Identity as ClaimsIdentity;
                var loginUserId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
                var loginUser = await _repository.UserAuthentication.GetUserById(loginUserId!);

                var accountBalanceChangeLog = new AccountBalanceChangeLog
                {
                    UserId = groupBalance.User.Id,
                    PaymentType = $"Manually, Group Admin: {loginUser.UserName!.Trim().ToLower()}",
                    OldValue = groupBalance.Balance,
                    UserName = groupBalance.User.UserName,
                    NewValue = groupBalance.Balance + accountBalance,
                    GroupId = groupId
                };
                await _context.AccountBalanceChangeLogs.AddAsync(accountBalanceChangeLog);
                await _context.SaveChangesAsync();

                groupBalance.Balance += accountBalance;
                groupBalance.LastUpdated = DateTime.Now;

                user!.IsActive = true;
                await _context.SaveChangesAsync();

                var groupCurrentBalance = CurrentBalance - accountBalance;

                response.Success = true;
                response.Message = $"Group current balance is {groupCurrentBalance}";
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"An error occurred: {ex.Message}";
                return BadRequest(response);
            }
        }


        [HttpPut("accountBalance")]
        public async Task<IActionResult> UpdateAccountBalance(string email, decimal accountBalance, decimal originalAmount, string? reference = null, int? groupId = null, int? pendingGrantsId = null)
        {
            CommonResponse response = new();
            try
            {
                if (string.IsNullOrEmpty(email))
                {
                    response.Success = false;
                    response.Message = "User email required.";
                    return Ok(response);
                }

                var user = await _context.Users.FirstOrDefaultAsync(i => i.Email == email);
                var groupBalance = await _context.GroupAccountBalance.FirstOrDefaultAsync(i => i.User.Email == email && i.Group.Id == groupId);

                if (groupBalance == null && user != null)
                {
                    var group = await _context.Groups.FirstOrDefaultAsync(i => i.Id == groupId);

                    if (group != null)
                    {
                        groupBalance = new GroupAccountBalance()
                        {
                            User = user,
                            Group = group,
                            Balance = 0
                        };
                    }
                }

                if (user?.AccountBalance + accountBalance < 0)
                {
                    response.Success = false;
                    response.Message = "Insufficient balance in user account.";
                    return Ok(response);
                }

                var identity = HttpContext?.User.Identity as ClaimsIdentity == null ? _httpContextAccessor.HttpContext?.User.Identity as ClaimsIdentity : HttpContext.User.Identity as ClaimsIdentity;
                var loginUserId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
                var loginUser = await _repository.UserAuthentication.GetUserById(loginUserId!);

                var accountBalanceChangeLog = new AccountBalanceChangeLog
                {
                    UserId = user!.Id,
                    PaymentType = $"Manually, {loginUser.UserName.Trim().ToLower()}",
                    OldValue = user.AccountBalance,
                    UserName = user.UserName,
                    NewValue = user.AccountBalance + accountBalance,
                    PendingGrantsId = pendingGrantsId != null ? pendingGrantsId : null,
                    Reference = !string.IsNullOrWhiteSpace(reference) ? reference : null
                };
                await _context.AccountBalanceChangeLogs.AddAsync(accountBalanceChangeLog);
                await _context.SaveChangesAsync();

                if (user.OptOutEmailNotifications == null || !user.OptOutEmailNotifications)
                {
                    var request = _httpContextAccessor.HttpContext?.Request.Headers["Origin"].ToString();
                    user.AccountBalance = user.AccountBalance == null ? accountBalance : user.AccountBalance + accountBalance;

                    if (accountBalance > 0 && originalAmount > 0)
                    {
                        _ = SendEmail(request!, email, user.FirstName!, user.LastName!, accountBalance, originalAmount);
                    }
                }
                await _context.SaveChangesAsync();

                response.Success = true;
                response.Message = "Account balance has been updated successfully!";
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"An error occurred: {ex.Message}";
                return BadRequest(response);
            }
        }

        [HttpPut("activate")]
        public async Task<IActionResult> UpdateActiveStatus(string email, bool status)
        {
            if (email == null || email == string.Empty)
            {
                return NotFound();
            }

            var user = await _context.Users.FirstOrDefaultAsync(i => i.Email == email);

            if (user == null)
            {
                return NotFound();
            }

            user.IsActive = status;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        [Authorize(Roles = "User, Admin")]
        public async Task<ActionResult<EditUserDto>> GetUser(TokenDto tokenData)
        {
            if (tokenData.Token == null)
                return BadRequest();

            var user = await _repository.UserAuthentication.GetUser(tokenData.Token);

            if (user == null)
                return BadRequest();

            var campaignId = await _context.Campaigns
                                            .Where(x => x.ContactInfoEmailAddress!.ToLower().Trim() == user.Email.ToLower().Trim() && x.IsActive == true)
                                            .OrderByDescending(x => x.Id)
                                            .Select(x => x.Id)
                                            .FirstOrDefaultAsync();

            var isFeedback = await _context.InvestmentFeedback.AnyAsync(i => i.UserId == user.Id);
            List<string> groupLinks = await _context.Groups.Where(x => x.Owner != null && x.Owner.Id == user.Id && x.Identifier != null).Select(x => x.Identifier!).ToListAsync();

            var dto = new EditUserDto
            {
                Address = user.Address!,
                Email = user.Email,
                FirstName = user.FirstName!,
                LastName = user.LastName!,
                PictureFileName = user.PictureFileName!,
                AccountBalance = user.AccountBalance ?? 0,
                UserName = user.UserName,
                Token = tokenData.Token,
                EmailFromGroupsOn = user.EmailFromGroupsOn,
                EmailFromUsersOn = user.EmailFromUsersOn,
                OptOutEmailNotifications = user.OptOutEmailNotifications,
                IsApprouveRequired = user.IsApprouveRequired,
                IsUserHidden = user.IsUserHidden,
                Feedback = isFeedback,
                IsFreeUser = user.IsFreeUser,
                IsAnonymousInvestment = user.IsAnonymousInvestment,
                GroupLinks = groupLinks.Count > 0 ? groupLinks : new List<string>(),
                InvestmentId = campaignId != null ? campaignId : null
            };
            return dto;
        }

        [HttpPost("{userName}")]
        public async Task<ActionResult<UserDetailDto>> GetUserByUserName(string userName, [FromBody] TokenDto tokenData)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return BadRequest();
            }

            var user = await _repository.UserAuthentication.GetUserByUserName(userName);

            if (user == null)
                return NotFound();

            var dto = new UserDetailDto
            {
                Id = user.Id,
                Address = user.Address!,
                Email = user.Email,
                FirstName = user.FirstName!,
                LastName = user.LastName!,
                UserName = user.UserName,
                PictureFileName = user.PictureFileName!,
                IsFollowing = false,
                IsFollowPending = false,
                IsOwner = false
            };

            if (!string.IsNullOrEmpty(tokenData.Token))
            {
                var userOwner = await _repository.UserAuthentication.GetUser(tokenData.Token);
                var isOwnerUserDetail = user.Id == userOwner.Id;
                dto.IsOwner = isOwnerUserDetail;

                if (userOwner != null && !isOwnerUserDetail)
                {
                    var followingRequest = await _context.Requests.FirstOrDefaultAsync(r => r.RequestOwner != null && r.UserToFollow != null && r.RequestOwner.Id == userOwner.Id && r.UserToFollow.Id == user.Id);
                    if (followingRequest != null)
                    {
                        dto.IsFollowing = true;
                        if (followingRequest.Status == "pending")
                        {
                            dto.IsFollowPending = true;
                        }
                        else
                        {
                            dto.IsFollowPending = false;
                        }
                    }
                    else
                    {
                        if (user.IsUserHidden)
                        {
                            return NotFound();
                        }
                        dto.IsFollowing = false;
                    }
                }
            }

            return dto;
        }

        [HttpPut("edit")]
        [Authorize(Roles = "User, Admin")]
        public async Task<IActionResult> UpdateUser([FromBody] EditUserDto user)
        {
            if (!string.IsNullOrWhiteSpace(user.PictureFile))
            {
                string imageFileName = Guid.NewGuid().ToString() + ".jpg";
                var imageBlob = _blobContainerClient.GetBlockBlobClient(imageFileName);
                var imagestr = user.PictureFile.Substring(user.PictureFile.IndexOf(',') + 1);
                var imageBytes = Convert.FromBase64String(imagestr);

                using (var stream = new MemoryStream(imageBytes))
                {
                    await imageBlob.UploadAsync(stream);
                }
                //var imageOldBlob = _blobContainerClient.GetBlockBlobClient(userDto.PictureFileName);
                //await imageOldBlob.DeleteIfExistsAsync();

                user.PictureFileName = imageFileName;
                await _context.SaveChangesAsync();
            }

            var userEmailOld = (await _repository.UserAuthentication.GetUserByUserName(user.UserName)).Email;
            if (userEmailOld != user.Email)
            {
                var recommendations = await _context.Recommendations
                                                    .Where(item => item.UserEmail == userEmailOld)
                                                    .ToListAsync();

                foreach (var recommendation in recommendations)
                {
                    recommendation.UserEmail = user.Email;
                }

                await _context.SaveChangesAsync();
            }

            var result = await _repository.UserAuthentication.EditUserData(user);

            if (!result.Succeeded)
                return BadRequest();

            var userEntities = await _repository.UserAuthentication.GetUser(user.Token);

            var isFeedback = await _context.InvestmentFeedback.AnyAsync(i => i.UserId == userEntities.Id);
            List<string> groupLinks = await _context.Groups.Where(x => x.Owner != null && x.Owner.Id == userEntities.Id && x.Identifier != null).Select(x => x.Identifier!).ToListAsync();

            var dto = new EditUserDto
            {
                Address = userEntities.Address!,
                Email = userEntities.Email,
                FirstName = userEntities.FirstName!,
                LastName = userEntities.LastName!,
                PictureFileName = userEntities.PictureFileName!,
                AccountBalance = userEntities.AccountBalance ?? 0,
                UserName = userEntities.UserName,
                Token = user.Token,
                EmailFromGroupsOn = userEntities.EmailFromGroupsOn,
                EmailFromUsersOn = userEntities.EmailFromUsersOn,
                OptOutEmailNotifications = userEntities.OptOutEmailNotifications,
                IsApprouveRequired = userEntities.IsApprouveRequired,
                IsUserHidden = userEntities.IsUserHidden,
                Feedback = isFeedback,
                IsFreeUser = userEntities.IsFreeUser,
                IsAnonymousInvestment = userEntities.IsAnonymousInvestment,
                GroupLinks = groupLinks.Count > 0 ? groupLinks : new List<string>()
            };

            return Ok(dto);
        }
    }
}
