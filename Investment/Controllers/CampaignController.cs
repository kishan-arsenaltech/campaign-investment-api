// Ignore Spelling: Admin Pdf Dto Sdg Captcha Accessors

using AutoMapper;
using Azure.Communication.Email;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using ClosedXML.Excel;
using Invest.Authorization.Enums;
using Invest.Core.Constants;
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
using QRCoder;
using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;

namespace Invest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Module(Modules.Investment)]
    public class CampaignController : ControllerBase
    {
        private readonly RepositoryContext _context;
        private readonly BlobContainerClient _blobContainerClient;
        private readonly IMapper _mapper;
        private readonly IMailService _mailService;
        private readonly IRepositoryManager _repository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AppSecrets _appSecrets;
        private readonly HttpClient _httpClient;
        private readonly EmailQueue _emailQueue;
        private readonly string requestOrigin = string.Empty;

        public CampaignController(RepositoryContext context, BlobContainerClient blobContainerClient, IMapper mapper, IMailService mailService, IRepositoryManager repository, IHttpContextAccessor httpContextAccessors, AppSecrets appSecrets, HttpClient httpClient, EmailQueue emailQueue)
        {
            _context = context;
            _mapper = mapper;
            _mailService = mailService;
            _blobContainerClient = blobContainerClient;
            _repository = repository;
            _httpContextAccessor = httpContextAccessors;
            _appSecrets = appSecrets;
            _httpClient = httpClient;
            _emailQueue = emailQueue;
            requestOrigin = httpContextAccessors.HttpContext!.Request.Headers["Origin"].ToString();
        }

        [HttpGet]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<ActionResult<IEnumerable<CampaignCardDto>>> GetCampaigns()
        {
            var campaigns = await GetCampaignsCardDto();
            if (campaigns != null)
                return Ok(campaigns);
            else
                return NotFound();
        }

        [HttpGet("trending-campaigns")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<ActionResult<IEnumerable<CampaignCardDtov2>>> GetTrendingCampaigns()
        {
            var campaigns = await GetTrendingCampaignsCardDto();
            if (campaigns != null)
                return Ok(campaigns);
            else
                return NotFound();
        }

        [HttpGet("with-categories")]
        [ModuleAuthorize(PermissionType.View)]
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

        [HttpGet("admin-campaigns")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> Get([FromQuery] PaginationDto pagination)
        {
            bool? isDeleted = pagination?.IsDeleted;
            var search = pagination?.SearchValue?.Trim();

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

            if (!string.IsNullOrEmpty(pagination?.Stages))
            {
                stages = pagination.Stages
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(int.Parse)
                                    .ToList();
            }

            var query = _context.Campaigns
                                .ApplySoftDeleteFilter(isDeleted)
                                .Where(c =>
                                    (string.IsNullOrEmpty(search)
                                        || (c.Name != null && EF.Functions.Like(c.Name, $"%{search.ToLower()}%")))
                                    && (stages == null
                                        || (c.Stage.HasValue && stages.Contains((int)c.Stage.Value)))
                                    && (!pagination!.InvestmentStatus.HasValue
                                        || c.IsActive == pagination.InvestmentStatus.Value)
                                )
                                .Select(c => new
                                {
                                    c.Id,
                                    c.Name,
                                    c.CreatedDate,
                                    c.AddedTotalAdminRaised,
                                    c.Stage,
                                    c.FundraisingCloseDate,
                                    c.IsActive,
                                    c.Property,
                                    OriginalPdfFileName = c.OriginalPdfFileName != null ? c.OriginalPdfFileName : null,
                                    ImageFileName = c.ImageFileName != null ? c.ImageFileName : null,
                                    PdfFileName = c.PdfFileName != null ? c.PdfFileName : null,
                                    c.MetaTitle,
                                    c.MetaDescription,
                                    c.DeletedAt,
                                    c.DeletedByUser
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
                                            c.FundraisingCloseDate,
                                            c.IsActive,
                                            c.Property,
                                            OriginalPdfFileName = c.OriginalPdfFileName != null ? c.OriginalPdfFileName : null,
                                            ImageFileName = c.ImageFileName != null ? c.ImageFileName : null,
                                            PdfFileName = c.PdfFileName != null ? c.PdfFileName : null,
                                            CurrentBalance = hasRec ? rec!.CurrentBalance : 0,
                                            NumberOfInvestors = hasRec ? rec!.NumberOfInvestors : 0,
                                            c.MetaTitle,
                                            c.MetaDescription,
                                            c.DeletedAt,
                                            DeletedBy = c.DeletedByUser != null
                                                        ? $"{c.DeletedByUser.FirstName} {c.DeletedByUser.LastName}"
                                                        : null
                                        };
                                    })
                                    .ToList();

            bool isAsc = pagination?.SortDirection?.ToLower() == "asc";
            enrichedCampaigns = pagination?.SortField?.ToLower() switch
            {
                "name" => isAsc ? enrichedCampaigns.OrderBy(x => x.Name?.Trim()).ToList() : enrichedCampaigns.OrderByDescending(x => x.Name?.Trim()).ToList(),
                "createddate" => isAsc ? enrichedCampaigns.OrderBy(x => x.CreatedDate).ToList() : enrichedCampaigns.OrderByDescending(x => x.CreatedDate).ToList(),
                "catacapfunding" => isAsc ? enrichedCampaigns.OrderBy(x => x.CurrentBalance).ToList() : enrichedCampaigns.OrderByDescending(x => x.CurrentBalance).ToList(),
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
        [ModuleAuthorize(PermissionType.View)]
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
        [ModuleAuthorize(PermissionType.View)]
        public async Task<ActionResult<DataDto>> GetData()
        {
            var sdgs = await _context.SDGs.ToListAsync();
            var themes = await _context.Themes.ToListAsync();
            var investmentTypes = await _context.InvestmentTypes.ToListAsync();
            var approvedBy = await _context.ApprovedBy.ToListAsync();

            return new DataDto
            {
                Sdg = sdgs,
                Theme = themes,
                InvestmentType = investmentTypes,
                ApprovedBy = approvedBy
            };
        }

        [HttpGet("pdf/{identifier}")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<ActionResult<string>> GetPdf(string identifier)
        {
            if (_context.Campaigns == null)
                return NotFound();
            
            var campaign = new CampaignDto();

            campaign = await _context.Campaigns
                                        .Where(c => !string.IsNullOrWhiteSpace(c.Property) && c.Property == identifier)
                                        .FirstOrDefaultAsync();

            if (campaign == null)
                campaign = await _context.Campaigns.FindAsync(Convert.ToInt32(identifier));

            if (campaign == null)
                return NotFound();
            
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
        [ModuleAuthorize(PermissionType.View)]
        public async Task<ActionResult<Campaign>> GetCampaign(string identifier)
        {
            if (_context.Campaigns == null)
                return NotFound();

            var campaign = new CampaignDto();

            int? campaignId = null;

            if (int.TryParse(identifier, out var parsedId))
                campaignId = parsedId;

            campaign = await _context.Campaigns
                                     .FirstOrDefaultAsync(c =>
                                         (!string.IsNullOrWhiteSpace(c.Property) && c.Property == identifier) ||
                                         (campaignId.HasValue && c.Id == campaignId.Value));

            if (campaign == null)
            {
                var slugs = await _context.Slug
                                      .FirstOrDefaultAsync(x =>
                                          x.Type == SlugType.Investment &&
                                          x.Value == identifier);

                if (slugs != null)
                    campaign = await _context.Campaigns.FirstOrDefaultAsync(x => x.Id == slugs.ReferenceId);
            }

            if (campaign == null)
                return NotFound();

            switch (campaign.Stage)
            {
                case InvestmentStage.ClosedInvested:
                case InvestmentStage.CompletedOngoing:
                case InvestmentStage.CompletedOngoingPrivate:
                    break;

                case InvestmentStage.Vetting:
                case InvestmentStage.New:
                    return NotFound();

                case InvestmentStage.ClosedNotInvested:
                    return Ok(new { Success = false, Message = "This investment has been closed." });

                default:
                    if (campaign.IsActive == false)
                    {
                        return NotFound();
                    }
                    break;
            }

            var campaignResponse = _mapper.Map<Campaign>(campaign);

            var siteConfigs = await _context.SiteConfiguration.Where(x => x.Type == SiteConfigurationType.StaticValue).ToDictionaryAsync(x => x.Key, x => x.Value);

            campaignResponse.Terms = ReplaceSiteConfigTokens(campaignResponse.Terms!, siteConfigs);

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

            campaignResponse.NumberOfInvestors = 0;

            if (reccomendations != null)
            {
                campaignResponse.CurrentBalance = reccomendations.CurrentBalance;
                campaignResponse.NumberOfInvestors = reccomendations.NumberOfInvestors;
            }

            campaignResponse.CurrentBalance = campaignResponse.CurrentBalance ?? 0;
            campaignResponse.AddedTotalAdminRaised = campaignResponse.AddedTotalAdminRaised ?? 0;

            if (campaign.Stage == InvestmentStage.ClosedInvested || campaign.Stage == InvestmentStage.CompletedOngoing || campaign.Stage == InvestmentStage.CompletedOngoingPrivate)
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
                                                        AddedTotalAdminRaised = c.AddedTotalAdminRaised ?? 0,

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
        [ModuleAuthorize(PermissionType.View)]
        public async Task<ActionResult<Campaign>> GetAdminCampaign(int id)
        {
            if (_context.Campaigns == null)
                return NotFound();
            
            var campaignDto = await _context.Campaigns.Include(x => x.GroupForPrivateAccess).FirstOrDefaultAsync(x => x.Id == id);

            if (campaignDto == null)
                return NotFound();

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

        [HttpPut("{id}/status")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<ActionResult<CampaignDto>> UpdateStatus(int id, bool status)
        {
            var campaign = await _context.Campaigns.SingleOrDefaultAsync(item => item.Id == id);
            if (campaign == null)
                return BadRequest();

            campaign.IsActive = status;
            campaign.ModifiedDate = DateTime.Now;
            await _context.SaveChangesAsync();
            var data = _mapper.Map<CampaignDto>(campaign);

            if (_appSecrets.IsProduction && status)
            {
                var variables = new Dictionary<string, string>
                {
                    { "date", DateTime.Now.ToString("MM/dd/yyyy") },
                    { "investmentLink", $"{_appSecrets.RequestOrigin}/investments/{campaign.Property}" },
                    { "campaignName", campaign.Name! }
                };

                _emailQueue.QueueEmail(async (sp) =>
                {
                    var emailService = sp.GetRequiredService<IEmailTemplateService>();

                    await emailService.SendTemplateEmailAsync(
                        EmailTemplateCategory.InvestmentApproved,
                        "test@gmail.com",
                        variables
                    );
                });
            }

            return Ok(data);
        }

        [HttpPut("{id}")]
        [DisableRequestSizeLimit]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<ActionResult<Campaign>> PutCampaign([FromBody] Campaign campaign)
        {
            if (campaign!.Id == null)
                return BadRequest();

            if (campaign.Property != null)
            {
                bool existsInSlug = await _context.Slug
                                            .AnyAsync(x =>
                                                x.Type == SlugType.Investment &&
                                                x.ReferenceId != campaign.Id &&
                                                x.Value == campaign.Property);

                bool existsInCampaign = await _context.Campaigns
                                                    .AnyAsync(x =>
                                                        x.Property != null &&
                                                        x.Id != campaign.Id &&
                                                        x.Property.ToLower().Trim() == campaign.Property);

                if (existsInCampaign || existsInSlug)
                    return Ok(new { Success = false, Message = "Investment name for URL already exists." });
            }

            var uploadedFiles = await UploadCampaignFiles(campaign);
            campaign.PdfFileName = uploadedFiles.GetValueOrDefault("PDFPresentation", campaign.PdfFileName);
            campaign.ImageFileName = uploadedFiles.GetValueOrDefault("Image", campaign.ImageFileName);
            campaign.TileImageFileName = uploadedFiles.GetValueOrDefault("TileImage", campaign.TileImageFileName);
            campaign.LogoFileName = uploadedFiles.GetValueOrDefault("Logo", campaign.LogoFileName);

            var existingCampaign = await _context.Campaigns.FirstOrDefaultAsync(x => x.Id == campaign.Id);

            if (!string.IsNullOrWhiteSpace(existingCampaign!.Property))
            {
                var currentSlug = await _context.Slug
                                                .FirstOrDefaultAsync(x =>
                                                    x.Value != null &&
                                                    x.Type == SlugType.Investment &&
                                                    x.Value == existingCampaign!.Property);

                if (currentSlug == null)
                {
                    await _context.Slug.AddAsync(new Slug
                    {
                        ReferenceId = campaign.Id.Value,
                        Type = SlugType.Investment,
                        Value = existingCampaign!.Property,
                        CreatedAt = DateTime.Now
                    });

                    await _context.SaveChangesAsync();
                }
            }

            campaign.CreatedDate = existingCampaign?.CreatedDate;
            campaign.ModifiedDate = DateTime.Now;

            var identity = HttpContext.User.Identity as ClaimsIdentity;
            var role = identity?.Claims.FirstOrDefault(i => i.Type == ClaimTypes.Role)?.Value;

            if (role == UserRoles.User)
                PreserveAdminFields(existingCampaign!, campaign);

            var campaignDto = _mapper.Map(campaign, existingCampaign);

            if (role != UserRoles.User)
            {
                campaignDto!.GroupForPrivateAccess = campaign.GroupForPrivateAccessDto != null
                                                        ? _mapper.Map<Group>(campaign.GroupForPrivateAccessDto)
                                                        : null;
                campaignDto.GroupForPrivateAccessId = campaign.GroupForPrivateAccessDto != null
                                                        ? campaign.GroupForPrivateAccessDto.Id
                                                        : null;
            }

            _context.Entry(existingCampaign!).State = EntityState.Modified;

            _ = SendUpdateCampaignEmails(existingCampaign!, campaign);

            return Ok(new { Success = true, Message = "Campaign details updated successfully", campaign });
        }

        [HttpPost("raisemoney")]
        [DisableRequestSizeLimit]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> PostRaiseMoneyCampaign([FromBody] Campaign campaign)
        {
            if (campaign == null)
                return Ok(new { Success = false, Message = "Campaign data is required." });

            if (campaign.ContactInfoEmailAddress == null)
                return Ok(new { Success = false, Message = "Email is required." });

            if (string.IsNullOrWhiteSpace(campaign.FirstName))
                return Ok(new { Success = false, Message = "First Name is required." });

            if (string.IsNullOrWhiteSpace(campaign.LastName))
                return Ok(new { Success = false, Message = "Last Name is required." });

            if (!string.IsNullOrEmpty(campaign.CaptchaToken) && !await VerifyCaptcha(campaign.CaptchaToken))
                return BadRequest("CAPTCHA verification failed.");

            var userEmail = campaign!.ContactInfoEmailAddress!.Trim().ToLower();

            var user = await _context.Users.SingleOrDefaultAsync(u => u.Email.ToLower() == userEmail)
                        ?? await RegisterAnonymousUser(campaign.FirstName!, campaign.LastName!, campaign.ContactInfoEmailAddress.Trim().ToLower()!);

            var uploadedFiles = await UploadCampaignFiles(campaign);
            campaign.PdfFileName = uploadedFiles.GetValueOrDefault("PDFPresentation", campaign.PdfFileName);
            campaign.ImageFileName = uploadedFiles.GetValueOrDefault("Image", campaign.ImageFileName);
            campaign.TileImageFileName = uploadedFiles.GetValueOrDefault("TileImage", campaign.TileImageFileName);
            campaign.LogoFileName = uploadedFiles.GetValueOrDefault("Logo", campaign.LogoFileName);

            var mappedCampaign = _mapper.Map<Campaign, CampaignDto>(campaign!);
            mappedCampaign.Status = "0";
            mappedCampaign.Stage = InvestmentStage.New;
            mappedCampaign.IsActive = false;
            mappedCampaign.PdfFileName = campaign.PdfFileName;
            mappedCampaign.ImageFileName = campaign.ImageFileName;
            mappedCampaign.TileImageFileName = campaign.TileImageFileName;
            mappedCampaign.LogoFileName = campaign.LogoFileName;
            mappedCampaign.CreatedDate = DateTime.Now;
            mappedCampaign.EmailSends = false;
            mappedCampaign.UserId = user != null ? user.Id : null;

            if (campaign.GroupForPrivateAccessDto != null)
                mappedCampaign.GroupForPrivateAccess = await _context.Groups.FirstOrDefaultAsync(i => i.Id == campaign.GroupForPrivateAccessDto.Id);

            _context.Campaigns.Add(mappedCampaign);
            await _context.SaveChangesAsync();

            _ = SendCreateCampaignEmails(campaign, mappedCampaign, userEmail);

            return Ok(new { Success = true, Message = "Investment has been created successfully." });
        }

        [HttpGet("send-investment-qr-code-email")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> SendInvestmentQRCodeEmail(int id, string investmentTag)
        {
            var investment = await _context.Campaigns.FirstOrDefaultAsync(x => x.Id == id);

            if (string.IsNullOrWhiteSpace(investment?.ContactInfoEmailAddress))
                return Ok(new { Success = false, Message = "You can’t send QR by email because your organizational email isn’t set up yet" });

            string? investmentUrl = !string.IsNullOrEmpty(investmentTag)
                                    ? investmentTag
                                    : !string.IsNullOrEmpty(investment.Property)
                                        ? $"{requestOrigin}/investments/{Uri.EscapeDataString(investment.Property)}"
                                        : null;

            if (string.IsNullOrWhiteSpace(investmentUrl))
                return Ok(new { Success = false, Message = "Failed to send email because investment URL is missing." });

            string fullName = investment.ContactInfoFullName ?? string.Empty;
            string[] parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string firstName = parts.Length > 0 ? parts[0] : string.Empty;

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(investmentUrl, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrBytes = qrCode.GetGraphic(20);

            var qrAttachment = new EmailAttachment(
                name: $"{investment.Name}.png",
                contentType: "image/png",
                content: BinaryData.FromBytes(qrBytes)
            );

            var variables = new Dictionary<string, string>
            {
                { "firstName", firstName },
                { "investmentName", investment.Name! },
                { "unsubscribeUrl", $"{_appSecrets.RequestOrigin}/settings" }
            };

            _emailQueue.QueueEmail(async (sp) =>
            {
                var emailService = sp.GetRequiredService<IEmailTemplateService>();

                await emailService.SendTemplateEmailAsync(
                    EmailTemplateCategory.InvestmentQRCode,
                    investment.ContactInfoEmailAddress!.Trim().ToLower(),
                    variables,
                    attachments: new List<EmailAttachment> { qrAttachment }
                );
            });

            return Ok(new { Success = true, Message = "Email sent successfully." });
        }

        [HttpDelete("{id}")]
        [ModuleAuthorize(PermissionType.Delete)]
        public async Task<IActionResult> DeleteCampaign(int id)
        {
            var campaign = await _context.Campaigns.FirstOrDefaultAsync(x => x.Id == id);
            if (campaign == null)
                return NotFound();

            var recommendations = _context.Recommendations.Where(r => r.CampaignId == id);
            _context.Recommendations.RemoveRange(recommendations);

            var accountBalanceChangeLogs = _context.AccountBalanceChangeLogs.Where(r => r.CampaignId == id);
            _context.AccountBalanceChangeLogs.RemoveRange(accountBalanceChangeLogs);

            _context.Campaigns.Remove(campaign);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("portfolio")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<ActionResult<PortfolioDto>> GetPortfolio()
        {
            var identity = HttpContext.User.Identity as ClaimsIdentity;

            if (_context.Users == null || identity == null)
                return NotFound();

            var email = identity.Claims.FirstOrDefault(i => i.Type == ClaimTypes.Email)?.Value;

            if (email == null || email == string.Empty)
                return NotFound();

            var portfolio = new PortfolioDto();

            var user = await _context.Users.FirstOrDefaultAsync(i => i.Email == email);

            if (user == null)
                return NotFound();

            portfolio.AccountBalance = user?.AccountBalance;

            var groupAccountBalance = await _context.GroupAccountBalance
                                                    .Where(g => g.User != null
                                                            && g.User.Id == user!.Id)
                                                    .Select(g => (decimal?)g.Balance)
                                                    .SumAsync();

            if (groupAccountBalance != null)
                portfolio.GroupBalance = groupAccountBalance;

            var userRecommendations = await _context.Recommendations
                                                    .Where(i => i.UserEmail == email
                                                            && (i.Status == "approved" || i.Status == "pending"))
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
                                    .Where(i => i.CampaignId != null)
                                    .Select(i => i.CampaignId)
                                    .ToList(); ;

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
                portfolio.Campaigns = userCampaigns.Where(c => c.Stage != InvestmentStage.ClosedNotInvested).ToList();
            }

            return portfolio;
        }

        [HttpGet("export")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> ExportCampaigns()
        {
            var campaigns = await _context.Campaigns
                                            .Include(c => c.Groups)
                                            .Include(c => c.Recommendations)
                                            .ToListAsync();

            var campaignDtos = campaigns.Select(c => new ExportCampaignDto
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
                Country = c.Country,
                OtherCountryAddress = c.OtherCountryAddress,
                City = c.City,
                State = c.State,
                ZipCode = c.ZipCode,
                NetworkDescription = c.NetworkDescription,
                ImpactAssetsFundingStatus = c.ImpactAssetsFundingStatus,
                InvestmentRole = c.InvestmentRole,
                ReferredToCataCap = c.Referred,
                Target = c.Target,
                Status = c.Status,
                TileImageFileName = c.TileImageFileName,
                ImageFileName = c.ImageFileName,
                PdfFileName = c.PdfFileName,
                OriginalPdfFileName = c.OriginalPdfFileName,
                LogoFileName = c.LogoFileName,
                IsActive = c.IsActive,
                IsPartOfFund = c.IsPartOfFund,
                AssociatedFundId = c.AssociatedFundId,
                FeaturedInvestment = c.FeaturedInvestment,
                Stage = c.Stage,
                InvestmentTag = "",
                Property = c.Property,
                AddedTotalAdminRaised = c.AddedTotalAdminRaised,
                Groups = c.Groups.ToList(),
                Recommendations = c.Recommendations,
                GroupForPrivateAccess = c.GroupForPrivateAccess,
                EmailSends = c.EmailSends,
                FundraisingCloseDate = c.FundraisingCloseDate,
                MissionAndVision = c.MissionAndVision,
                PersonalizedThankYou = c.PersonalizedThankYou,
                ExpectedTotal = c.ExpectedTotal,
                InvestmentTypeCategory = c.InvestmentTypeCategory,
                EquityValuation = c.EquityValuation,
                EquitySecurityType = c.EquitySecurityType,
                FundTerm = c.FundTerm,
                EquityTargetReturn = c.EquityTargetReturn,
                DebtPaymentFrequency = c.DebtPaymentFrequency,
                DebtMaturityDate = c.DebtMaturityDate,
                DebtInterestRate = c.DebtInterestRate,
                CreatedDate = c.CreatedDate,
                MetaTitle = c.MetaTitle,
                MetaDescription = c.MetaDescription
            }).ToList();

            string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            string fileName = "Investments.xlsx";

            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet worksheet = workbook.Worksheets.Add("Campaigns");

                string[] headers = new string[]
                {
                    "Id", "Name", "Description", "Themes", "Approved By", "SDGs", "Type of Investment",
                    "Terms", "Minimum Investment", "Website", "Contact Info FullName", "Contact Info Address1", "Contact Info Address2",
                    "Investment Owner email", "Investment Informational Email", "Contact Info Phone Number", "Country", "Other Country Address", "City", "State", "ZipCode", "Tell us a bit about your network", "ImpactAssetsFundingStatus",
                    "InvestmentRole", "How where you referred to CataCap?", "Target", "Status", "Tile Image File Name", "Image File Name", "Pdf File Name", "Original Pdf File Name",
                    "Logo File Name", "Is Active", "Is Part Of Fund", "Associated Fund", "Featured Investment", "Stage", "Property", "Added Total Admin Raised",
                    "Groups", "Total Recommendations","Total Investors", "Group For Private Access", "Email Sends", "Expected Fundraising Close Date",
                    "Mission/Vision", "Personalized Thank You", "How much money do you already have in commitments for your investment",
                    "Investment Type", "Equity / Valuation", "Equity / Security Type", "Fund / Term", "Equity / Funds Target Return",
                    "Debt / Payment Frequency", "Debt / Maturity Date", "Debt / Interest Rate", "Created Date", "Meta Title", "Meta Description"
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
                    worksheet.Cell(row, col++).Value = dto.Country;
                    worksheet.Cell(row, col++).Value = dto.OtherCountryAddress;
                    worksheet.Cell(row, col++).Value = dto.City;
                    worksheet.Cell(row, col++).Value = dto.State;
                    worksheet.Cell(row, col++).Value = dto.ZipCode;
                    worksheet.Cell(row, col++).Value = dto.NetworkDescription;
                    worksheet.Cell(row, col++).Value = dto.ImpactAssetsFundingStatus;
                    worksheet.Cell(row, col++).Value = dto.InvestmentRole;
                    worksheet.Cell(row, col++).Value = dto.ReferredToCataCap;
                    worksheet.Cell(row, col++).Value = dto.Target;
                    worksheet.Cell(row, col++).Value = dto.Status;
                    worksheet.Cell(row, col++).Value = dto.TileImageFileName;
                    worksheet.Cell(row, col++).Value = dto.ImageFileName;
                    worksheet.Cell(row, col++).Value = dto.PdfFileName;
                    worksheet.Cell(row, col++).Value = dto.OriginalPdfFileName;
                    worksheet.Cell(row, col++).Value = dto.LogoFileName;
                    worksheet.Cell(row, col++).Value = dto.IsActive.HasValue && dto.IsActive.Value ? "Active" : "Inactive";
                    worksheet.Cell(row, col++).Value = dto.IsPartOfFund ? "Yes" : "No";

                    var campaign = await _context.Campaigns.FirstOrDefaultAsync(x => x.Id == dto.AssociatedFundId);
                    worksheet.Cell(row, col++).Value = dto.IsPartOfFund ? campaign?.Name : null;

                    worksheet.Cell(row, col++).Value = dto.FeaturedInvestment ? "Yes" : "No";

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

                    var expectedTotalCell = worksheet.Cell(row, col++);
                    expectedTotalCell.Value = dto.ExpectedTotal;
                    expectedTotalCell.Style.NumberFormat.Format = "$#,##0.00";

                    worksheet.Cell(row, col++).Value = dto.InvestmentTypeCategory;

                    var equityValuationCell = worksheet.Cell(row, col++);
                    equityValuationCell.Value = dto.EquityValuation;
                    equityValuationCell.Style.NumberFormat.Format = "$#,##0.00";

                    worksheet.Cell(row, col++).Value = dto.EquitySecurityType;
                    worksheet.Cell(row, col++).Value = dto.FundTerm?.ToString("MM-dd-yyyy");
                    worksheet.Cell(row, col++).Value = dto.EquityTargetReturn;
                    worksheet.Cell(row, col++).Value = dto.DebtPaymentFrequency;
                    worksheet.Cell(row, col++).Value = dto.DebtMaturityDate?.ToString("MM-dd-yyyy");
                    worksheet.Cell(row, col++).Value = dto.DebtInterestRate;
                    worksheet.Cell(row, col++).Value = dto.CreatedDate?.ToString("MM-dd-yyyy");

                    worksheet.Cell(row, col++).Value = dto.MetaTitle;
                    worksheet.Cell(row, col++).Value = dto.MetaDescription;
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

        [HttpGet("document")]
        [ModuleAuthorize(PermissionType.View)]
        public IActionResult Document(string action, string pdfFileName, string? originalPdfFileName = null)
        {
            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(pdfFileName))
                return Ok(new { Success = false, Message = "Parameters required." });

            BlockBlobClient blobClient = _blobContainerClient.GetBlockBlobClient(pdfFileName);
            var expiryTime = DateTimeOffset.UtcNow.AddMinutes(5);
            string? sasUri = null;

            switch (action)
            {
                case "open":
                    sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, expiryTime).ToString();
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

                    //var sasToken = sasBuilder.ToSasQueryParameters(new StorageSharedKeyCredential(blobClient.AccountName, "storage-secret-key")).ToString();
                    var sasToken = "";

                    var uriBuilder = new UriBuilder(blobClient.Uri)
                    {
                        Query = sasToken
                    };
                    sasUri = uriBuilder.Uri.ToString();
                    break;
            }

            if (sasUri == null)
                return BadRequest(new { Success = false, Message = "Failed to load document." });

            return Ok(new { Success = true, Message = sasUri });
        }

        [HttpPost("{id}/clone")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> Clone(int id, string name)
        {
            name = name.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                bool nameExists = await _context.Campaigns.AnyAsync(x => x.Name!.Trim() == name);

                if (nameExists)
                    return Ok(new { Success = false, Message = "Campaign name already exists." });
            }

            var campaign = await _context.Campaigns.FirstOrDefaultAsync(x => x.Id == id);
            if (campaign == null)
                return Ok(new { Success = false, Message = "Campaign not found." });

            var property = name?.ToLower();
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
                Name = name,
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
                Country = campaign?.Country,
                OtherCountryAddress = campaign?.OtherCountryAddress,
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

            return Ok(new { Success = true, Message = "Investment cloned successfully." });
        }

        [HttpGet("themes")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetAllThemesList()
        {
            try
            {
                var investmentThemes = await _context.Themes
                                                    .Select(x => new { x.Id, x.Name, x.Mandatory })
                                                    .OrderBy(x => x.Name)
                                                    .ToListAsync();

                if (investmentThemes != null)
                    return Ok(investmentThemes);

                return BadRequest(new { Success = false, Message = "No investment themes found." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Success = false, Message = $"An error occurred: {ex.Message}" });
            }
        }

        [HttpGet("names")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetNames(int stage, int id)
        {
            var investmentTypes = await _context.InvestmentTypes.ToListAsync();

            if (stage == 4)
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
            else if (stage == 3)
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
            else if (stage == 0)
            {
                var campaignList = await _context.Campaigns
                                                    .Where(x => x.Name!.Trim() != string.Empty && x.Id != id)
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
            else if (stage == 10)
            {
                var campaignList = await _context.Campaigns
                                                .Where(x => (x.Stage == InvestmentStage.ClosedInvested
                                                                || x.Stage == InvestmentStage.CompletedOngoing
                                                                || x.Stage == InvestmentStage.CompletedOngoingPrivate)
                                                                && x.Name!.Trim() != string.Empty)
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

        [HttpGet("types")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetTypes()
        {
            var investmentTypes = await _context.InvestmentTypes
                                        .Select(i => new { i.Id, i.Name })
                                        .OrderBy(i => i.Name)
                                        .ToListAsync();

            investmentTypes.Add(new { Id = -1, Name = (string?)"Other" });

            if (investmentTypes != null)
                return Ok(investmentTypes);

            return BadRequest(new { Success = false, Message = "Invalid investment stage." });
        }

        [HttpGet("completed-investments")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetDetails([FromQuery] CompletedInvestmentsRequestDto requestDto)
        {
            if (requestDto.InvestmentId <= 0)
                return Ok(new { Success = false, Message = "InvestmentId is required." });

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
                PendingRecommendationsAmount = totalPendingInvestmentAmount,
                InvestmentVehicle = requestDto.InvestmentVehicle
            };

            if (responseDto != null)
                return Ok(responseDto);

            return Ok(new { Success = false, Message = "No records found for the selected investment." });
        }

        [HttpPost("completed-investments")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> SaveOrUpdate([FromBody] CompletedInvestmentsRequestDto requestDto)
        {
            if (requestDto.InvestmentId <= 0)
                return Ok(new { Success = false, Message = "InvestmentId is required." });

            if (requestDto.TotalInvestmentAmount <= 0)
                return Ok(new { Success = false, Message = "Amount must be greater than zero." });

            if (string.IsNullOrEmpty(requestDto.InvestmentDetail))
                return Ok(new { Success = false, Message = "Investment detail is required." });

            if (requestDto.DateOfLastInvestment == null)
                return Ok(new { Success = false, Message = "Last investment date is required." });

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
                    Name = requestDto.TypeOfInvestmentName.Trim()
                };

                _context.InvestmentTypes.Add(investmentType);
                await _context.SaveChangesAsync();

                investmentTypeIds.Add(investmentType.Id.ToString());
            }

            var updatedTypeIds = string.Join(",", investmentTypeIds);

            var campaign = await _context.Campaigns.Where(x => x.Id == requestDto.InvestmentId).FirstOrDefaultAsync();

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

            var updatedTypeOfInvestmentIds = string.Join(",", investmentTypeIds);

            if (requestDto.Id == null || requestDto.Id == 0)
            {
                var entity = new CompletedInvestmentsDetails
                {
                    CampaignId = requestDto.InvestmentId,
                    InvestmentDetail = requestDto.InvestmentDetail,
                    Amount = requestDto.TotalInvestmentAmount,
                    DateOfLastInvestment = requestDto.DateOfLastInvestment,
                    TypeOfInvestment = updatedTypeIds,
                    Donors = totalInvestors,
                    Themes = campaign?.Themes,
                    InvestmentVehicle = !string.IsNullOrWhiteSpace(requestDto.InvestmentVehicle) ? requestDto.InvestmentVehicle : null,
                    CreatedBy = userId!,
                    CreatedOn = DateTime.Now
                };

                await _context.CompletedInvestmentsDetails.AddAsync(entity);
                await _context.SaveChangesAsync();

                return Ok(new { Success = true, Message = "Investment details saved successfully." });
            }

            var existing = await _context.CompletedInvestmentsDetails.FirstOrDefaultAsync(x => x.Id == requestDto.Id);

            if (existing == null)
                return Ok(new { Success = false, Message = "Record not found." });

            decimal oldAmount = existing.Amount ?? 0m;

            existing.InvestmentDetail = requestDto.InvestmentDetail;
            existing.Amount = requestDto.TotalInvestmentAmount;
            existing.DateOfLastInvestment = requestDto.DateOfLastInvestment;
            existing.TypeOfInvestment = updatedTypeIds;
            existing.SiteConfigurationId = requestDto.TransactionTypeId;
            existing.InvestmentVehicle = requestDto.InvestmentVehicle;
            existing.ModifiedOn = DateTime.Now;
            await _context.SaveChangesAsync();

            return Ok(new { Success = true, Message = "Investment details updated successfully." });
        }

        [HttpGet("completed-investments-history")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> Get([FromQuery] CompletedInvestmentsPaginationDto requestDto)
        {
            var selectedThemeIds = ParseCommaSeparatedIds(requestDto!.ThemesId);
            var selectedInvestmentTypeIds = ParseCommaSeparatedIds(requestDto.InvestmentTypeId);
            bool? isDeleted = requestDto.IsDeleted;

            var themes = await _context.Themes.ToListAsync();
            var investmentTypes = await _context.InvestmentTypes.ToListAsync();

            IQueryable<CompletedInvestmentsDetails> query = _context.CompletedInvestmentsDetails
                                                                    .Include(x => x.Campaign)
                                                                    .Include(x => x.SiteConfiguration)
                                                                    .Include(x => x.DeletedByUser)
                                                                    .ApplySoftDeleteFilter(isDeleted);

            var completedDetails = await query.ToListAsync();

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

            var userEmails = usersQuery.Select(u => u.Email);

            var recommendations = await _context.Recommendations
                                                .Where(r =>
                                                        userEmails.Contains(r.UserEmail!) &&
                                                        r != null &&
                                                        r.Campaign != null &&
                                                        (r.Status!.ToLower() == "approved"
                                                            || r.Status.ToLower() == "pending")
                                                            && r.Amount > 0 &&
                                                        !string.IsNullOrWhiteSpace(r.UserEmail))
                                                .ToListAsync();

            var totalInvestors = completedDetails.Select(x => x.Donors).Sum();
            var totalInvestmentAmount = recommendations?.Sum(r => r.Amount ?? 0) ?? 0;

            int completedCount = completedDetails.Count;

            string lastCompletedDate = completedDetails
                                        .Where(x => x.DateOfLastInvestment.HasValue)
                                        .OrderByDescending(x => x.DateOfLastInvestment!.Value)
                                        .Select(x => DateOnly.FromDateTime(x.DateOfLastInvestment!.Value).ToString("MM/dd/yyyy"))
                                        .FirstOrDefault() ?? string.Empty;

            var campaignIds = completedDetails.Select(c => c.CampaignId).ToList();

            var recStats = await _context.Recommendations
                                            .Where(r =>
                                                userEmails.Contains(r.UserEmail!) &&
                                                campaignIds.Contains(r.CampaignId!.Value) &&
                                                (r.Status == "approved" || r.Status == "pending") &&
                                                r.Amount > 0 &&
                                                r.UserEmail != null)
                                            .GroupBy(r => r.CampaignId)
                                            .Select(g => new
                                            {
                                                CampaignId = g.Key!.Value,
                                                CurrentBalance = g.Sum(x => x.Amount ?? 0),
                                                NumberOfInvestors = g.Select(x => x.UserEmail!.ToLower()).Distinct().Count()
                                            })
                                            .ToDictionaryAsync(x => x.CampaignId);

            var avatars = await _context.Recommendations
                                        .Where(r =>
                                            campaignIds.Contains(r.CampaignId!.Value) &&
                                            (r.Status == "approved" || r.Status == "pending"))
                                        .Join(_context.Users,
                                                r => r.UserEmail,
                                                u => u.Email,
                                                (r, u) => new
                                                {
                                                    r.CampaignId,
                                                    u.PictureFileName,
                                                    u.ConsentToShowAvatar,
                                                    r.Id
                                                })
                                        .Where(x => x.PictureFileName != null && x.ConsentToShowAvatar)
                                        .ToListAsync();

            var avatarLookup = avatars
                                .GroupBy(x => x.CampaignId!.Value)
                                .ToDictionary(
                                    g => g.Key,
                                    g => g.OrderByDescending(x => x.Id)
                                            .Select(x => x.PictureFileName)
                                            .Distinct()
                                            .Take(3)
                                            .ToList()
                                );

            dynamic response = new ExpandoObject();

            var completedInvestmentsHistory = completedDetails
                                                .Select(x =>
                                                {
                                                    var campaign = x.Campaign;

                                                    var themeIds = ParseCommaSeparatedIds(campaign?.Themes);
                                                    var invTypeIds = ParseCommaSeparatedIds(x.TypeOfInvestment);

                                                    var themeNames = themes
                                                                        .Where(t => themeIds.Contains(t.Id))
                                                                        .OrderBy(t => t.Name)
                                                                        .Select(t => t.Name)
                                                                        .ToList();

                                                    var investmentTypesNames = investmentTypes
                                                                                .Where(i => invTypeIds.Contains(i.Id))
                                                                                .OrderBy(i => i.Name)
                                                                                .Select(i => i.Name)
                                                                                .ToList();

                                                    var dto = new CompletedInvestmentsHistoryResponseDto
                                                    {
                                                        Id = x.Id,
                                                        DateOfLastInvestment = x.DateOfLastInvestment,
                                                        Name = campaign?.Name,
                                                        CataCapFund = _context.Campaigns
                                                                                .Where(c => c.Id == campaign!.AssociatedFundId)
                                                                                .Select(c => c.Name)
                                                                                .FirstOrDefault(),
                                                        TileImageFileName = campaign!.TileImageFileName,
                                                        Description = campaign.Description,
                                                        Target = campaign.Target,
                                                        InvestmentDetail = x.InvestmentDetail,
                                                        TransactionType = x.SiteConfiguration?.Id,
                                                        Stage = (campaign.Stage?.GetType()
                                                                .GetField(campaign.Stage.ToString()!)?
                                                                .GetCustomAttributes(typeof(DescriptionAttribute), false)?
                                                                .FirstOrDefault() as DescriptionAttribute)?.Description
                                                                ?? campaign.Stage.ToString(),
                                                        TotalInvestmentAmount = Math.Round(x.Amount ?? 0, 0),
                                                        TypeOfInvestment = string.Join(", ", investmentTypesNames),
                                                        Donors = x.Donors,
                                                        Property = campaign.Property,
                                                        Themes = string.Join(", ", themeNames),
                                                        InvestmentVehicle = x.InvestmentVehicle,
                                                        ApprovedRecommendationsAmount = _context.Recommendations
                                                                                                .Where(r =>
                                                                                                    r.CampaignId == campaign.Id &&
                                                                                                    r.Status!.ToLower() == "approved" &&
                                                                                                    r.Amount > 0)
                                                                                                .Sum(r => r.Amount ?? 0),
                                                        LatestInvestorAvatars = avatarLookup.ContainsKey(campaign.Id!.Value)
                                                                                ? avatarLookup[campaign.Id.Value]!
                                                                                : new List<string>(),
                                                        DeletedAt = x.DeletedAt,
                                                        DeletedBy = x.DeletedByUser != null
                                                                    ? $"{x.DeletedByUser.FirstName} {x.DeletedByUser.LastName}"
                                                                    : null,
                                                    };

                                                    if (recStats.TryGetValue(campaign.Id!.Value, out var stats))
                                                    {
                                                        dto.CurrentBalance = stats.CurrentBalance + (campaign.AddedTotalAdminRaised ?? 0);
                                                        dto.NumberOfInvestors = stats.NumberOfInvestors;
                                                    }

                                                    return new
                                                    {
                                                        CreatedOn = x.CreatedOn,
                                                        ThemeIds = themeIds,
                                                        InvestmentTypeIds = invTypeIds,
                                                        Dto = dto
                                                    };
                                                })
                                                .Where(x =>
                                                    (selectedThemeIds?.Count == 0 || x.ThemeIds.Any(id => selectedThemeIds!.Contains(id))) &&
                                                    (selectedInvestmentTypeIds?.Count == 0 || x.InvestmentTypeIds.Any(id => selectedInvestmentTypeIds!.Contains(id))) &&
                                                    (string.IsNullOrEmpty(requestDto.SearchValue) ||
                                                        (!string.IsNullOrEmpty(x.Dto.Name) && x.Dto.Name.Contains(requestDto.SearchValue, StringComparison.OrdinalIgnoreCase)) ||
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

                "fund" => isAsc
                    ? completedInvestmentsHistory.OrderBy(x => x.Dto.CataCapFund).ToList()
                    : completedInvestmentsHistory.OrderByDescending(x => x.Dto.CataCapFund).ToList(),

                "investmentname" => isAsc
                    ? completedInvestmentsHistory.OrderBy(x => x.Dto.Name).ToList()
                    : completedInvestmentsHistory.OrderByDescending(x => x.Dto.Name).ToList(),

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

                _ => completedInvestmentsHistory.OrderByDescending(x => x.CreatedOn).ThenBy(x => x.Dto.Name).ToList()
            };

            response.totalCount = completedInvestmentsHistory.Count;

            int currentPage = requestDto?.CurrentPage.GetValueOrDefault() ?? 0;
            int perPage = requestDto?.PerPage.GetValueOrDefault() ?? 0;

            bool hasPagination = currentPage > 0 && perPage > 0;

            if (hasPagination)
                response.items = completedInvestmentsHistory
                                .Skip((currentPage - 1) * perPage)
                                .Take(perPage)
                                .Select(x => x.Dto)
                                .ToList();
            else
                response.items = completedInvestmentsHistory.Select(x => x.Dto).ToList();

            response.completedInvestments = completedCount;
            response.totalInvestmentAmount = Math.Round(totalInvestmentAmount, 0);
            response.totalInvestors = totalInvestors;
            response.lastCompletedInvestmentsDate = lastCompletedDate;

            if (response.totalCount == 0)
                response.message = "No records found for completed investments.";

            return Ok(response);
        }

        [HttpGet("export-completed-investments")]
        [ModuleAuthorize(PermissionType.View)]
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
                                                    Name = x.Campaign?.Name,
                                                    InvestmentDetail = x.InvestmentDetail,
                                                    TotalInvestmentAmount = x.Amount,
                                                    TypeOfInvestment = string.Join(", ", investmentTypesNames),
                                                    Donors = x.Donors,
                                                    Themes = string.Join(", ", themeNames)
                                                }
                                            };
                                        })
                                        .OrderByDescending(x => x.CreatedOn)
                                        .ThenBy(x => x.Dto.Name)
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
                    worksheet.Cell(row, 2).Value = dto.Name;
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
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> CalculateReturns([FromBody] ReturnCalculationRequestDto requestDto)
        {
            try
            {
                if (requestDto.InvestmentId <= 0)
                    return Ok(new { Success = false, Message = "InvestmentId is required." });
                if (requestDto.ReturnAmount <= 0)
                    return Ok(new { Success = false, Message = "Return amount must be greater than zero." });

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
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> SaveReturns([FromBody] ReturnCalculationRequestDto requestDto)
        {
            try
            {
                if (requestDto.InvestmentId <= 0)
                    return Ok(new { Success = false, Message = "InvestmentId is required." });
                if (requestDto.ReturnAmount <= 0)
                    return Ok(new { Success = false, Message = "Return amount must be greater than zero." });
                if (string.IsNullOrEmpty(requestDto.MemoNote))
                    return Ok(new { Success = false, Message = "Admin memo is required." });

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

        [HttpGet("returns-history")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetReturnsHistory([FromQuery] ReturnsHistoryRequestDto requestDto)
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
                    query = query?.Where(x => x.CampaignId == requestDto.InvestmentId);

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
        [ModuleAuthorize(PermissionType.View)]
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

        [HttpGet("check-missing-investment-urls")]
        [ModuleAuthorize(PermissionType.View)]
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

        [HttpGet("list-of-pdf-not-exist-on-azure")]
        [ModuleAuthorize(PermissionType.View)]
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
                        missingCampaigns.Add(campaign?.Name!);
                }

                return Ok(new { MissingFilesCampaignName = missingCampaigns });
            }
            catch (Exception ex)
            {
                return BadRequest(ex);
            }
        }

        [HttpGet("download-all-files-from-container")]
        [ModuleAuthorize(PermissionType.View)]
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

        private static List<int> ParseCommaSeparatedIds(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<int>();

            return input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => int.TryParse(id.Trim(), out var val) ? val : (int?)null)
                        .Where(id => id.HasValue)
                        .Select(id => id!.Value)
                        .ToList();
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

        private async Task<IEnumerable<CampaignCardDtov2>> GetTrendingCampaignsCardDto()
        {
            var campaigns = await _context.Campaigns
                                      .Where(i => i.IsActive!.Value &&
                                                  (i.Stage == InvestmentStage.Public ||
                                                  i.Stage == InvestmentStage.CompletedOngoing) &&
                                                  i.GroupForPrivateAccessId == null)
                                      .ToListAsync();

            var campaignIds = campaigns.Select(c => c.Id).ToList();

            var recStats = await _context.Recommendations
                                         .Where(r =>
                                             campaignIds.Contains(r.CampaignId!.Value) &&
                                             (r.Status == "approved" || r.Status == "pending") &&
                                             r.Amount > 0 &&
                                             r.UserEmail != null)
                                         .GroupBy(r => r.CampaignId)
                                         .Select(g => new
                                         {
                                             CampaignId = g.Key!.Value,
                                             CurrentBalance = g.Sum(x => x.Amount ?? 0),
                                             NumberOfInvestors = g.Select(x => x.UserEmail!.ToLower()).Distinct().Count()
                                         })
                                         .ToDictionaryAsync(x => x.CampaignId);

            var avatars = await _context.Recommendations
                                        .Where(r =>
                                            campaignIds.Contains(r.CampaignId!.Value) &&
                                            (r.Status == "approved" || r.Status == "pending"))
                                        .Join(_context.Users,
                                                r => r.UserEmail,
                                                u => u.Email,
                                                (r, u) => new
                                                {
                                                    r.CampaignId,
                                                    u.PictureFileName,
                                                    u.ConsentToShowAvatar,
                                                    r.Id
                                                })
                                        .Where(x => x.PictureFileName != null && x.ConsentToShowAvatar)
                                        .ToListAsync();

            var avatarLookup = avatars
                               .GroupBy(x => x.CampaignId!.Value)
                               .ToDictionary(
                                   g => g.Key,
                                   g => g.OrderByDescending(x => x.Id)
                                           .Select(x => x.PictureFileName)
                                           .Distinct()
                                           .Take(3)
                                           .ToList()
                               );

            var resultDtos = campaigns.Select(c =>
            {
                var dto = _mapper.Map<CampaignCardDtov2>(c);

                if (recStats.TryGetValue(c.Id!.Value, out var stats))
                {
                    dto.CurrentBalance = stats.CurrentBalance + (c.AddedTotalAdminRaised ?? 0);
                    dto.NumberOfInvestors = stats.NumberOfInvestors;
                }

                dto.LatestInvestorAvatars = avatarLookup.ContainsKey(c.Id.Value) ? avatarLookup[c.Id.Value]! : new List<string>();

                return dto;
            })
            .OrderByDescending(c => c.NumberOfInvestors)
            .ThenByDescending(c => c.CurrentBalance)
            .Take(6)
            .ToList();

            return resultDtos;
        }

        private static string ReplaceSiteConfigTokens(string html, Dictionary<string, string> siteConfigs)
        {
            if (string.IsNullOrWhiteSpace(html))
                return html;

            return System.Text.RegularExpressions.Regex.Replace(html, @"\{(.*?)\}", match =>
            {
                var key = match.Groups[1].Value.Trim();

                if (!siteConfigs.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    return string.Empty;

                return RemoveOuterPTags(value);

            }, System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string RemoveOuterPTags(string html)
        {
            html = html.Trim();

            var match = System.Text.RegularExpressions.Regex.Match(html, @"^\s*<p[^>]*>(.*?)<\/p>\s*$",
                System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            return match.Success ? match.Groups[1].Value.Trim() : html;
        }

        private async Task<Dictionary<string, string?>> UploadCampaignFiles(Campaign campaign)
        {
            var filesToUpload = new Dictionary<string, (string? Base64, string Extension)>
            {
                ["PDFPresentation"] = (campaign.PDFPresentation, ".pdf"),
                ["Image"] = (campaign.Image, ".jpg"),
                ["TileImage"] = (campaign.TileImage, ".jpg"),
                ["Logo"] = (campaign.Logo, ".jpg")
            };

            var uploadTasks = filesToUpload.Where(f => !string.IsNullOrWhiteSpace(f.Value.Base64))
                                           .ToDictionary(
                                               f => f.Key,
                                               f => UploadBase64File(f.Value.Base64!, f.Value.Extension)
                                           );

            await Task.WhenAll(uploadTasks.Values);

            var result = uploadTasks.ToDictionary(
                                        f => f.Key,
                                        f => (string?)f.Value.Result
                                    );

            return result;
        }

        private async Task<string> UploadBase64File(string base64Data, string extension)
        {
            if (string.IsNullOrWhiteSpace(base64Data))
                return string.Empty;

            string fileName = $"{Guid.NewGuid()}{extension}";
            var blob = _blobContainerClient.GetBlockBlobClient(fileName);

            var dataIndex = base64Data.Substring(base64Data.IndexOf(',') + 1);
            var bytes = Convert.FromBase64String(dataIndex);

            using var stream = new MemoryStream(bytes);
            await blob.UploadAsync(stream);

            return fileName;
        }

        private void PreserveAdminFields(CampaignDto existing, Campaign update)
        {
            update.MinimumInvestment = existing.MinimumInvestment;
            update.ApprovedBy = existing.ApprovedBy;
            update.Stage = existing.Stage;
            update.GroupForPrivateAccessDto = _mapper.Map<GroupDto>(existing.GroupForPrivateAccess);
            update.Property = existing.Property;
            update.AddedTotalAdminRaised = existing.AddedTotalAdminRaised;
            update.IsActive = existing.IsActive;
        }

        private async Task SendUpdateCampaignEmails(CampaignDto existing, Campaign campaign)
        {
            if (_appSecrets.IsProduction)
            {
                if (existing?.Stage == InvestmentStage.Public && campaign.Stage == InvestmentStage.Private)
                {
                    var variables = new Dictionary<string, string>
                    {
                        { "date", DateTime.Now.ToString("MM/dd/yyyy") },
                        { "investmentLink", $"{_appSecrets.RequestOrigin}/investments/{campaign.Property}" },
                        { "campaignName", campaign.Name! }
                    };

                    _emailQueue.QueueEmail(async (sp) =>
                    {
                        var emailService = sp.GetRequiredService<IEmailTemplateService>();

                        await emailService.SendTemplateEmailAsync(
                            EmailTemplateCategory.InvestmentApproved,
                            "test@gmail.com",
                            variables
                        );
                    });
                }
            }

            if (campaign.Stage == InvestmentStage.ComplianceReview)
            {
                var variables = new Dictionary<string, string>
                {
                    { "campaignName", campaign.Name! }
                };

                _emailQueue.QueueEmail(async (sp) =>
                {
                    var emailService = sp.GetRequiredService<IEmailTemplateService>();

                    var subjectPrefix = _appSecrets.IsProduction ? "" : "QA - ";

                    await emailService.SendTemplateEmailAsync(
                        EmailTemplateCategory.ComplianceReviewNotification,
                        "test@gmail.com",
                        variables,
                        subjectPrefix
                    );
                });
            }
        }

        private async Task SendCreateCampaignEmails(Campaign campaign, CampaignDto mappedCampaign, string userEmail)
        {
            var tasks = new List<Task>();

            if (_appSecrets.IsProduction)
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

                var campaignVariables = new Dictionary<string, string>
                {
                    { "userFullName", $"{campaign!.FirstName} {campaign.LastName}" },
                    { "ownerEmail", campaign.ContactInfoEmailAddress ?? "" },
                    { "informationalEmail", campaign.InvestmentInformationalEmail ?? "" },
                    { "mobileNumber", campaign.ContactInfoPhoneNumber ?? "" },
                    { "addressLine1", campaign.ContactInfoAddress ?? "" },
                    { "investmentName", campaign.Name ?? "" },
                    { "investmentDescription", campaign.Description ?? "" },
                    { "website", campaign.Website ?? "" },
                    { "investmentTypes", investmentTypeNamesString },
                    { "terms", campaign.Terms ?? "" },
                    { "target", campaign.Target?.ToString() ?? "" },
                    { "fundraisingCloseDate", campaign.FundraisingCloseDate?.ToString() ?? "" },
                    { "themes", themeNamesString },
                    { "sdgs", sdgNamesString },
                    { "impactAssetsFundingStatus", campaign.ImpactAssetsFundingStatus ?? "" },
                    { "investmentRole", campaign.InvestmentRole ?? "" },

                    { "addressLine2Section", string.IsNullOrWhiteSpace(campaign.ContactInfoAddress2) ? "" : $"<p>Address Line 2: {campaign.ContactInfoAddress2}</p><br/>" },
                    { "citySection", string.IsNullOrWhiteSpace(campaign.City) ? "" : $"<p>City: {campaign.City}</p><br/>" },
                    { "stateSection", string.IsNullOrWhiteSpace(campaign.State) ? "" : $"<p>State: {campaign.State}</p><br/>" },
                    { "zipCodeSection", string.IsNullOrWhiteSpace(campaign.ZipCode) ? "" : $"<p>Zip Code: {campaign.ZipCode}</p><br/>" }
                };

                _emailQueue.QueueEmail(async (sp) =>
                {
                    var emailService = sp.GetRequiredService<IEmailTemplateService>();

                    await emailService.SendTemplateEmailAsync(
                        EmailTemplateCategory.InvestmentSubmissionNotification,
                        "test@gmail.com",
                        campaignVariables
                    );
                });

                var catacapAdminVariables = new Dictionary<string, string>
                {
                    { "date", DateTime.Now.ToString("M/d/yyyy") },
                    { "campaignName", mappedCampaign.Name! }
                };

                _emailQueue.QueueEmail(async (sp) =>
                {
                    var emailService = sp.GetRequiredService<IEmailTemplateService>();

                    await emailService.SendTemplateEmailAsync(
                        EmailTemplateCategory.InvestmentPublished,
                        "test@gmail.com",
                        catacapAdminVariables
                    );
                });
            }

            var variables = new Dictionary<string, string>
            {
                { "fullName", $"{campaign!.FirstName!} {campaign.LastName!}" },
                { "investmentName", campaign.Name! },
                { "preLaunchToolkitUrl", "preLaunchToolkitUrl" },
                { "partnerBenefitsUrl", "partnerBenefitsUrl" },
                { "faqPageUrl", "faqPageUrl" },
                { "unsubscribeUrl", $"{_appSecrets.RequestOrigin}/settings" }
            };

            _emailQueue.QueueEmail(async (sp) =>
            {
                var emailService = sp.GetRequiredService<IEmailTemplateService>();

                await emailService.SendTemplateEmailAsync(
                    EmailTemplateCategory.InvestmentUnderReview,
                    userEmail,
                    variables
                );
            });
        }

        private async Task<bool> VerifyCaptcha(string token)
        {
            var requestContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("secret", _appSecrets.CaptchaSecretKey),
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
            var userName = $"{firstName}{lastName}".Replace(" ", "").Trim().ToLower();
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
                Password = _appSecrets.DefaultPassword,
                Email = email
            };

            await _repository.UserAuthentication.RegisterUserAsync(registrationDto, UserRoles.User);

            var user = await _repository.UserAuthentication.GetUserByUserName(userName);
            user.IsFreeUser = true;
            await _repository.UserAuthentication.UpdateUser(user);
            await _repository.SaveAsync();

            var variables = new Dictionary<string, string>
            {
                { "firstName", firstName! },
                { "userName", userName },
                { "resetPasswordUrl", $"{_appSecrets.RequestOrigin}/forgotpassword" },
                { "siteUrl", _appSecrets.RequestOrigin }
            };

            _emailQueue.QueueEmail(async (sp) =>
            {
                var emailService = sp.GetRequiredService<IEmailTemplateService>();

                await emailService.SendTemplateEmailAsync(
                    EmailTemplateCategory.WelcomeAnonymousUser,
                    email,
                    variables
                );
            });

            return user;
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
                                            .Where(i => i.IsActive!.Value &&
                                                    (i.Stage == InvestmentStage.Public
                                                        || i.Stage == InvestmentStage.CompletedOngoing)
                                                    )
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
    }
}
