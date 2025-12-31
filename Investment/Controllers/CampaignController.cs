// Ignore Spelling: Admin Pdf Dto Sdg Captcha Accessors

using AutoMapper;
using Azure.Communication.Email;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using ClosedXML.Excel;
using Investment.Core.Dtos;
using Investment.Extensions;
using Investment.Repo.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Investment.Service.Interfaces;
using Investment.Core.Entities;
using QRCoder;
using Invest.Core.Entities;

namespace Invest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CampaignController : ControllerBase
    {
        private readonly RepositoryContext _context;
        private readonly BlobContainerClient _blobContainerClient;
        private readonly IMapper _mapper;
        private readonly IMailService _mailService;
        private readonly IRepositoryManager _repository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly KeyVaultConfigService _keyVaultConfigService;
        private readonly HttpClient _httpClient;
        private readonly IWebHostEnvironment _environment;
        private readonly string defaultPassword = "SEcurE!Pa$$w0rd_#2025";

        public CampaignController(RepositoryContext context, BlobContainerClient blobContainerClient, IMapper mapper, IMailService mailService, IRepositoryManager repository, IHttpContextAccessor httpContextAccessors, IWebHostEnvironment environment, KeyVaultConfigService keyVaultConfigService, HttpClient httpClient)
        {
            _context = context;
            _mapper = mapper;
            _mailService = mailService;
            _blobContainerClient = blobContainerClient;
            _repository = repository;
            _httpContextAccessor = httpContextAccessors;
            _environment = environment;
            _keyVaultConfigService = keyVaultConfigService;
            _httpClient = httpClient;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<CampaignCardDto>>> GetCampaigns()
        {
            var campaigns = await GetCampaignsCardDto();
            if (campaigns != null)
                return Ok(campaigns);
            else
                return NotFound();
        }

        [HttpGet("withCategories")]
        public async Task<ActionResult<CampaignCardWithCategories>> GetCampaignsWithCategories(string sourcedBy)
        {
            IEnumerable<CampaignCardDto> campaigns = await GetCampaignsCardDto(sourcedBy);

            if (campaigns == null)
                return NotFound();

            campaigns = campaigns.OrderByDescending(x => x.CurrentBalance);

            var categories = await _repository.Category.GetAll(trackChanges: false);
            var categoriesDto = _mapper.Map<List<CategoryDto>>(categories);
            var investmentTypes = await _context.InvestmentTypes.ToListAsync();

            return new CampaignCardWithCategories
            {
                Campaigns = campaigns,
                Categories = categoriesDto,
                InvestmentTypes = investmentTypes
            };
        }

        [HttpPost("admincampaigns")]
        public async Task<IActionResult> GetAdminCampaigns([FromBody] PaginationDto pagination)
        {
            var recommendations = await _context.Recommendations
                                        .Include(x => x.Campaign)
                                        .Where(x => x.Amount > 0 &&
                                                    x.UserEmail != null &&
                                                    x.Campaign != null &&
                                                    x.Campaign.Id != null &&
                                                    (x.Status!.ToLower() == "approved" || x.Status.ToLower() == "pending"))
                                        .GroupBy(x => x.Campaign!.Id!.Value)
                                        .Select(g => new
                                        {
                                            CampaignId = g.Key,
                                            CurrentBalance = g.Sum(i => i.Amount ?? 0),
                                            NumberOfInvestors = g.Select(r => r.UserEmail).Distinct().Count()
                                        })
                                        .ToDictionaryAsync(x => x.CampaignId);

            List<int>? stages = null;

            if (!string.IsNullOrEmpty(pagination.Stages))
            {
                stages = pagination.Stages
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(int.Parse)
                                    .ToList();
            }

            var query = _context.Campaigns
                                .Where(c =>
                                    (string.IsNullOrEmpty(pagination.SearchValue)
                                        || EF.Functions.Like(c.Name!, $"%{pagination.SearchValue}%"))
                                    && (stages == null
                                        || (c.Stage.HasValue && stages.Contains((int)c.Stage.Value)))
                                )
                                .Select(c => new
                                {
                                    c.Id,
                                    c.Name,
                                    c.CreatedDate,
                                    c.AddedTotalAdminRaised,
                                    c.Stage,
                                    c.IsActive,
                                    c.Property,
                                    OriginalPdfFileName = c.OriginalPdfFileName != null ? c.OriginalPdfFileName : null,
                                    ImageFileName = c.ImageFileName != null ? c.ImageFileName : null,
                                    PdfFileName = c.PdfFileName != null ? c.PdfFileName : null
                                });

            var campaignList = await query.ToListAsync();

            var enrichedCampaigns = campaignList
                                    .Where(c => c.Id != null)
                                    .Select(c =>
                                    {
                                        var hasRec = recommendations.TryGetValue(c.Id!.Value, out var rec);
                                        return new
                                        {
                                            c.Id,
                                            c.Name,
                                            c.CreatedDate,
                                            c.Stage,
                                            c.IsActive,
                                            c.Property,
                                            OriginalPdfFileName = c.OriginalPdfFileName != null ? c.OriginalPdfFileName : null,
                                            ImageFileName = c.ImageFileName != null ? c.ImageFileName : null,
                                            PdfFileName = c.PdfFileName != null ? c.PdfFileName : null,
                                            CurrentBalance = hasRec ? rec!.CurrentBalance : 0,
                                            NumberOfInvestors = hasRec ? rec!.NumberOfInvestors : 0
                                        };
                                    })
                                    .ToList();

            bool isAsc = pagination?.SortDirection?.ToLower() == "asc";
            enrichedCampaigns = pagination?.SortField?.ToLower() switch
            {
                "name" => isAsc ? enrichedCampaigns.OrderBy(x => x.Name).ToList() : enrichedCampaigns.OrderByDescending(x => x.Name).ToList(),
                "createddate" => isAsc ? enrichedCampaigns.OrderBy(x => x.CreatedDate).ToList() : enrichedCampaigns.OrderByDescending(x => x.CreatedDate).ToList(),
                "totalrecommendations" => isAsc ? enrichedCampaigns.OrderBy(x => x.CurrentBalance).ToList() : enrichedCampaigns.OrderByDescending(x => x.CurrentBalance).ToList(),
                "totalinvestors" => isAsc ? enrichedCampaigns.OrderBy(x => x.NumberOfInvestors).ToList() : enrichedCampaigns.OrderByDescending(x => x.NumberOfInvestors).ToList(),
                _ => enrichedCampaigns.OrderByDescending(x => x.CreatedDate).ToList()
            };

            int page = pagination?.CurrentPage ?? 1;
            int pageSize = pagination?.PerPage ?? 50;
            int totalCount = enrichedCampaigns.Count();

            var pagedResult = enrichedCampaigns.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            if (pagedResult.Any())
            {
                return Ok(new
                {
                    items = pagedResult,
                    totalCount
                });
            }

            return Ok(new { Success = false, Message = "Data not found." });
        }

        [HttpGet("network")]
        [Authorize(Roles = "User, Admin")]
        public async Task<ActionResult<IEnumerable<Campaign>>> GetCampaignsNetwork()
        {
            string userId = string.Empty;
            var identity = HttpContext.User.Identity as ClaimsIdentity;
            if (identity != null)
            {
                userId = identity.Claims.FirstOrDefault(i => i.Type == "id")?.Value!;
            }
            var userData = await _context.Users.FirstOrDefaultAsync(i => i.Id == userId);
            var requestsByTheUser = await _context
                                            .Requests
                                            .Include(i => i.RequestOwner)
                                            .Where(i => i.RequestOwner != null && i.RequestOwner.Id == userData!.Id && i.Status == "accepted")
                                            .Include(i => i.UserToFollow)
                                            .Include(i => i.GroupToFollow)
                                            .ToListAsync();

            var folowedUserEmails = requestsByTheUser.Where(i => i.UserToFollow != null).Select(i => i.UserToFollow!.Email).ToList();
            var recommendations = await _context.Recommendations.Include(i => i.Campaign).Where(i => folowedUserEmails.Contains(i.UserEmail!)).ToListAsync();
            var items = recommendations.GroupBy(g => g.Campaign?.Id).Select(g => g.First()).ToList().Select(i => i.Campaign?.Id);

            var data = await _context.Campaigns
                                        .Where(i => i.IsActive!.Value && i.Stage == InvestmentStage.Public)
                                        .Include(i => i.GroupForPrivateAccess)
                                        .Where(i => items.Contains(i.Id))
                                        .ToListAsync();
            if (data.Count > 0)
            {
                var result = _mapper.Map<List<CampaignDto>, List<Campaign>>(data);
                var reccomendations = await _context.Recommendations
                        .Where(x => x.Amount > 0 &&
                                x.UserEmail != null &&
                                (x.Status!.ToLower() == "approved" || x.Status.ToLower() == "pending"))
                        .GroupBy(x => x.Campaign!.Id)
                        .Select(g => new
                        {
                            CampaignId = g.Key!.Value,
                            CurrentBalance = g.Sum(i => i.Amount ?? 0),
                            NumberOfInvestors = g.Select(r => r.UserEmail).Distinct().Count()
                        })
                        .ToListAsync();

                foreach (var c in result)
                {
                    var groupedRecommendation = reccomendations.FirstOrDefault(i => i.CampaignId == c.Id);
                    if (groupedRecommendation != null)
                    {
                        c.CurrentBalance = groupedRecommendation.CurrentBalance + (c.AddedTotalAdminRaised ?? 0);
                        c.NumberOfInvestors = groupedRecommendation.NumberOfInvestors;
                    }
                }

                for (int i = 0; i < data.Count; i++)
                {
                    result[i].GroupForPrivateAccessDto = _mapper.Map<Group, GroupDto>(data[i].GroupForPrivateAccess!);
                }

                return result;
            }

            var folowedUserGroups = requestsByTheUser.Where(i => i.GroupToFollow != null).Select(i => i.GroupToFollow!.Id).ToList();
            var filteredGroups = await _context.Groups.Include(i => i.Campaigns).Where(i => folowedUserGroups.Contains(i.Id)).ToListAsync();
            var campaignsList = new List<Campaign>();
            foreach (var c in filteredGroups)
            {
                var camp = _mapper.Map<List<CampaignDto>, List<Campaign>>(c.Campaigns!);
                campaignsList.AddRange(camp);
            }

            return campaignsList;
        }

        [HttpGet("data")]
        public async Task<ActionResult<Data>> GetData()
        {
            var sdgs = await _context.SDGs.ToListAsync();
            var themes = await _context.Themes.ToListAsync();
            var investmentTypes = await _context.InvestmentTypes.ToListAsync();
            var approvedBy = await _context.ApprovedBy.ToListAsync();

            return new Data
            {
                Sdg = sdgs,
                Theme = themes,
                InvestmentType = investmentTypes,
                ApprovedBy = approvedBy
            };
        }

        [HttpGet("pdf/{identifier}")]
        public async Task<ActionResult<string>> GetPdf(string identifier)
        {
            if (_context.Campaigns == null)
            {
                return NotFound();
            }
            var campaign = new CampaignDto();

            campaign = await _context.Campaigns
                                        .Where(c => !string.IsNullOrWhiteSpace(c.Property) && c.Property == identifier)
                                        .FirstOrDefaultAsync();

            if (campaign == null)
            {
                campaign = await _context.Campaigns.FindAsync(Convert.ToInt32(identifier));
            }

            if (campaign == null)
            {
                return NotFound();
            }
            var campaignResponse = _mapper.Map<Campaign>(campaign);

            BlockBlobClient pdfBlockBlob = _blobContainerClient.GetBlockBlobClient(campaignResponse.PdfFileName);
            using (var memoryStream = new MemoryStream())
            {
                await pdfBlockBlob.DownloadToAsync(memoryStream);
                var bytes = memoryStream.ToArray();
                var b64String = Convert.ToBase64String(bytes);
                return "data:application/pdf;base64," + b64String;
            }
        }

        [HttpGet("{identifier}")]
        public async Task<ActionResult<Campaign>> GetCampaign(string identifier)
        {
            if (_context.Campaigns == null)
            {
                return NotFound();
            }

            var campaign = new CampaignDto();

            campaign = await _context.Campaigns
                                        .Where(c => !string.IsNullOrWhiteSpace(c.Property) && c.Property == identifier)
                                        .FirstOrDefaultAsync();

            if (campaign == null && int.TryParse(identifier, out int id))
            {
                campaign = await _context.Campaigns.FindAsync(id);
            }

            if (campaign == null || campaign.IsActive == false)
            {
                return NotFound();
            }
            else if (campaign.Stage == InvestmentStage.ClosedNotInvested)
            {
                return Ok(new { Success = false, Message = "This investment has been closed." });
            }

            var campaignResponse = _mapper.Map<Campaign>(campaign);

            var reccomendations = await _context.Recommendations
                                        .Where(x => x.Campaign != null &&
                                                x.Campaign.Id == campaignResponse.Id &&
                                                x.Amount > 0 &&
                                                x.UserEmail != null &&
                                                (x.Status!.ToLower() == "approved" || x.Status.ToLower() == "pending"))
                                        .GroupBy(x => x.Campaign!.Id)
                                        .Select(g => new
                                        {
                                            CurrentBalance = g.Sum(i => i.Amount ?? 0),
                                            NumberOfInvestors = g.Select(r => r.UserEmail).Distinct().Count()
                                        })
                                        .FirstOrDefaultAsync();

            if (reccomendations != null)
            {
                campaignResponse.CurrentBalance = reccomendations.CurrentBalance;
                campaignResponse.NumberOfInvestors = reccomendations.NumberOfInvestors;
            }

            campaignResponse.CurrentBalance = (campaignResponse.CurrentBalance ?? 0) + (campaignResponse.AddedTotalAdminRaised ?? 0);

            if (campaign.Stage == InvestmentStage.ClosedInvested)
            {
                List<int> themeIds = campaign?.Themes?
                                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(id => int.TryParse(id.Trim(), out var val) ? val : (int?)null)
                                                .Where(id => id.HasValue)
                                                .Select(id => id!.Value)
                                                .ToList() ?? new List<int>();

                var allCampaigns = await _context.Campaigns.ToListAsync();

                var matchedCampaigns = allCampaigns
                                            .Where(c =>
                                                c.IsActive == true &&
                                                c.Stage == InvestmentStage.Public &&
                                                c.Id != campaign!.Id &&
                                                themeIds.Any(id =>
                                                    c.Themes == id.ToString() ||
                                                    c.Themes!.StartsWith(id + ",") ||
                                                    c.Themes.EndsWith("," + id) ||
                                                    c.Themes.Contains("," + id + ",")
                                                ))
                                            .ToList();

                if (matchedCampaigns.Any())
                {
                    var matchedCampaignsCardDto = matchedCampaigns
                                                    .Select(c => new MatchedCampaignsCardDto
                                                    {
                                                        Id = c.Id,
                                                        Name = c.Name!,
                                                        Description = c.Description!,
                                                        Target = c.Target!,
                                                        TileImageFileName = c.TileImageFileName!,
                                                        ImageFileName = c.ImageFileName!,
                                                        Property = c.Property!,

                                                        CurrentBalance = _context.Recommendations
                                                                                    .Where(r => r.Campaign!.Id == c.Id &&
                                                                                                r.Amount > 0 &&
                                                                                                r.UserEmail != null &&
                                                                                                (r.Status!.ToLower() == "approved" || r.Status.ToLower() == "pending"))
                                                                                    .Sum(r => (decimal?)r.Amount) ?? 0,

                                                        NumberOfInvestors = _context.Recommendations
                                                                                        .Where(r => r.Campaign!.Id == c.Id &&
                                                                                                    r.Amount > 0 &&
                                                                                                    r.UserEmail != null &&
                                                                                                    (r.Status!.ToLower() == "approved" || r.Status.ToLower() == "pending"))
                                                                                        .Select(r => r.UserEmail!)
                                                                                        .Distinct()
                                                                                        .Count()
                                                    })
                                                    .OrderByDescending(c => c.CurrentBalance)
                                                    .Take(3)
                                                    .ToList();

                    campaignResponse.MatchedCampaigns = matchedCampaignsCardDto;
                }
            }
            return campaignResponse;
        }

        [HttpGet("admin/{id}")]
        [DisableRequestSizeLimit]
        public async Task<ActionResult<Campaign>> GetAdminCampaign(int id)
        {
            if (_context.Campaigns == null)
            {
                return NotFound();
            }
            var campaignDto = await _context.Campaigns.Include(x => x.GroupForPrivateAccess).FirstOrDefaultAsync(x => x.Id == id);

            if (campaignDto == null)
            {
                return NotFound();
            }

            Campaign campaign = _mapper.Map<Campaign>(campaignDto);

            campaign.GroupForPrivateAccessDto = campaignDto.GroupForPrivateAccess != null
                                                            ? _mapper.Map<GroupDto>(campaignDto.GroupForPrivateAccess)
                                                            : null;

            campaign.CurrentBalance = await _context.Recommendations
                                                    .Where(i => i.Campaign != null && i.Campaign.Id == campaign.Id)
                                                    .GroupBy(x => x.Campaign!.Id)
                                                    .Select(g => g.Sum(i => i.Status == "approved" || i.Status == "pending" ? i.Amount : 0))
                                                    .FirstOrDefaultAsync();

            return campaign;
        }

        [HttpPut("/status/{id}")]
        public async Task<ActionResult<CampaignDto>> UpdateCampaignStatus(int id, bool status)
        {
            var campaign = await _context.Campaigns.SingleOrDefaultAsync(item => item.Id == id);
            if (campaign == null)
            {
                return BadRequest();
            }

            campaign.IsActive = status;
            campaign.ModifiedDate = DateTime.Now;
            await _context.SaveChangesAsync();
            var data = _mapper.Map<CampaignDto>(campaign);

            if (_environment.EnvironmentName == "Production" && status)
            {
                var date = DateTime.Now.ToString("MM/dd/yyyy");
                var subject = "New Investment approved on Production";
                var body = $@"
                            <html>
                                <body>
                                    <p>Hello Team!</p>
                                    <p>A new Investment was approved on Production on {date}: {campaign.Name}</p>
                                    <p>Thanks.</p>
                                </body>
                            </html>
                            ";
            }

            return Ok(data);
        }

        [DisableRequestSizeLimit]
        [HttpPut("{id}")]
        public async Task<ActionResult<Campaign>> PutCampaign([FromBody] Campaign campaign)
        {
            if (campaign!.Id == null)
                return BadRequest();

            if (campaign.Property != null)
            {
                bool alreadyExistCampaign = await _context.Campaigns
                                                    .AnyAsync(x => x.Property != null && x.Id != campaign.Id
                                                        && x.Property.ToLower().Trim() == campaign.Property.ToLower().Trim());

                if (alreadyExistCampaign)
                {
                    return Ok(new { Success = false, Message = "Investment name for URL already exists." });
                }
            }

            if (!string.IsNullOrWhiteSpace(campaign.ContactInfoEmailAddress))
            {
                var alreadyExistCampaign = await _context.Campaigns
                                                        .AnyAsync(x =>
                                                            !string.IsNullOrWhiteSpace(x.ContactInfoEmailAddress)
                                                            && x.Id != campaign.Id
                                                            && x.ContactInfoEmailAddress!.ToLower().Trim() == campaign.ContactInfoEmailAddress.ToLower().Trim());

                if (alreadyExistCampaign)
                {
                    return Ok(new { Success = false, Message = "This organizational email is already used for another investment." });
                }
            }

            if (!string.IsNullOrWhiteSpace(campaign.PDFPresentation))
            {
                string pdfFileName = Guid.NewGuid().ToString() + ".pdf";
                var pdfBlob = _blobContainerClient.GetBlockBlobClient(pdfFileName);
                var pdfStr = campaign.PDFPresentation.Substring(campaign.PDFPresentation.IndexOf(',') + 1);
                var pdfBytes = Convert.FromBase64String(pdfStr);
                using (var stream = new MemoryStream(pdfBytes))
                {
                    await pdfBlob.UploadAsync(stream);
                }
                //var pdfOldBlob = _blobContainerClient.GetBlockBlobClient(campaign.PdfFileName);
                //await pdfOldBlob.DeleteIfExistsAsync();

                campaign.PdfFileName = pdfFileName;
            }

            if (!string.IsNullOrWhiteSpace(campaign.Image))
            {
                string imageFileName = Guid.NewGuid().ToString() + ".jpg";
                var imageBlob = _blobContainerClient.GetBlockBlobClient(imageFileName);
                var imagestr = campaign.Image.Substring(campaign.Image.IndexOf(',') + 1);
                var imageBytes = Convert.FromBase64String(imagestr);
                using (var stream = new MemoryStream(imageBytes))
                {
                    await imageBlob.UploadAsync(stream);
                }
                //var imageOldBlob = _blobContainerClient.GetBlockBlobClient(campaign.ImageFileName);
                //await imageOldBlob.DeleteIfExistsAsync();

                campaign.ImageFileName = imageFileName;
            }

            if (!string.IsNullOrWhiteSpace(campaign.TileImage))
            {
                string tileImageFileName = Guid.NewGuid().ToString() + ".jpg";
                var tileImageBlob = _blobContainerClient.GetBlockBlobClient(tileImageFileName);
                var tileImagestr = campaign.TileImage.Substring(campaign.TileImage.IndexOf(',') + 1);
                var tileImageBytes = Convert.FromBase64String(tileImagestr);
                using (var stream = new MemoryStream(tileImageBytes))
                {
                    await tileImageBlob.UploadAsync(stream);
                }
                //var tIilemageOldBlob = _blobContainerClient.GetBlockBlobClient(campaign.TileImageFileName);
                //await tIilemageOldBlob.DeleteIfExistsAsync();

                campaign.TileImageFileName = tileImageFileName;
            }

            if (!string.IsNullOrWhiteSpace(campaign.Logo))
            {
                string logoFileName = Guid.NewGuid().ToString() + ".jpg";
                var logoBlob = _blobContainerClient.GetBlockBlobClient(logoFileName);
                var logostr = campaign.Logo.Substring(campaign.Logo.IndexOf(',') + 1);
                var logoBytes = Convert.FromBase64String(logostr);
                using (var stream = new MemoryStream(logoBytes))
                {
                    await logoBlob.UploadAsync(stream);
                }
                //var logoOldBlob = _blobContainerClient.GetBlockBlobClient(campaign.LogoFileName);
                //await logoOldBlob.DeleteIfExistsAsync();

                campaign.LogoFileName = logoFileName;
            }

            var existingCampaign = await _context.Campaigns.FindAsync(campaign.Id);

            campaign.CreatedDate = existingCampaign?.CreatedDate;
            campaign.ModifiedDate = DateTime.Now;

            if (_environment.EnvironmentName == "Production")
            {
                if (existingCampaign?.Stage == InvestmentStage.Public && campaign.Stage == InvestmentStage.Private)
                {
                    var date = DateTime.Now.ToString("MM/dd/yyyy");
                    var subject = "New Investment approved on Production";
                    var body = $@"
                                <html>
                                    <body>
                                        <p>Hello Team!</p>
                                        <p>A new Investment was approved on Production on {date}: {campaign.Name}</p>
                                        <p>Thanks.</p>
                                    </body>
                                </html>
                                ";

                    //_ = Task.Run(async () =>
                    //{
                    //    await Task.WhenAll(_mailService.SendMailAsync("", subject, "", body));
                    //});
                }

                if (campaign.Stage == InvestmentStage.ComplianceReview)
                {
                    var subject = "An investment is ready for compliance review";
                    var body = $@"
                                <html>
                                    <body>
                                        <p>Hello Tim!</p>
                                        <p>A new Investment is ready for compliance review: <b>{campaign.Name}</b></p>
                                        <p>Thanks.</p>
                                    </body>
                                </html>
                                ";

                    //_ = Task.Run(async () =>
                    //{
                    //    await Task.WhenAll(_mailService.SendMailAsync("", subject, "", body));
                    //});
                }
            }

            var identity = HttpContext.User.Identity as ClaimsIdentity;
            var role = identity?.Claims.FirstOrDefault(i => i.Type == ClaimTypes.Role)?.Value;

            if (role != "Admin")
            {
                campaign!.MinimumInvestment = existingCampaign!.MinimumInvestment;
                campaign!.ApprovedBy = existingCampaign!.ApprovedBy;
                campaign!.Stage = existingCampaign!.Stage;
                campaign!.GroupForPrivateAccessDto = _mapper.Map<GroupDto>(existingCampaign.GroupForPrivateAccess);
                campaign!.Property = existingCampaign!.Property;
                campaign!.AddedTotalAdminRaised = existingCampaign!.AddedTotalAdminRaised;
                campaign!.IsActive = existingCampaign!.IsActive;
            }

            var campaignDto = _mapper.Map(campaign, existingCampaign);

            if (role == "Admin")
            {
                if (campaign.GroupForPrivateAccessDto != null)
                {
                    campaignDto!.GroupForPrivateAccess = _mapper.Map<Group>(campaign.GroupForPrivateAccessDto);
                    campaignDto!.GroupForPrivateAccessId = campaign.GroupForPrivateAccessDto.Id;
                }
                else
                {
                    campaignDto!.GroupForPrivateAccess = null;
                    campaignDto!.GroupForPrivateAccessId = null;
                }
            }

            _context.Entry(existingCampaign!).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CampaignExists(campaign.Id.Value))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Ok();
        }

        [HttpPost("raisemoney")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> PostRaiseMoneyCampaign([FromBody] Campaign campaign)
        {
            try
            {
                if (!string.IsNullOrEmpty(campaign.CaptchaToken))
                {
                    if (!await VerifyCaptcha(campaign.CaptchaToken))
                        return BadRequest("CAPTCHA verification failed.");
                }

                if (campaign == null)
                {
                    return Ok(new { Success = false, Message = "Campaign data is required." });
                }
                if (campaign.ContactInfoEmailAddress == null)
                {
                    return Ok(new { Success = false, Message = "Email is required." });
                }

                var userEmail = campaign!.ContactInfoEmailAddress!.Trim().ToLower();

                var existingUser = await _context.Users.SingleOrDefaultAsync(u => u.Email.ToLower() == userEmail);

                User? user = existingUser;

                if (user == null)
                {
                    if (string.IsNullOrWhiteSpace(campaign.FirstName))
                    {
                        return Ok(new { Success = false, Message = "First Name is required." });
                    }
                    if (string.IsNullOrWhiteSpace(campaign.LastName))
                    {
                        return Ok(new { Success = false, Message = "Last Name is required." });
                    }

                    user = await RegisterAnonymousUser(campaign.FirstName!, campaign.LastName!, campaign.ContactInfoEmailAddress.Trim().ToLower()!);
                }
                else
                {
                    var existingContactInfoEmailAddress = await _context.Campaigns
                                                                        .AnyAsync(x =>
                                                                            !string.IsNullOrWhiteSpace(x.ContactInfoEmailAddress)
                                                                            && x.ContactInfoEmailAddress!.ToLower().Trim() == userEmail);

                    if (existingContactInfoEmailAddress)
                        return Ok(new { Success = false, Message = "This organizational email is already used for another investment." });
                }

                string pdfFileName = Guid.NewGuid().ToString() + ".pdf";
                var pdfBlob = _blobContainerClient.GetBlockBlobClient(pdfFileName);
                var pdfStr = campaign?.PDFPresentation?.Substring(campaign.PDFPresentation.IndexOf(',') + 1);
                var pdfBytes = Convert.FromBase64String(pdfStr!);
                using (var stream = new MemoryStream(pdfBytes))
                    await pdfBlob.UploadAsync(stream);


                string imageFileName = Guid.NewGuid().ToString() + ".jpg";
                var imageBlob = _blobContainerClient.GetBlockBlobClient(imageFileName);
                var imagestr = campaign?.Image?.Substring(campaign.Image.IndexOf(',') + 1);
                var imageBytes = Convert.FromBase64String(imagestr!);
                using (var stream = new MemoryStream(imageBytes))
                    await imageBlob.UploadAsync(stream);


                string tileImageFileName = Guid.NewGuid().ToString() + ".jpg";
                var tileImageBlob = _blobContainerClient.GetBlockBlobClient(tileImageFileName);
                var tileImagestr = campaign?.TileImage?.Substring(campaign.TileImage.IndexOf(',') + 1);
                var tileImageBytes = Convert.FromBase64String(tileImagestr!);
                using (var stream = new MemoryStream(tileImageBytes))
                    await tileImageBlob.UploadAsync(stream);


                string logoFileName = Guid.NewGuid().ToString() + ".jpg";
                var logoBlob = _blobContainerClient.GetBlockBlobClient(logoFileName);
                var logostr = campaign?.Logo?.Substring(campaign.Logo.IndexOf(',') + 1);
                var logoBytes = Convert.FromBase64String(logostr!);
                using (var stream = new MemoryStream(logoBytes))
                    await logoBlob.UploadAsync(stream);


                var mappedCampaign = _mapper.Map<Campaign, CampaignDto>(campaign!);
                mappedCampaign.Status = "0";
                mappedCampaign.Stage = InvestmentStage.New;
                mappedCampaign.IsActive = false;
                mappedCampaign.TileImageFileName = tileImageFileName;
                mappedCampaign.ImageFileName = imageFileName;
                mappedCampaign.LogoFileName = logoFileName;
                mappedCampaign.PdfFileName = pdfFileName;
                mappedCampaign.CreatedDate = DateTime.Now;
                mappedCampaign.EmailSends = false;
                mappedCampaign.UserId = user != null ? user.Id : null;

                mappedCampaign.GroupForPrivateAccess = campaign?.GroupForPrivateAccessDto != null ? await _context.Groups.FirstOrDefaultAsync(i => i.Id == campaign.GroupForPrivateAccessDto.Id) : null;

                _context.Campaigns.Add(mappedCampaign);
                await _context.SaveChangesAsync();

                if (_environment.EnvironmentName == "Production")
                {
                    var parsIdSdgs = campaign?.SDGs?.Split(',').Select(id => int.Parse(id)).ToList();
                    var parsIdInvestmentTypes = campaign?.InvestmentTypes?.Split(',').Select(id => int.Parse(id)).ToList();
                    var parsIdThemes = campaign?.Themes?.Split(',').Select(id => int.Parse(id)).ToList();

                    var sdgNames = _context.SDGs.Where(c => parsIdSdgs!.Contains(c.Id)).Select(c => c.Name).ToList();
                    var themeNames = _context.Themes.Where(c => parsIdThemes!.Contains(c.Id)).Select(c => c.Name).ToList();
                    var investmentTypeNames = _context.InvestmentTypes.Where(c => parsIdInvestmentTypes!.Contains(c.Id)).Select(c => c.Name).ToList();

                    var sdgNamesString = string.Join(", ", sdgNames);
                    var themeNamesString = string.Join(", ", themeNames);
                    var investmentTypeNamesString = string.Join(", ", investmentTypeNames);

                    var emailParts = new List<string>
                    {
                        $"<p>User Name: {campaign!.FirstName} {campaign.LastName}</p><br/>",
                        $"<p>Investment Owner Email: {campaign.ContactInfoEmailAddress}</p><br/>",
                        $"<p>Investment Informational Email: {campaign.InvestmentInformationalEmail}</p><br/>",
                        $"<p>Mobile Number: {campaign.ContactInfoPhoneNumber}</p><br/>",
                        $"<p>Address Line 1: {campaign.ContactInfoAddress}</p><br/>"
                    };

                    if (!string.IsNullOrWhiteSpace(campaign.ContactInfoAddress2))
                        emailParts.Add($"<p>Address Line 2: {campaign.ContactInfoAddress2}</p><br/>");

                    if (!string.IsNullOrWhiteSpace(campaign.City))
                        emailParts.Add($"<p>City: {campaign.City}</p><br/>");

                    if (!string.IsNullOrWhiteSpace(campaign.State))
                        emailParts.Add($"<p>State: {campaign.State}</p><br/>");

                    if (!string.IsNullOrWhiteSpace(campaign.ZipCode))
                        emailParts.Add($"<p>Zip Code: {campaign.ZipCode}</p><br/>");

                    emailParts.AddRange(new[]
                    {
                        $"<p>Investment Name: {campaign?.Name}</p><br/>",
                        $"<p>About the Investment: {campaign?.Description}</p><br/>",
                        $"<p>Investment website URL: {campaign?.Website}</p><br/>",
                        $"<p>Type of Investment: {investmentTypeNamesString}</p><br/>",
                        $"<p>Investment Terms: {campaign?.Terms}</p><br/>",
                        $"<p>Fundraising Goal: {campaign?.Target}</p><br/>",
                        $"<p>Expected Fundraising Close Date or Evergreen: {campaign?.FundraisingCloseDate}</p><br/>",
                        $"<p>Investment Themes Covered: {themeNamesString}</p><br/>",
                        $"<p>SDGs impacted by investment: {sdgNamesString}</p><br/>",
                        $"<p>Have you received funding from Impact Assets before?: {campaign?.ImpactAssetsFundingStatus}</p><br/>",
                        $"<p>Your role with the investment: {campaign?.InvestmentRole}</p><br/>"
                    });

                    var emailBody = $"<html><body>{string.Join("", emailParts)}</body></html>";

                    var date = DateTime.Now.Date.ToString("M/d/yyyy");
                    var subject = "New Investment live on Production";
                    var body = $@"
                                <html>
                                    <body>
                                        <p>Hello Team!</p>
                                        <br/>
                                        <p>A new Investment was posted to Production on {date}: <strong>{mappedCampaign.Name}</strong>.</p>
                                        <br/>
                                        <p>Thanks.</p>
                                    </body>
                                </html>";

                    _ = Task.Run(async () =>
                    {
                        await Task.WhenAll(
                            _mailService.SendMailAsync("investment.campaign@mailinator.com", "New request to raise money", "", emailBody),
                            _mailService.SendMailAsync("investment.campaign@mailinator.com", subject, "", body)
                        );
                    });
                }

                return Ok(new { Success = true, Message = "Investment has been created successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        private async Task<bool> VerifyCaptcha(string token)
        {
            string captchaSecretKey = _keyVaultConfigService.GetCaptchaSecretKey();

            var requestContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("secret", captchaSecretKey),
                new KeyValuePair<string, string>("response", token)
            });

            var response = await _httpClient.PostAsync("https://hcaptcha.com/siteverify", requestContent);

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            bool isSuccess = doc.RootElement.GetProperty("success").GetBoolean();

            return isSuccess;
        }

        private async Task<User> RegisterAnonymousUser(string firstName, string lastName, string email)
        {
            var userName = $"{firstName}{lastName}".Trim().ToLower();
            Random random = new Random();
            while (_context.Users.Any(x => x.UserName == userName))
            {
                userName = $"{userName}{random.Next(0, 100)}".ToLower();
            }

            UserRegistrationDto registrationDto = new UserRegistrationDto()
            {
                FirstName = firstName,
                LastName = lastName,
                UserName = userName,
                Password = defaultPassword,
                Email = email
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

            var requestOrigin = HttpContext?.Request.Headers["Origin"].ToString();

            _ = SendWelcomeEmail(requestOrigin!, email, userName, firstName!);

            return user;
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

        private async Task SendWelcomeEmail(string request, string emailTo, string userName, string firstName)
        {
            string logoUrl = $"{request}/logo-for-email.png";
            string logoHtml = $@"
                                <div style='text-align: center;'>
                                    <a href='https://investment-campaign.org' target='_blank'>
                                        <img src='{logoUrl}' alt='Logo' width='300' height='150' />
                                    </a>
                                </div>";

            string resetPasswordUrl = $"{request}/forgotpassword";
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
                                            <p>Have questions? Email Shabbir at <a href='mailto:investment.campaign@mailinator.com'>investment.campaign@mailinator.com</a></p>
                                            <p><a href='{request}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                        </body>
                                    </html>";

            await _mailService.SendMailAsync(emailTo, subject, "", body);
        }

        [HttpGet("send-investment-QR-code-email")]
        public async Task<IActionResult> SendInvestmentQACodeEmail(int id)
        {
            try
            {
                var requestOrigin = HttpContext?.Request.Headers["Origin"].ToString();
                string subject = "🚀 Share Your Investment with the World – Your QR Code is Ready!";

                var investment = await _context.Campaigns.FindAsync(id);

                string investmentUrl = $"{requestOrigin}/invest/{Uri.EscapeDataString(investment!.Property!.Trim())}";
                string fullName = investment.ContactInfoFullName ?? string.Empty;
                string[] parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string firstName = parts.Length > 0 ? parts[0] : string.Empty;

                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(investmentUrl, QRCodeGenerator.ECCLevel.Q);
                var qrCode = new PngByteQRCode(qrCodeData);
                byte[] qrBytes = qrCode.GetGraphic(20);

                var body = $@"
                                <html>
                                    <body>
                                        <p>Hi {firstName},</p>
                                        <p>Exciting news – your investment <b>{investment.Name}</b> is now live on Investment Campaign! 🎉</p>
                                        <p>To help you spread the word, we've generated a <b>QR code just for you </b>– it’s attached to this email and ready to go.</p>
                                        <p>📲 Share it on socials, in emails, or at events – wherever you want to grow your support!</p>
                                        <p>We’re cheering you on and can’t wait to see your fundraising take flight.</p>
                                        <p>As always, the Investment Campaign team is here if you need anything.</p>
                                        <p>Onwards and upwards,</p>
                                        <p style='margin-bottom: 0px;'><b>The Investment Campaign Team</b></p>
                                        <p style='margin-top: 0px; margin-bottom: 0px;'>🔗 <a href='https://www.linkedin.com/company/investment-campaign-us/'>LinkedIn</a></p>
                                        <p style='margin-top: 0px; margin-bottom: 0px;'>🌐 <a href='https://investment-campaign.org/'>investment-campaign.org</a></p>
                                        <p style='margin-top: 0px;'>✉️ <a href='mailto:investment.campaign@mailinator.com'>investment.campaign@mailinator.com</a></p>
                                        <p>—</p>
                                        <p>Questions, feedback, or just want to say hi? Reach out to us anytime.</p>
                                        <p>Don’t want to get these emails? <a href='{requestOrigin}/settings' target='_blank'>Unsubscribe</a>.</p>
                                    </body>
                                </html>";

                var qrAttachment = new EmailAttachment(
                    name: $"{investment.Name}.png",
                    contentType: "image/png",
                    content: BinaryData.FromBytes(qrBytes)
                );

                await _mailService.SendMailAsync(investment!.ContactInfoEmailAddress!.Trim().ToLower(), subject, "", body, new List<EmailAttachment> { qrAttachment });

                return Ok(new { Success = true, Message = "Email sent successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCampaign(int id)
        {
            if (_context.Campaigns == null)
            {
                return NotFound();
            }

            var recommendationsToRemove = await _context.Recommendations.Where(i => i.Campaign!.Id == id).ToListAsync();

            foreach (var r in recommendationsToRemove)
            {
                _context.Recommendations.Remove(r);
            }

            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null)
            {
                return NotFound();
            }

            _context.Campaigns.Remove(campaign);
            await _context.SaveChangesAsync();

            //BlockBlobClient tileImageBlockBlob = _blobContainerClient.GetBlockBlobClient(campaign.TileImageFileName);
            //BlockBlobClient imageBlockBlob = _blobContainerClient.GetBlockBlobClient(campaign.ImageFileName);
            //BlockBlobClient logoBlockBlob = _blobContainerClient.GetBlockBlobClient(campaign.LogoFileName);
            //BlockBlobClient pdfBlockBlob = _blobContainerClient.GetBlockBlobClient(campaign.PdfFileName);

            //await tileImageBlockBlob.DeleteIfExistsAsync();
            //await imageBlockBlob.DeleteIfExistsAsync();
            //await logoBlockBlob.DeleteIfExistsAsync();
            //await pdfBlockBlob.DeleteIfExistsAsync();

            return NoContent();
        }

        [HttpGet("portfolio")]
        public async Task<ActionResult<Portfolio>> GetPortfolio()
        {
            var identity = HttpContext.User.Identity as ClaimsIdentity;

            if (_context.Users == null || identity == null)
            {
                return NotFound();
            }

            var email = identity.Claims.FirstOrDefault(i => i.Type == ClaimTypes.Email)?.Value;

            if (email == null || email == string.Empty)
            {
                return NotFound();
            }

            var portfolio = new Portfolio();
            var user = await _context.Users.FirstOrDefaultAsync(i => i.Email == email);
            portfolio.AccountBalance = user?.AccountBalance;

            var groupAccountBalance = await _context.GroupAccountBalance.FirstOrDefaultAsync(g => g.User.Id == user!.Id);
            if (groupAccountBalance != null)
            {
                portfolio.GroupBalance = groupAccountBalance.Balance;
            }

            var userRecommendations = await _context.Recommendations
                .Where(i => i.UserEmail == email && (i.Status == "approved" || i.Status == "pending"))
                .Include(item => item.Campaign)
                .ToListAsync();
            List<RecommendationsDto> dataRecommendation = new List<RecommendationsDto>();
            if (userRecommendations.Count > 0)
            {
                for (int i = 0; i < userRecommendations.Count; i++)
                {
                    RecommendationsDto recommendationsDto = new RecommendationsDto();
                    recommendationsDto.Id = userRecommendations[i].Id;
                    recommendationsDto.UserEmail = userRecommendations[i].UserEmail;
                    recommendationsDto.CampaignId = userRecommendations[i].Campaign?.Id;
                    recommendationsDto.Amount = userRecommendations[i].Amount;
                    recommendationsDto.Status = userRecommendations[i].Status;
                    recommendationsDto.DateCreated = userRecommendations[i].DateCreated;
                    dataRecommendation.Add(recommendationsDto);
                }
            }

            if (dataRecommendation != null)
            {
                var campaignIds = dataRecommendation
                .Select(i => i.CampaignId);
                var data = await _context.Campaigns
                    .Where(i => campaignIds.Contains(i.Id))
                    .ToListAsync();
                var userCampaigns = _mapper.Map<List<CampaignDto>, List<Campaign>>(data);

                var userRecommendationBalances = await _context.Recommendations
                                                        .Where(x => x.Campaign != null &&
                                                                campaignIds.Contains(x.Campaign.Id) &&
                                                                x.Amount > 0 &&
                                                                x.UserEmail != null &&
                                                                (x.Status!.ToLower() == "approved" || x.Status.ToLower() == "pending"))
                                                        .GroupBy(x => x.Campaign!.Id)
                                                        .Select(g => new
                                                        {
                                                            CampaignId = g.Key!.Value,
                                                            CurrentBalance = g.Sum(i => i.Amount ?? 0),
                                                            NumberOfInvestors = g.Select(r => r.UserEmail).Distinct().Count()
                                                        })
                                                        .ToListAsync();

                foreach (var c in userCampaigns)
                {
                    var item = userRecommendationBalances.FirstOrDefault(i => i.CampaignId == c.Id);
                    if (item != null)
                    {
                        c.CurrentBalance = item.CurrentBalance + (c.AddedTotalAdminRaised ?? 0);
                        c.NumberOfInvestors = item.NumberOfInvestors;
                    }
                }

                portfolio.Recommendations = dataRecommendation;
                portfolio.Campaigns = userCampaigns;
            }


            return portfolio;
        }

        private async Task<IEnumerable<CampaignCardDto>> GetCampaignsCardDto(string? sourcedBy = null)
        {
            if (_context.Campaigns == null)
            {
                return null!;
            }

            var sourcedByNamesList = sourcedBy?.ToLower().Split(',').Select(n => n.Trim()).ToList();

            var approvedBy = sourcedByNamesList == null || !sourcedByNamesList.Any()
                            ? new List<int>()
                            : await _context.ApprovedBy
                                            .Where(x => sourcedByNamesList.Contains(x.Name!.ToLower()))
                                            .Select(x => x.Id)
                                            .ToListAsync();

            var campaigns = await _context.Campaigns
                                            .Where(i => i.IsActive!.Value && i.Stage == InvestmentStage.Public)
                                            .Include(i => i.GroupForPrivateAccess)
                                            .ToListAsync();

            if (approvedBy.Any())
            {
                campaigns = campaigns
                                .Where(c => !string.IsNullOrWhiteSpace(c.ApprovedBy) &&
                                            approvedBy.Any(id => c.ApprovedBy
                                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(s => s.Trim())
                                                .Where(s => int.TryParse(s, out _))
                                                .Select(int.Parse)
                                                .Contains(id)))
                                .ToList();
            }

            var data = campaigns
                              .Select(c => new
                              {
                                  Campaign = c,
                                  Recommendations = _context.Recommendations
                                                            .Where(r => r.Campaign != null &&
                                                                    r.Campaign.Id == c.Id &&
                                                                    (r.Status!.ToLower() == "approved" || r.Status.ToLower() == "pending") &&
                                                                    r.Amount > 0 &&
                                                                    r.UserEmail != null)
                                                            .GroupBy(r => r.Campaign!.Id)
                                                            .Select(g => new
                                                            {
                                                                CurrentBalance = g.Sum(r => r.Amount ?? 0),
                                                                NumberOfInvestors = g.Select(r => r.UserEmail!.ToLower().Trim()).Distinct().Count()
                                                            })
                                                            .FirstOrDefault()
                              })
                              .ToList();

            var result = data.Select(item =>
            {
                var campaignDto = _mapper.Map<CampaignCardDto>(item.Campaign);
                if (item.Recommendations != null)
                {
                    campaignDto.CurrentBalance = item.Recommendations.CurrentBalance + (item.Campaign.AddedTotalAdminRaised ?? 0);
                    campaignDto.NumberOfInvestors = item.Recommendations.NumberOfInvestors;
                }
                campaignDto.GroupForPrivateAccessDto = _mapper.Map<GroupDto>(item.Campaign.GroupForPrivateAccess);
                return campaignDto;
            }).ToList();

            return result;
        }

        private bool CampaignExists(int id)
        {
            return (_context.Campaigns?.Any(e => e.Id == id)).GetValueOrDefault();
        }

        [HttpGet("Export")]
        public async Task<IActionResult> ExportCampaigns()
        {
            var campaigns = await _context.Campaigns
                                            .Include(c => c.Groups)
                                            .Include(c => c.Recommendations)
                                            .ToListAsync();

            var campaignDtos = campaigns.Select(c => new CampaignDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                Themes = c.Themes,
                ApprovedBy = c.ApprovedBy,
                SDGs = c.SDGs,
                InvestmentTypes = c.InvestmentTypes,
                Terms = c.Terms,
                MinimumInvestment = c.MinimumInvestment?.ToString(),
                Website = c.Website,
                ContactInfoFullName = c.ContactInfoFullName,
                ContactInfoAddress = c.ContactInfoAddress,
                ContactInfoAddress2 = c.ContactInfoAddress2,
                ContactInfoEmailAddress = c.ContactInfoEmailAddress,
                InvestmentInformationalEmail = c.InvestmentInformationalEmail,
                ContactInfoPhoneNumber = c.ContactInfoPhoneNumber,
                City = c.City,
                State = c.State,
                ZipCode = c.ZipCode,
                NetworkDescription = c.NetworkDescription,
                ImpactAssetsFundingStatus = c.ImpactAssetsFundingStatus,
                InvestmentRole = c.InvestmentRole,
                Referred = c.Referred,
                Target = c.Target,
                Status = c.Status,
                TileImageFileName = c.TileImageFileName,
                ImageFileName = c.ImageFileName,
                PdfFileName = c.PdfFileName,
                OriginalPdfFileName = c.OriginalPdfFileName,
                LogoFileName = c.LogoFileName,
                IsActive = c.IsActive,
                Stage = c.Stage,
                Property = c.Property,
                AddedTotalAdminRaised = c.AddedTotalAdminRaised,
                Groups = c.Groups.ToList(),
                Recommendations = c.Recommendations,
                GroupForPrivateAccess = c.GroupForPrivateAccess,
                EmailSends = c.EmailSends,
                FundraisingCloseDate = c.FundraisingCloseDate,
                MissionAndVision = c.MissionAndVision,
                PersonalizedThankYou = c.PersonalizedThankYou,
                //HasExistingInvestors = c.HasExistingInvestors,
                ExpectedTotal = c.ExpectedTotal,
                CreatedDate = c.CreatedDate
            }).ToList();

            string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            string fileName = "Investments.xlsx";

            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet worksheet = workbook.Worksheets.Add("Campaigns");

                string[] headers = new string[]
                {
                    "Id", "Name", "Description", "Themes", "Approved By", "SDGs", "Investment Types",
                    "Terms", "Minimum Investment", "Website", "Contact Info FullName", "Contact Info Address1", "Contact Info Address2",
                    "Contact Info Email Address", "Investment Informational Email", "Contact Info Phone Number", "City", "State", "ZipCode", "Tell us a bit about your network", "ImpactAssetsFundingStatus",
                    "InvestmentRole", "How where you referred to Investment Campaign?", "Target", "Status", "Tile Image File Name", "Image File Name", "Pdf File Name", "Original Pdf File Name",
                    "Logo File Name", "Is Active", "Stage", "Property", "Added Total Admin Raised",
                    "Groups", "Total Recommendations","Total Investors", "Group For Private Access", "Email Sends", "Expected Fundraising Close Date",
                    "Mission/Vision", "Personalized Thank You", "How much money do you already have in commitments for your investment", "Created Date"
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                    worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                }

                for (int index = 0; index < campaignDtos.Count; index++)
                {
                    var dto = campaignDtos[index];
                    int row = index + 2;
                    int col = 1;

                    worksheet.Cell(row, col++).Value = dto.Id;
                    worksheet.Cell(row, col++).Value = dto.Name;
                    worksheet.Cell(row, col++).Value = dto.Description;
                    worksheet.Cell(row, col++).Value = dto.Themes;
                    worksheet.Cell(row, col++).Value = dto.ApprovedBy;
                    worksheet.Cell(row, col++).Value = dto.SDGs;
                    worksheet.Cell(row, col++).Value = dto.InvestmentTypes;
                    worksheet.Cell(row, col++).Value = dto.Terms;
                    worksheet.Cell(row, col++).Value = dto.MinimumInvestment;
                    worksheet.Cell(row, col++).Value = dto.Website;
                    worksheet.Cell(row, col++).Value = dto.ContactInfoFullName;
                    worksheet.Cell(row, col++).Value = dto.ContactInfoAddress;
                    worksheet.Cell(row, col++).Value = dto.ContactInfoAddress2;
                    worksheet.Cell(row, col++).Value = dto.ContactInfoEmailAddress;
                    worksheet.Cell(row, col++).Value = dto.InvestmentInformationalEmail;
                    worksheet.Cell(row, col++).Value = dto.ContactInfoPhoneNumber;
                    worksheet.Cell(row, col++).Value = dto.City;
                    worksheet.Cell(row, col++).Value = dto.State;
                    worksheet.Cell(row, col++).Value = dto.ZipCode;
                    worksheet.Cell(row, col++).Value = dto.NetworkDescription;
                    worksheet.Cell(row, col++).Value = dto.ImpactAssetsFundingStatus;
                    worksheet.Cell(row, col++).Value = dto.InvestmentRole;
                    worksheet.Cell(row, col++).Value = dto.Referred;
                    worksheet.Cell(row, col++).Value = dto.Target;
                    worksheet.Cell(row, col++).Value = dto.Status;
                    worksheet.Cell(row, col++).Value = dto.TileImageFileName;
                    worksheet.Cell(row, col++).Value = dto.ImageFileName;
                    worksheet.Cell(row, col++).Value = dto.PdfFileName;
                    worksheet.Cell(row, col++).Value = dto.OriginalPdfFileName;
                    worksheet.Cell(row, col++).Value = dto.LogoFileName;
                    worksheet.Cell(row, col++).Value = dto.IsActive.HasValue && dto.IsActive.Value ? "Active" : "Inactive";

                    var description = (dto.Stage?.GetType()
                                         .GetField(dto.Stage?.ToString()!)
                                         ?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                                         ?.FirstOrDefault() as DescriptionAttribute)?.Description
                                         ?? dto.Stage.ToString();

                    worksheet.Cell(row, col++).Value = description;

                    worksheet.Cell(row, col++).Value = dto.Property;

                    var adminRaised = dto.AddedTotalAdminRaised ?? 0;
                    var adminRaisedCell = worksheet.Cell(row, col++);
                    adminRaisedCell.Value = adminRaised;
                    adminRaisedCell.Style.NumberFormat.Format = "$#,##0.00";

                    worksheet.Cell(row, col++).Value = string.Join(",", dto.Groups.Select(g => g.Name));

                    var recommendations = dto.Recommendations?
                                                    .Where(r => r != null &&
                                                            (r.Status?.Equals("approved", StringComparison.OrdinalIgnoreCase) == true ||
                                                            r.Status?.Equals("pending", StringComparison.OrdinalIgnoreCase) == true) &&
                                                            r.Campaign?.Id == dto.Id &&
                                                            r.Amount > 0 &&
                                                            !string.IsNullOrWhiteSpace(r.UserEmail))
                                                    .ToList();

                    var totalRecommendedAmount = recommendations?.Sum(r => r.Amount ?? 0) ?? 0;
                    var totalRecommendedAmountCell = worksheet.Cell(row, col++);
                    totalRecommendedAmountCell.Value = totalRecommendedAmount;
                    totalRecommendedAmountCell.Style.NumberFormat.Format = "$#,##0.00";

                    var totalInvestors = recommendations?.Select(r => r.UserEmail).Distinct().Count() ?? 0;
                    worksheet.Cell(row, col++).Value = totalInvestors;

                    worksheet.Cell(row, col++).Value = dto.GroupForPrivateAccess?.Name;
                    worksheet.Cell(row, col++).Value = dto.EmailSends.HasValue && dto.EmailSends.Value ? "Yes" : "No";
                    worksheet.Cell(row, col++).Value = dto.FundraisingCloseDate != null ? dto.FundraisingCloseDate : null;
                    worksheet.Cell(row, col++).Value = dto.MissionAndVision;
                    worksheet.Cell(row, col++).Value = dto.PersonalizedThankYou;
                    //worksheet.Cell(row, 34).Value = dto.HasExistingInvestors.HasValue && dto.HasExistingInvestors.Value ? "Yes" : "No";

                    var expectedTotalCell = worksheet.Cell(row, col++);
                    expectedTotalCell.Value = dto.ExpectedTotal;
                    expectedTotalCell.Style.NumberFormat.Format = "$#,##0.00";

                    worksheet.Cell(row, col++).Value = dto.CreatedDate?.ToString("MM-dd-yyyy");
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, contentType, fileName);
                }
            }
        }

        [HttpGet("get-investment-document-url")]
        public IActionResult GetInvestmentDocumentUrl(string pdfFileName, string action, string? originalPdfFileName = null)
        {
            CommonResponse response = new();
            try
            {
                if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(pdfFileName))
                {
                    response.Success = false;
                    response.Message = "Parameters required.";
                    return Ok(response);
                }

                BlockBlobClient blobClient = _blobContainerClient.GetBlockBlobClient(pdfFileName);
                var expiryTime = DateTimeOffset.UtcNow.AddMinutes(5);
                string? sasUri = null;

                switch (action)
                {
                    case "open":
                        sasUri = blobClient.GenerateSasUri(Azure.Storage.Sas.BlobSasPermissions.Read, expiryTime).ToString();
                        break;

                    case "download":
                        var sasBuilder = new BlobSasBuilder
                        {
                            BlobContainerName = blobClient.BlobContainerName,
                            BlobName = blobClient.Name,
                            Resource = "b",
                            ExpiresOn = expiryTime
                        };

                        sasBuilder.SetPermissions(BlobSasPermissions.Read);

                        string downloadFileName = !string.IsNullOrEmpty(originalPdfFileName) ? Uri.UnescapeDataString(originalPdfFileName) : pdfFileName;

                        sasBuilder.ContentDisposition = $"attachment; filename=\"{downloadFileName}\"";

                        var sasToken = sasBuilder.ToSasQueryParameters(new StorageSharedKeyCredential(blobClient.AccountName, string.Empty)).ToString();

                        var uriBuilder = new UriBuilder(blobClient.Uri)
                        {
                            Query = sasToken
                        };
                        sasUri = uriBuilder.Uri.ToString();
                        break;
                }

                if (sasUri == null)
                {
                    response.Success = false;
                    response.Message = "Failed to load document.";
                    return BadRequest(response);
                }

                response.Success = true;
                response.Message = sasUri;
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"An error occurred: {ex.Message}";
                return BadRequest(response);
            }
        }

        [HttpPost("clone-investment")]
        public async Task<IActionResult> CloneInvestment(int campaignId, string campaignName)
        {
            CommonResponse response = new();
            try
            {
                campaignName = campaignName.Trim();
                if (!string.IsNullOrEmpty(campaignName))
                {
                    bool nameExists = await _context.Campaigns.AnyAsync(x => x.Name!.Trim() == campaignName);

                    if (nameExists)
                    {
                        response.Success = false;
                        response.Message = "Campaign name already exists.";
                        return Ok(response);
                    }
                }

                var campaign = await _context.Campaigns.FindAsync(campaignId);
                if (campaign == null)
                {
                    response.Success = false;
                    response.Message = "Campaign not found.";
                    return Ok(response);
                }

                var property = campaignName?.ToLower();
                var withoutSpacesProperty = property?.Replace(" ", "");
                var updatedProperty = withoutSpacesProperty + $"-qbe-{DateTime.Now.Year}";

                int counter = 1;
                while (await _context.Campaigns.AnyAsync(x => x.Property == updatedProperty))
                {
                    updatedProperty = withoutSpacesProperty + $"-qbe-{DateTime.Now.Year}-{counter}";
                    counter++;
                }

                var createCampaign = new CampaignDto
                {
                    Name = campaignName,
                    Description = campaign?.Description,
                    Themes = campaign?.Themes,
                    ApprovedBy = campaign?.ApprovedBy,
                    SDGs = campaign?.SDGs,
                    InvestmentTypes = campaign?.InvestmentTypes,
                    Terms = campaign?.Terms,
                    MinimumInvestment = campaign?.MinimumInvestment,
                    Website = campaign?.Website,
                    NetworkDescription = campaign?.NetworkDescription,
                    ContactInfoFullName = campaign?.ContactInfoFullName,
                    ContactInfoAddress = campaign?.ContactInfoAddress,
                    ContactInfoAddress2 = campaign?.ContactInfoAddress2,
                    ContactInfoEmailAddress = null,
                    InvestmentInformationalEmail = null,
                    ContactInfoPhoneNumber = campaign?.ContactInfoPhoneNumber,
                    City = campaign?.City,
                    State = campaign?.State,
                    ZipCode = campaign?.ZipCode,
                    ImpactAssetsFundingStatus = campaign?.ImpactAssetsFundingStatus,
                    InvestmentRole = campaign?.InvestmentRole,
                    Referred = campaign?.Referred,
                    Target = campaign?.Target,
                    Status = "0",
                    TileImageFileName = campaign?.TileImageFileName,
                    ImageFileName = campaign?.ImageFileName,
                    PdfFileName = campaign?.PdfFileName,
                    OriginalPdfFileName = campaign?.OriginalPdfFileName,
                    LogoFileName = campaign?.LogoFileName,
                    IsActive = false,
                    Stage = InvestmentStage.New,
                    Property = updatedProperty,
                    AddedTotalAdminRaised = 0,
                    GroupForPrivateAccessId = null,
                    FundraisingCloseDate = campaign?.FundraisingCloseDate,
                    MissionAndVision = campaign?.MissionAndVision,
                    PersonalizedThankYou = campaign?.PersonalizedThankYou,
                    HasExistingInvestors = campaign?.HasExistingInvestors,
                    ExpectedTotal = campaign?.ExpectedTotal,
                    EmailSends = false,
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now
                };
                _context.Campaigns.Add(createCampaign);
                await _context.SaveChangesAsync();

                response.Success = true;
                response.Message = "Investment cloned successfully.";
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"An error occurred: {ex.Message}";
                return BadRequest(response);
            }
        }

        [HttpGet("get-all-investment-themes-list")]
        public async Task<IActionResult> GetAllThemesList()
        {
            try
            {
                var investmentThemes = await _context.Themes
                                                    .Select(x => new { x.Id, x.Name, x.Mandatory })
                                                    .OrderBy(x => x.Name)
                                                    .ToListAsync();

                if (investmentThemes != null)
                {
                    return Ok(investmentThemes);
                }

                return BadRequest(new { Success = false, Message = "No investment themes found." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpGet("get-all-investment-name-list")]
        public async Task<IActionResult> GetAllInvestmentNameList(int investmentStage)
        {
            try
            {
                var investmentTypes = await _context.InvestmentTypes.ToListAsync();

                if (investmentStage == 4)
                {
                    var campaignList = await _context.Campaigns
                                                    .Where(x => x.Stage != InvestmentStage.ClosedNotInvested && x.Name!.Trim() != string.Empty)
                                                    .Select(x => new
                                                    {
                                                        x.Id,
                                                        x.Name,
                                                        InvestmentTypeIds = x.InvestmentTypes
                                                    })
                                                    .OrderBy(x => x.Name)
                                                    .ToListAsync();

                    var result = campaignList.Select(c => new
                    {
                        c.Id,
                        c.Name,
                        IsPrivateDebt = c.InvestmentTypeIds!
                                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(id => int.Parse(id.Trim()))
                                                .Select(id => investmentTypes.FirstOrDefault(t => t.Id == id)?.Name)
                                                .Any(name => name != null && name.Contains("Private Debt"))
                    }).ToList();

                    return Ok(result);
                }
                else if (investmentStage == 3)
                {
                    var campaignList = await _context.Campaigns
                                                    .Where(x => x.Stage == InvestmentStage.ClosedInvested && x.Name!.Trim() != string.Empty)
                                                    .Select(x => new
                                                    {
                                                        x.Id,
                                                        x.Name
                                                    })
                                                    .OrderBy(x => x.Name)
                                                    .ToListAsync();

                    var result = campaignList.Select(c => new
                    {
                        c.Id,
                        c.Name
                    }).ToList();

                    return Ok(result);
                }
                else if (investmentStage == 0)
                {
                    var campaignList = await _context.Campaigns
                                                        .Where(x => x.Name!.Trim() != string.Empty)
                                                        .Select(x => new
                                                        {
                                                            x.Id,
                                                            x.Name
                                                        })
                                                        .OrderBy(x => x.Name)
                                                        .ToListAsync();

                    var result = campaignList.Select(c => new
                    {
                        c.Id,
                        c.Name
                    }).ToList();

                    return Ok(result);
                }

                return BadRequest(new { Success = false, Message = "Invalid investment stage." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpGet("get-all-investment-type-list")]
        public async Task<IActionResult> GetAllInvestmentTypeList()
        {
            try
            {
                var investmentTypes = await _context.InvestmentTypes
                                            .Select(i => new { i.Id, i.Name })
                                            .OrderBy(i => i.Name)
                                            .ToListAsync();

                investmentTypes.Add(new { Id = -1, Name = (string?)"Other" });

                if (investmentTypes != null)
                {
                    return Ok(investmentTypes);
                }

                return BadRequest(new { Success = false, Message = "Invalid investment stage." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost("get-completed-investments-details")]
        public async Task<IActionResult> GetAllCompletedInvestmentsDetails([FromBody] CompletedInvestmentsRequestDto requestDto)
        {
            try
            {
                if (requestDto.InvestmentId <= 0)
                {
                    return Ok(new { Success = false, Message = "InvestmentId is required." });
                }

                var campaign = await _context.Campaigns
                                                .Where(x => x.Id == requestDto.InvestmentId)
                                                .FirstOrDefaultAsync();

                var recommendations = await _context.Recommendations
                                                .Where(r =>
                                                        r != null &&
                                                        r.Campaign != null &&
                                                        (r.Status!.ToLower() == "approved" || r.Status.ToLower() == "pending") &&
                                                        r.Campaign.Id == requestDto.InvestmentId &&
                                                        r.Amount > 0 &&
                                                        !string.IsNullOrWhiteSpace(r.UserEmail))
                                                .ToListAsync();

                var totalApprovedInvestmentAmount = recommendations?.Where(r => r.Status!.ToLower() == "approved")
                                                                    .Sum(r => r.Amount ?? 0) ?? 0;

                var totalPendingInvestmentAmount = recommendations?.Where(r => r.Status!.ToLower() == "pending")
                                                                    .Sum(r => r.Amount ?? 0) ?? 0;

                var lastInvestmentDate = recommendations?
                                            .OrderByDescending(x => x.Id)
                                            .Select(x => x.DateCreated?.Date)
                                            .FirstOrDefault();

                CompletedInvestmentsResponseDto responseDto = new CompletedInvestmentsResponseDto
                {
                    DateOfLastInvestment = lastInvestmentDate,
                    TypeOfInvestmentIds = campaign?.InvestmentTypes,
                    ApprovedRecommendationsAmount = totalApprovedInvestmentAmount,
                    PendingRecommendationsAmount = totalPendingInvestmentAmount
                };

                if (responseDto != null)
                {
                    return Ok(responseDto);
                }

                return Ok(new { Success = false, Message = "No records found for the selected investment." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost("save-completed-investments-details")]
        public async Task<IActionResult> SaveCompletedInvestmentsDetails([FromBody] CompletedInvestmentsRequestDto requestDto)
        {
            try
            {
                if (requestDto.InvestmentId <= 0)
                {
                    return Ok(new { Success = false, Message = "InvestmentId is required." });
                }
                if (requestDto.TotalInvestmentAmount <= 0)
                {
                    return Ok(new { Success = false, Message = "Amount must be greater than zero." });
                }
                if (string.IsNullOrEmpty(requestDto.InvestmentDetail))
                {
                    return Ok(new { Success = false, Message = "Investment detail is required." });
                }
                if (requestDto.DateOfLastInvestment == null)
                {
                    return Ok(new { Success = false, Message = "Last investment date is required." });
                }

                var campaign = await _context.Campaigns
                                                .Where(x => x.Id == requestDto.InvestmentId)
                                                .FirstOrDefaultAsync();

                var recommendations = await _context.Recommendations
                                                .Where(r =>
                                                        r != null &&
                                                        r.Campaign != null &&
                                                        (r.Status!.ToLower() == "approved" || r.Status.ToLower() == "pending") &&
                                                        r.Campaign.Id == requestDto.InvestmentId &&
                                                        r.Amount > 0 &&
                                                        !string.IsNullOrWhiteSpace(r.UserEmail))
                                                .ToListAsync();

                var totalInvestors = recommendations?.Select(r => r.UserEmail).Distinct().Count() ?? 0;
                var totalInvestmentAmount = recommendations?.Sum(r => r.Amount ?? 0) ?? 0;

                var identity = HttpContext.User.Identity as ClaimsIdentity;
                var userId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;

                var investmentTypeIds = requestDto.TypeOfInvestmentIds?
                                                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(id => id.Trim())
                                                    .Where(id => id != "-1")
                                                    .ToList() ?? new List<string>();

                if (!string.IsNullOrWhiteSpace(requestDto.TypeOfInvestmentIds)
                    && !string.IsNullOrWhiteSpace(requestDto.TypeOfInvestmentName)
                    && requestDto.TypeOfInvestmentIds.Split(',').Any(id => id.Trim() == "-1"))
                {
                    var investmentType = new InvestmentType
                    {
                        Name = requestDto.TypeOfInvestmentName?.Trim()
                    };

                    _context.InvestmentTypes.Add(investmentType);
                    await _context.SaveChangesAsync();

                    investmentTypeIds.Add(investmentType.Id.ToString());
                }

                var updatedTypeOfInvestmentIds = string.Join(",", investmentTypeIds);

                var returnMaster = new CompletedInvestmentsDetails
                {
                    DateOfLastInvestment = requestDto.DateOfLastInvestment,
                    CampaignId = requestDto.InvestmentId,
                    InvestmentDetail = requestDto.InvestmentDetail,
                    Amount = totalInvestmentAmount,
                    TypeOfInvestment = updatedTypeOfInvestmentIds,
                    Donors = totalInvestors,
                    Themes = campaign?.Themes,
                    CreatedBy = userId!,
                    CreatedOn = DateTime.Now
                };

                _context.CompletedInvestmentsDetails.Add(returnMaster);
                await _context.SaveChangesAsync();

                return Ok(new { Success = true, Message = "Investment details saved successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost("get-completed-investments-history")]
        public async Task<IActionResult> GetAllCompletedInvestmentsHistory([FromBody] CompletedInvestmentsPaginationDto requestDto)
        {
            try
            {
                var themes = await _context.Themes.ToListAsync();
                var investmentTypes = await _context.InvestmentTypes.ToListAsync();

                var selectedThemeIds = requestDto.ThemesId?
                                                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(id => int.TryParse(id.Trim(), out var val) ? val : (int?)null)
                                                    .Where(id => id.HasValue)
                                                    .Select(id => id!.Value)
                                                    .ToList() ?? new List<int>();

                var selectedInvestmentTypeIds = requestDto.InvestmentTypeId?
                                                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                            .Select(id => int.TryParse(id.Trim(), out var val) ? val : (int?)null)
                                                            .Where(id => id.HasValue)
                                                            .Select(id => id!.Value)
                                                            .ToList() ?? new List<int>();

                var completedInvestmentsCount = await _context.Campaigns.Where(x => x.Stage == InvestmentStage.ClosedInvested).CountAsync();

                var recommendations = await _context.Recommendations
                                                    .Where(r =>
                                                            r != null &&
                                                            r.Campaign != null &&
                                                            (r.Status!.ToLower() == "approved" || r.Status.ToLower() == "pending") &&
                                                            r.Amount > 0 &&
                                                            !string.IsNullOrWhiteSpace(r.UserEmail))
                                                    .ToListAsync();

                var totalInvestors = recommendations?.Select(r => r.UserEmail?.ToLower().Trim()).Distinct().Count() ?? 0;
                var totalInvestmentAmount = recommendations?.Sum(r => r.Amount ?? 0) ?? 0;

                var adminRole = await _context.Roles.FirstOrDefaultAsync(i => i.Name == "Admin");
                var adminUsers = await _context.UserRoles
                                        .Where(i => adminRole != null && i.RoleId == adminRole.Id)
                                        .Select(i => i.UserId)
                                        .ToListAsync();

                var totalUsersAccountBalance = await _context.Users
                                                        .Where(i => !adminUsers.Contains(i.Id))
                                                        .SumAsync(x => x.AccountBalance);

                var query = await _context.CompletedInvestmentsDetails
                                            .Include(x => x.Campaign)
                                            .ToListAsync();

                string lastCompletedInvestmentsDate = query
                                                        .Where(x => x.DateOfLastInvestment.HasValue)
                                                        .OrderByDescending(x => x.DateOfLastInvestment!.Value)
                                                        .Select(x => DateOnly.FromDateTime(x.DateOfLastInvestment!.Value).ToString("MM/dd/yyyy"))
                                                        .FirstOrDefault() ?? string.Empty;

                var completedInvestmentsHistory = query
                                                    .Select(x =>
                                                    {
                                                        List<int> themeIds = x.Campaign?.Themes?
                                                                                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                                                        .Select(id => int.TryParse(id.Trim(), out var val) ? val : (int?)null)
                                                                                        .Where(id => id.HasValue)
                                                                                        .Select(id => id!.Value)
                                                                                        .ToList() ?? new List<int>();

                                                        var themeNames = themes
                                                                            .Where(t => themeIds.Contains(t.Id))
                                                                            .OrderBy(t => t.Name)
                                                                            .Select(t => t.Name)
                                                                            .ToList();

                                                        List<int> investmentTypesIds = x.TypeOfInvestment?
                                                                                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                                                        .Select(id => int.TryParse(id.Trim(), out var val) ? val : (int?)null)
                                                                                        .Where(id => id.HasValue)
                                                                                        .Select(id => id!.Value)
                                                                                        .ToList() ?? new List<int>();

                                                        var investmentTypesNames = investmentTypes
                                                                                        .Where(i => investmentTypesIds.Contains(i.Id))
                                                                                        .OrderBy(i => i.Name)
                                                                                        .Select(i => i.Name)
                                                                                        .ToList();

                                                        return new
                                                        {
                                                            CreatedOn = x.CreatedOn,
                                                            ThemeIds = themeIds,
                                                            InvestmentTypeIds = investmentTypesIds,
                                                            Dto = new CompletedInvestmentsHistoryResponseDto
                                                            {
                                                                DateOfLastInvestment = x.DateOfLastInvestment,
                                                                InvestmentName = x.Campaign?.Name,
                                                                InvestmentDetail = x.InvestmentDetail,
                                                                TotalInvestmentAmount = x.Amount,
                                                                TypeOfInvestment = string.Join(", ", investmentTypesNames),
                                                                Donors = x.Donors,
                                                                Property = x.Campaign?.Property,
                                                                Themes = string.Join(", ", themeNames)
                                                            }
                                                        };
                                                    })
                                                    .Where(x =>
                                                        (selectedThemeIds?.Count == 0 || x.ThemeIds.Any(id => selectedThemeIds!.Contains(id))) &&
                                                        (selectedInvestmentTypeIds?.Count == 0 || x.InvestmentTypeIds.Any(id => selectedInvestmentTypeIds!.Contains(id))) &&
                                                        (string.IsNullOrEmpty(requestDto.SearchValue) ||
                                                         (!string.IsNullOrEmpty(x.Dto.InvestmentName) && x.Dto.InvestmentName.Contains(requestDto.SearchValue, StringComparison.OrdinalIgnoreCase)) ||
                                                         (!string.IsNullOrEmpty(x.Dto.InvestmentDetail) && x.Dto.InvestmentDetail.Contains(requestDto.SearchValue, StringComparison.OrdinalIgnoreCase)))
                                                    )
                                                    .ToList();

                bool isAsc = requestDto?.SortDirection?.ToLower() == "asc";
                string? sortField = requestDto?.SortField?.ToLower();

                completedInvestmentsHistory = sortField switch
                {
                    "dateoflastinvestment" => isAsc
                        ? completedInvestmentsHistory.OrderBy(x => x.Dto.DateOfLastInvestment).ToList()
                        : completedInvestmentsHistory.OrderByDescending(x => x.Dto.DateOfLastInvestment).ToList(),

                    "investmentname" => isAsc
                        ? completedInvestmentsHistory.OrderBy(x => x.Dto.InvestmentName).ToList()
                        : completedInvestmentsHistory.OrderByDescending(x => x.Dto.InvestmentName).ToList(),

                    "investmentdetail" => isAsc
                        ? completedInvestmentsHistory.OrderBy(x => x.Dto.InvestmentDetail).ToList()
                        : completedInvestmentsHistory.OrderByDescending(x => x.Dto.InvestmentDetail).ToList(),

                    "totalinvestmentamount" => isAsc
                        ? completedInvestmentsHistory.OrderBy(x => x.Dto.TotalInvestmentAmount).ToList()
                        : completedInvestmentsHistory.OrderByDescending(x => x.Dto.TotalInvestmentAmount).ToList(),

                    "donors" => isAsc
                        ? completedInvestmentsHistory.OrderBy(x => x.Dto.Donors).ToList()
                        : completedInvestmentsHistory.OrderByDescending(x => x.Dto.Donors).ToList(),

                    "typeofinvestment" => isAsc
                        ? completedInvestmentsHistory.OrderBy(x => x.Dto.TypeOfInvestment).ToList()
                        : completedInvestmentsHistory.OrderByDescending(x => x.Dto.TypeOfInvestment).ToList(),

                    "themes" => isAsc
                        ? completedInvestmentsHistory.OrderBy(x => x.Dto.Themes).ToList()
                        : completedInvestmentsHistory.OrderByDescending(x => x.Dto.Themes).ToList(),

                    _ => completedInvestmentsHistory.OrderByDescending(x => x.CreatedOn).ThenBy(x => x.Dto.InvestmentName).ToList()
                };

                int totalCount = completedInvestmentsHistory.Count();

                if (totalCount > 0)
                {
                    int currentPage = requestDto?.CurrentPage ?? 1;
                    int perPage = requestDto?.PerPage ?? 10;

                    var pagedReturns = completedInvestmentsHistory
                                            .Skip((currentPage - 1) * perPage)
                                            .Take(perPage)
                                            .Select(x => x.Dto)
                                            .ToList();

                    dynamic response = new ExpandoObject();
                    response.items = pagedReturns;
                    response.totalCount = totalCount;
                    response.completedInvestments = completedInvestmentsCount;
                    response.totalInvestmentAmount = totalInvestmentAmount + totalUsersAccountBalance;
                    response.totalInvestors = totalInvestors;
                    response.lastCompletedInvestmentsDate = lastCompletedInvestmentsDate;

                    return Ok(response);
                }

                return Ok(new { Success = false, Message = "No records found for completed investments." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpGet("export-completed-investments")]
        public async Task<IActionResult> ExportCompletedInvestments()
        {
            var themes = await _context.Themes.ToListAsync();
            var investmentTypes = await _context.InvestmentTypes.ToListAsync();

            var query = await _context.CompletedInvestmentsDetails
                                        .Include(x => x.Campaign)
                                        .ToListAsync();

            var completedInvestments = query
                                        .Select(x =>
                                        {
                                            List<int> themeIds = x.Campaign?.Themes?
                                                                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                                            .Select(id => int.TryParse(id.Trim(), out var val) ? val : (int?)null)
                                                                            .Where(id => id.HasValue)
                                                                            .Select(id => id!.Value)
                                                                            .ToList() ?? new List<int>();

                                            var themeNames = themes
                                                                .Where(t => themeIds.Contains(t.Id))
                                                                .Select(t => t.Name)
                                                                .ToList();

                                            List<int> investmentTypesIds = x.TypeOfInvestment?
                                                                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                                                .Select(id => int.TryParse(id.Trim(), out var val) ? val : (int?)null)
                                                                                .Where(id => id.HasValue)
                                                                                .Select(id => id!.Value)
                                                                                .ToList() ?? new List<int>();

                                            var investmentTypesNames = investmentTypes
                                                                            .Where(t => investmentTypesIds.Contains(t.Id))
                                                                            .Select(t => t.Name)
                                                                            .ToList();

                                            return new
                                            {
                                                CreatedOn = x.CreatedOn,
                                                Dto = new CompletedInvestmentsHistoryResponseDto
                                                {
                                                    DateOfLastInvestment = x.DateOfLastInvestment,
                                                    InvestmentName = x.Campaign?.Name,
                                                    InvestmentDetail = x.InvestmentDetail,
                                                    TotalInvestmentAmount = x.Amount,
                                                    TypeOfInvestment = string.Join(", ", investmentTypesNames),
                                                    Donors = x.Donors,
                                                    Themes = string.Join(", ", themeNames)
                                                }
                                            };
                                        })
                                        .OrderByDescending(x => x.CreatedOn)
                                        .ThenBy(x => x.Dto.InvestmentName)
                                        .Select(x => x.Dto)
                                        .ToList();

            string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            string fileName = "CompletedInvestmentsDetails.xlsx";

            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet worksheet = workbook.Worksheets.Add("Returns");

                var headers = new[]
                {
                    "Date Of Last Investment", "Investment Name", "Investment Detail", "Amount", "Type Of Investment", "Donors", "Themes"
                };

                for (int col = 0; col < headers.Length; col++)
                {
                    worksheet.Cell(1, col + 1).Value = headers[col];
                }

                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;

                worksheet.Columns().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                for (int index = 0; index < completedInvestments.Count; index++)
                {
                    var dto = completedInvestments[index];
                    int row = index + 2;

                    worksheet.Cell(row, 1).Value = dto.DateOfLastInvestment;
                    worksheet.Cell(row, 2).Value = dto.InvestmentName;
                    worksheet.Cell(row, 3).Value = dto.InvestmentDetail;
                    worksheet.Cell(row, 4).Value = $"${Convert.ToDecimal(dto.TotalInvestmentAmount):N2}";
                    worksheet.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(row, 5).Value = dto.TypeOfInvestment;
                    worksheet.Cell(row, 6).Value = dto.Donors;
                    worksheet.Cell(row, 7).Value = dto.Themes;
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

        [HttpPost("calculate-returns")]
        public async Task<IActionResult> CalculateReturns([FromBody] ReturnCalculationRequestDto requestDto)
        {
            try
            {
                if (requestDto.InvestmentId <= 0)
                {
                    return Ok(new { Success = false, Message = "InvestmentId is required." });
                }
                if (requestDto.ReturnAmount <= 0)
                {
                    return Ok(new { Success = false, Message = "Return amount must be greater than zero." });
                }

                var campaignName = await _context.Campaigns.Where(x => x.Id == requestDto.InvestmentId).Select(x => x.Name).SingleOrDefaultAsync();

                var activeUsers = await _context.Users.Where(x => x.IsActive == true).Select(x => x.Email).ToListAsync();

                var recommendations = await _context.Recommendations
                                                    .Where(x => x.Campaign != null
                                                                && x.Campaign.Id == requestDto.InvestmentId
                                                                && x.Status!.ToLower() == "approved"
                                                                && activeUsers.Contains(x.UserEmail!))
                                                    .ToListAsync();

                decimal totalInvestment = recommendations.Sum(x => x.Amount ?? 0);

                var results = (from r in recommendations
                               join u in _context.Users on r.UserEmail?.ToLower() equals u.Email.ToLower()
                               let userPercentage = (Convert.ToDecimal(r.Amount) / totalInvestment)
                               select new ReturnCalculationResponseDto
                               {
                                   InvestmentName = campaignName,
                                   FirstName = u.FirstName,
                                   LastName = u.LastName,
                                   Email = r.UserEmail,
                                   InvestmentAmount = Convert.ToDecimal(r.Amount),
                                   Percentage = Math.Round(userPercentage * 100m, 2),
                                   ReturnedAmount = Math.Round(userPercentage * requestDto.ReturnAmount, 2)
                               })
                                .OrderByDescending(x => x.InvestmentAmount)
                                .ToList();

                int totalCount = results.Count;

                if (requestDto.CurrentPage.HasValue && requestDto.PerPage.HasValue)
                {
                    int currentPage = requestDto.CurrentPage ?? 1;
                    int perPage = requestDto.PerPage ?? 10;

                    results = results.Skip((currentPage - 1) * perPage).Take(perPage).ToList();
                }

                if (totalCount > 0)
                {
                    dynamic response = new ExpandoObject();
                    response.items = results;
                    response.totalCount = totalCount;
                    response.investmentName = campaignName;
                    response.investmentId = requestDto.InvestmentId;
                    return Ok(response);
                }

                return Ok(new { Success = false, Message = "No records found for the selected investment." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpPost("save-returns")]
        public async Task<IActionResult> SaveReturns([FromBody] ReturnCalculationRequestDto requestDto)
        {
            try
            {
                if (requestDto.InvestmentId <= 0)
                {
                    return Ok(new { Success = false, Message = "InvestmentId is required." });
                }
                if (requestDto.ReturnAmount <= 0)
                {
                    return Ok(new { Success = false, Message = "Return amount must be greater than zero." });
                }
                if (string.IsNullOrEmpty(requestDto.MemoNote))
                {
                    return Ok(new { Success = false, Message = "Admin memo is required." });
                }

                var allEmailTasks = new List<Task>();

                var identity = HttpContext.User.Identity as ClaimsIdentity;
                var userId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;

                var actionResult = await CalculateReturns(requestDto) as OkObjectResult;

                if (actionResult == null || actionResult.Value == null)
                    return BadRequest(new { Success = false, Message = "Failed to calculate returns." });

                dynamic responseDto = actionResult.Value;

                var items = (IEnumerable<ReturnCalculationResponseDto>)responseDto.items;

                var returnMaster = new ReturnMaster
                {
                    CampaignId = requestDto.InvestmentId,
                    CreatedBy = userId!,
                    ReturnAmount = requestDto.ReturnAmount,
                    TotalInvestors = items.Count(),
                    TotalInvestmentAmount = Convert.ToDecimal(items.Sum(x => x.InvestmentAmount)),
                    MemoNote = !string.IsNullOrEmpty(requestDto.MemoNote) ? requestDto.MemoNote : null,
                    Status = "Accepted",
                    PrivateDebtStartDate = requestDto.PrivateDebtStartDate,
                    PrivateDebtEndDate = requestDto.PrivateDebtEndDate,
                    PostDate = DateTime.Now,
                    CreatedOn = DateTime.Now
                };

                _context.ReturnMasters.Add(returnMaster);
                await _context.SaveChangesAsync();

                foreach (var item in items)
                {
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == item.Email);

                    var returnDetail = new ReturnDetails
                    {
                        ReturnMasterId = returnMaster.Id,
                        UserId = user?.Id!,
                        InvestmentAmount = Convert.ToDecimal(item.InvestmentAmount),
                        PercentageOfTotalInvestment = Convert.ToDecimal(item.Percentage),
                        ReturnAmount = Convert.ToDecimal(item.ReturnedAmount)
                    };

                    _context.ReturnDetails.Add(returnDetail);

                    await UpdateUsersWalletBalance(user!, Convert.ToDecimal(item.ReturnedAmount), returnMaster.Campaign?.Name!, returnMaster.Id);

                    allEmailTasks.Add(SendReturnsEmail(user!.Email, user.FirstName, user.LastName, item.InvestmentName, Convert.ToDecimal(item.ReturnedAmount)));
                }
                await _context.SaveChangesAsync();
                await _repository.SaveAsync();

                _ = Task.WhenAll(allEmailTasks);

                return Ok(new { Success = true, Message = "Returns submitted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }
        private async Task UpdateUsersWalletBalance(User user, decimal amount, string investmentName, int ReturnMastersId)
        {
            var accountBalanceChangeLog = new AccountBalanceChangeLog
            {
                UserId = user.Id,
                PaymentType = $"Returned Amount, Return Masters Id= {ReturnMastersId}",
                OldValue = user.AccountBalance,
                UserName = user.UserName,
                NewValue = user.AccountBalance + amount,
                InvestmentName = investmentName
            };

            await _context.AccountBalanceChangeLogs.AddAsync(accountBalanceChangeLog);

            user.AccountBalance += amount;

            await _repository.UserAuthentication.UpdateUser(user);
        }
        private async Task SendReturnsEmail(string emailTo, string? firstName, string? lastName, string? investmentName, decimal returnedAmount)
        {
            string request = HttpContext.Request.Headers["Origin"].ToString();
            string logoUrl = $"{request}/logo-for-email.png";
            string logoHtml = $@"
                                <div style='text-align: center;'>
                                    <a href='https://investment-campaign.org' target='_blank'>
                                        <img src='{logoUrl}' alt='Logo' width='300' height='150' />
                                    </a>
                                </div>";

            string formattedAmount = string.Format(CultureInfo.GetCultureInfo("en-US"), "${0:N2}", returnedAmount);

            string subject = "You Got Funded! Your Investment Campaign Is Growing";

            var body = logoHtml + $@"
                                    <html>
                                        <body>
                                            <p><b>Hi {firstName} {lastName},</b></p>
                                            <p>Great news — <b>{investmentName}</b> just returned <b>{formattedAmount}</b> to your donor account on Investment Campaign!</p>
                                            <p>Your available balance now reflects this amount and can be part of a new impact investment.</p>
                                            <p style='margin-bottom: 0px;'>With deep gratitude,</p>
                                            <p style='margin-top: 0px;'>— The Investment Campaign Team</p>
                                            <p>🌍 <a href='https://investment-campaign.org/'>investment-campaign.org</a> | 💼 <a href='https://www.linkedin.com/company/investment-campaign-us/'>Follow us on LinkedIn</a></p>
                                            <p><a href='{request}/settings' target='_blank'>Unsubscribe</a> from Investment Campaign notifications.</p>
                                        </body>
                                    </html>";

            await _mailService.SendMailAsync(emailTo, subject, "", body);
        }

        [HttpPost("get-returns-history")]
        public async Task<IActionResult> GetReturnsHistory([FromBody] ReturnsHistoryRequestDto requestDto)
        {
            try
            {
                var query = _context.ReturnMasters?
                                    .Where(x => x.ReturnDetails != null)
                                    .Include(x => x.ReturnDetails)!
                                        .ThenInclude(x => x.User)
                                    .Include(x => x.Campaign)
                                    .AsQueryable();

                if (requestDto.InvestmentId > 0)
                {
                    query = query?.Where(x => x.CampaignId == requestDto.InvestmentId);
                }

                List<ReturnMaster> returnMasters = await query!.ToListAsync();

                int totalCount = returnMasters.SelectMany(x => x.ReturnDetails!).Count();

                var returnsHistory = returnMasters
                                    .SelectMany(rm => rm.ReturnDetails ?? new List<ReturnDetails>(), (rm, rd) => new
                                    {
                                        CreatedOn = rm.CreatedOn,
                                        InvestmentAmount = rd.InvestmentAmount,
                                        Dto = new ReturnsHistoryResponseDto
                                        {
                                            InvestmentName = rm.Campaign?.Name,
                                            FirstName = rd.User?.FirstName,
                                            LastName = rd.User?.LastName,
                                            Email = rd.User?.Email,
                                            InvestmentAmount = rd.InvestmentAmount,
                                            Percentage = rd.PercentageOfTotalInvestment,
                                            ReturnedAmount = rd.ReturnAmount,
                                            Memo = rm.MemoNote,
                                            Status = rm.Status,
                                            PrivateDebtDates = rm.PrivateDebtStartDate.HasValue && rm.PrivateDebtEndDate.HasValue
                                                                ? string.Format(CultureInfo.GetCultureInfo("en-US"), "{0:MM/dd/yy}-{1:MM/dd/yy}",
                                                                    rm.PrivateDebtStartDate.Value.Date,
                                                                    rm.PrivateDebtEndDate.Value.Date)
                                                                : null,
                                            PostDate = rm.PostDate.Date.ToString("MM/dd/yy", CultureInfo.GetCultureInfo("en-US"))
                                        }
                                    })
                                    .OrderByDescending(x => x.CreatedOn)
                                    .ThenByDescending(x => x.InvestmentAmount)
                                    .Select(x => x.Dto)
                                    .ToList();

                if (totalCount > 0)
                {
                    int currentPage = requestDto.CurrentPage ?? 1;
                    int perPage = requestDto.PerPage ?? 10;

                    var pagedReturns = returnsHistory.Skip((currentPage - 1) * perPage).Take(perPage).ToList();

                    dynamic response = new ExpandoObject();
                    response.items = pagedReturns;
                    response.totalCount = totalCount;
                    return Ok(response);
                }

                return Ok(new { Success = false, Message = "No data found for the selected investment." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpGet("export-returns")]
        public async Task<IActionResult> ExportReturns()
        {
            var query = await _context.ReturnMasters
                                        .Where(x => x.ReturnDetails != null)
                                        .Include(x => x.ReturnDetails)!
                                            .ThenInclude(x => x.User)
                                        .Include(x => x.Campaign)
                                        .ToListAsync();

            var returnMasters = query
                                .SelectMany(rm => rm.ReturnDetails ?? new List<ReturnDetails>(), (rm, rd) => new
                                {
                                    CreatedOn = rm.CreatedOn,
                                    InvestmentAmount = rd.InvestmentAmount,
                                    Dto = new ReturnsHistoryResponseDto
                                    {
                                        InvestmentName = rm.Campaign?.Name,
                                        FirstName = rd.User?.FirstName,
                                        LastName = rd.User?.LastName,
                                        Email = rd.User?.Email,
                                        InvestmentAmount = rd.InvestmentAmount,
                                        Percentage = rd.PercentageOfTotalInvestment,
                                        ReturnedAmount = rd.ReturnAmount,
                                        Memo = rm.MemoNote,
                                        Status = rm.Status,
                                        PrivateDebtDates = rm.PrivateDebtStartDate.HasValue && rm.PrivateDebtEndDate.HasValue
                                                            ? string.Format(CultureInfo.GetCultureInfo("en-US"), "{0:MM/dd/yy}-{1:MM/dd/yy}",
                                                                rm.PrivateDebtStartDate.Value.Date,
                                                                rm.PrivateDebtEndDate.Value.Date)
                                                            : null,
                                        PostDate = rm.PostDate.Date.ToString("MM/dd/yy", CultureInfo.GetCultureInfo("en-US"))
                                    }
                                })
                                .OrderByDescending(x => x.CreatedOn)
                                .ThenByDescending(x => x.InvestmentAmount)
                                .Select(x => x.Dto)
                                .ToList();

            string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            string fileName = "Returns.xlsx";

            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet worksheet = workbook.Worksheets.Add("Returns");

                var headers = new[]
                {
                    "Investment Name", "Date Range", "Post Date", "First Name", "Last Name", "Email",
                    "Investment Amount", "Percentage", "Returned Amount", "Memo", "Status"
                };

                for (int col = 0; col < headers.Length; col++)
                {
                    worksheet.Cell(1, col + 1).Value = headers[col];
                }

                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;

                worksheet.Columns().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                for (int index = 0; index < returnMasters.Count; index++)
                {
                    var dto = returnMasters[index];
                    int row = index + 2;

                    worksheet.Cell(row, 1).Value = dto.InvestmentName;
                    worksheet.Cell(row, 2).Value = dto.PrivateDebtDates;
                    worksheet.Cell(row, 3).Value = dto.PostDate;
                    worksheet.Cell(row, 4).Value = dto.FirstName;
                    worksheet.Cell(row, 5).Value = dto.LastName;
                    worksheet.Cell(row, 6).Value = dto.Email;
                    worksheet.Cell(row, 7).Value = $"${Convert.ToDecimal(dto.InvestmentAmount):N2}";
                    worksheet.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(row, 8).Value = dto.Percentage / 100m;
                    worksheet.Cell(row, 8).Style.NumberFormat.Format = "0.00%";
                    worksheet.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(row, 9).Value = $"${Convert.ToDecimal(dto.ReturnedAmount):N2}";
                    worksheet.Cell(row, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(row, 10).Value = dto.Memo;
                    worksheet.Cell(row, 11).Value = dto.Status;
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

        [HttpGet("get-campaign-data-for-thank-you-page")]
        public async Task<IActionResult> GetCampaignData(int? campaignId, int? groupId)
        {
            try
            {
                CampaignDto? campaign = null;
                if (campaignId != null || campaignId > 0)
                    campaign = await _context.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId);

                bool isFromGroup = false;
                Group? group = null;
                List<string> groupMemberEmails = new List<string>();

                if (groupId != null || groupId > 0)
                {
                    isFromGroup = true;
                    group = await _context.Groups.FindAsync(groupId);

                    groupMemberEmails = await _context.Requests
                                                      .Where(x => x.GroupToFollow!.Id == groupId
                                                               && x.UserToFollow != null)
                                                      .Select(x => x.UserToFollow!.Email)
                                                      .ToListAsync();
                }

                int totalFellowDonorInvestors = isFromGroup
                                                    ? await _context.Requests
                                                                    .Where(x => x.GroupToFollow!.Id == groupId
                                                                            && x.Status!.ToLower().Trim() == "accepted")
                                                                    .Select(x => x.UserToFollow!.Id)
                                                                    .Distinct()
                                                                    .CountAsync()
                                                    : await _context.Users.CountAsync(x => x.IsActive == true);

                var totalRecommendationsAmount = isFromGroup
                                                    ? await _context.Recommendations
                                                                    .Where(r =>
                                                                           groupMemberEmails.Contains(r.UserEmail!) &&
                                                                           (r.Status == "approved" || r.Status == "pending") &&
                                                                           r.CampaignId != null &&
                                                                           r.Amount > 0)
                                                                    .SumAsync(r => (decimal?)r.Amount) ?? 0
                                                    : await _context.Recommendations
                                                                    .Where(r =>
                                                                        (r.Status == "approved" || r.Status == "pending") &&
                                                                        r.CampaignId != null &&
                                                                        r.Amount > 0 &&
                                                                        r.UserEmail != null)
                                                                    .SumAsync(r => (decimal?)r.Amount) ?? 0;

                int totalCompletedInvestments = await _context.CompletedInvestmentsDetails.CountAsync();

                string themeNamesStr = string.Empty;
                List<MatchedCampaignsCardDto>? matchedCampaignsCardDtos = null;

                if (campaignId > 0)
                {
                    List<int> themeIds = campaign?.Themes?
                                                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(id => int.TryParse(id.Trim(), out var val) ? val : (int?)null)
                                                .Where(id => id.HasValue)
                                                .Select(id => id!.Value)
                                                .ToList() ?? new List<int>();

                    List<string> themeNames = _context.Themes
                                                        .Where(t => themeIds.Contains(t.Id) && t.Name != null)
                                                        .Select(t => t.Name!)
                                                        .ToList();

                    themeNamesStr = string.Join(", ", themeNames);

                    var recommendationAggregates = await _context.Recommendations
                                                                .Where(r => r.Amount > 0 &&
                                                                            r.UserEmail != null &&
                                                                            (r.Status == "approved" || r.Status == "pending"))
                                                                .GroupBy(r => r.CampaignId)
                                                                .Select(g => new
                                                                {
                                                                    CampaignId = g.Key,
                                                                    CurrentBalance = g.Sum(r => (decimal?)r.Amount) ?? 0,
                                                                    NumberOfInvestors = g.Select(r => r.UserEmail!).Distinct().Count()
                                                                })
                                                                .ToListAsync();

                    var recsWithAvatars = await _context.Recommendations
                                                        .Where(r => r.UserEmail != null && (r.Status == "approved" || r.Status == "pending") && r.CampaignId != null)
                                                        .Join(_context.Users,
                                                              r => r.UserEmail,
                                                              u => u.Email,
                                                              (r, u) => new { r.CampaignId, u.PictureFileName, r.Id })
                                                        .Where(x => x.PictureFileName != null)
                                                        .OrderByDescending(x => x.CampaignId)
                                                        .ToListAsync();

                    var avatarsLookup = recsWithAvatars
                                                .GroupBy(x => x.CampaignId!.Value)
                                                .ToDictionary(
                                                    g => g.Key,
                                                    g => g.OrderByDescending(x => x.Id)
                                                          .Select(x => x.PictureFileName!)
                                                          .Distinct()
                                                          .Take(3)
                                                          .ToList()
                                                );

                    if (themeIds.Any())
                    {
                        matchedCampaignsCardDtos = _context.Campaigns
                                                            .Where(c => c.IsActive == true &&
                                                                        c.Stage == InvestmentStage.Public &&
                                                                        c.Id != campaign!.Id)
                                                            .AsEnumerable()
                                                            .Where(c => themeIds.Any(id =>
                                                                    c.Themes == id.ToString() ||
                                                                    c.Themes!.StartsWith(id + ",") ||
                                                                    c.Themes.EndsWith("," + id) ||
                                                                    c.Themes.Contains("," + id + ",")))
                                                            .Select(c =>
                                                            {
                                                                var agg = recommendationAggregates.FirstOrDefault(a => a.CampaignId == c.Id);
                                                                return new MatchedCampaignsCardDto
                                                                {
                                                                    Id = c.Id,
                                                                    Name = c.Name!,
                                                                    Description = c.Description!,
                                                                    Target = c.Target!,
                                                                    TileImageFileName = c.TileImageFileName!,
                                                                    ImageFileName = c.ImageFileName!,
                                                                    Property = c.Property!,
                                                                    CurrentBalance = agg?.CurrentBalance ?? 0,
                                                                    NumberOfInvestors = agg?.NumberOfInvestors ?? 0,
                                                                    LatestInvestorAvatars = c.Id.HasValue && avatarsLookup.ContainsKey(c.Id.Value)
                                                                                                ? avatarsLookup[c.Id.Value]
                                                                                                : new List<string>()
                                                                };
                                                            })
                                                            .OrderByDescending(c => c.CurrentBalance)
                                                            .Take(3)
                                                            .ToList();
                    }
                }

                var campaignData = new
                {
                    themes = themeNamesStr,
                    fellowDonorInvestors = totalFellowDonorInvestors,
                    totalRaisedforImpact = totalRecommendationsAmount,
                    completedInvestments = totalCompletedInvestments,
                    matchedCampaigns = matchedCampaignsCardDtos
                };

                return Ok(campaignData);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpGet("check-missing-investment-urls")]
        public async Task<IActionResult> CheckMissingInvestmentUrls()
        {
            try
            {
                var campaigns = await _context.Campaigns.Where(x => string.IsNullOrWhiteSpace(x.Property)).Select(x => x.Name).ToListAsync();

                return Ok(new { InvestmentUrlNotExist = campaigns });
            }
            catch (Exception ex)
            {
                return BadRequest(ex);
            }
        }

        [HttpGet("get-list-of-pdf-not-exist-on-azure")]
        public async Task<IActionResult> GetNotExistPdfList()
        {
            try
            {
                var campaigns = await _context.Campaigns
                                .Where(c => !string.IsNullOrEmpty(c.PdfFileName))
                                .Select(c => new { c.Name, c.PdfFileName })
                                .ToListAsync();

                var missingCampaigns = new List<string>();

                foreach (var campaign in campaigns)
                {
                    var blobClient = _blobContainerClient.GetBlobClient(campaign.PdfFileName);

                    if (!await blobClient.ExistsAsync())
                    {
                        missingCampaigns.Add(campaign?.Name!);
                    }
                }

                return Ok(new { MissingFilesCampaignName = missingCampaigns });
            }
            catch (Exception ex)
            {
                return BadRequest(ex);
            }
        }

        [HttpGet("download-all-files-from-container")]
        public async Task<IActionResult> DownloadAllFiles()
        {
            try
            {
                var zipStream = new MemoryStream();

                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await foreach (BlobItem blobItem in _blobContainerClient.GetBlobsAsync())
                    {
                        var blobClient = _blobContainerClient.GetBlobClient(blobItem.Name);
                        var blobDownloadInfo = await blobClient.DownloadAsync();

                        var entry = archive.CreateEntry(blobItem.Name, CompressionLevel.Fastest);

                        using (var entryStream = entry.Open())
                        {
                            await blobDownloadInfo.Value.Content.CopyToAsync(entryStream);
                        }
                    }
                }

                zipStream.Position = 0;

                var containerName = _blobContainerClient.Name;
                var zipFileName = $"{containerName}_AllFiles.zip";

                return File(zipStream, "application/zip", zipFileName);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "An error occurred.", error = ex.Message });
            }
        }
    }

    public class Data
    {
        public IEnumerable<Category> Theme { get; set; } = Enumerable.Empty<Category>();
        public IEnumerable<Sdg> Sdg { get; set; } = Enumerable.Empty<Sdg>();
        public IEnumerable<InvestmentType> InvestmentType { get; set; } = Enumerable.Empty<InvestmentType>();
        public IEnumerable<ApprovedBy> ApprovedBy { get; set; } = Enumerable.Empty<ApprovedBy>();
    }

    public class Portfolio
    {
        public decimal? AccountBalance { get; set; }
        public decimal? GroupBalance { get; set; }
        public List<RecommendationsDto> Recommendations { get; set; } = new List<RecommendationsDto>();
        public List<Campaign> Campaigns { get; set; } = new List<Campaign>();
    }

    public class NetworkRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
