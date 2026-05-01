using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Invest.Authorization.Enums;
using Invest.Core.Entities;
using Investment.Authorization.Attributes;
using Investment.Authorization.Enums;
using Investment.Core.Constants;
using Investment.Core.Dtos;
using Investment.Core.Entities;
using Investment.Core.Extensions;
using Investment.Repo.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Investment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Module(Modules.SiteConfiguration)]
    public class SiteConfigurationController : ControllerBase
    {
        private readonly RepositoryContext _context;
        private readonly BlobContainerClient _blobContainerClient;

        public SiteConfigurationController(RepositoryContext context, BlobContainerClient blobContainerClient)
        {
            _context = context;
            _blobContainerClient = blobContainerClient;
        }

        [HttpGet("slug/{slug}")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> SlugCheck(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return Ok(new { Success = false, Message = "Slug is required." });

            var campaignExists = await _context.Campaigns.AnyAsync(c => c.Property == slug);
            var groupExists = await _context.Groups.AnyAsync(g => g.Identifier == slug);

            var exists = campaignExists || groupExists;

            return Ok(new { exists });
        }

        [HttpGet("{type}")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<object> Get(string type, bool? isDeleted)
        {
            switch (type.ToLower().Trim())
            {
                case "investment-terms":

                    return await _context.SiteConfiguration
                                         .ApplySoftDeleteFilter(isDeleted)
                                         .Where(x => x.Type == SiteConfigurationType.StaticValue)
                                         .OrderBy(x => x.Key)
                                         .Select(x => new
                                         {
                                             x.Id,
                                             x.Key,
                                             x.Value
                                         })
                                         .ToListAsync();

                case "sourcedby":

                    return await _context.ApprovedBy
                                         .ApplySoftDeleteFilter(isDeleted)
                                         .OrderBy(x => x.Name)
                                         .Select(x => new
                                         {
                                             x.Id,
                                             Value = x.Name
                                         })
                                         .ToListAsync();

                case "themes":

                    return await _context.Themes
                                         .ApplySoftDeleteFilter(isDeleted)
                                         .OrderBy(x => x.Name)
                                         .Select(x => new
                                         {
                                             x.Id,
                                             Value = x.Name,
                                             x.ImageFileName,
                                             x.Description
                                         })
                                         .ToListAsync();

                case "configuration":

                    return await _context.SiteConfiguration
                                         .ApplySoftDeleteFilter(isDeleted)
                                         .Where(t => t.Type == SiteConfigurationType.Configuration)
                                         .OrderBy(x => x.Key)
                                         .Select(x => new
                                         {
                                             x.Id,
                                             x.Key,
                                             x.Value
                                         })
                                         .ToListAsync();

                case "transaction-type":

                    return await _context.SiteConfiguration
                                         .ApplySoftDeleteFilter(isDeleted)
                                         .Where(t => t.Type == SiteConfigurationType.TransactionType)
                                         .OrderBy(t => t.Value)
                                         .Select(t => new
                                         {
                                             t.Id,
                                             t.Value
                                         })
                                         .ToListAsync();

                case "statistics":

                    return await _context.SiteConfiguration
                                         .ApplySoftDeleteFilter(isDeleted)
                                         .Where(t => t.Type.Contains(SiteConfigurationType.Statistics))
                                         .OrderBy(x => x.Key)
                                         .Select(x => new
                                         {
                                             x.Id,
                                             x.Key,
                                             x.Value,
                                             Type = x.Type.Replace("Statistics-", "")
                                         })
                                         .ToListAsync();

                case "meta-information":

                    return await _context.SiteConfiguration
                                         .ApplySoftDeleteFilter(isDeleted)
                                         .Where(t => t.Type.Contains(SiteConfigurationType.MetaInformation))
                                         .OrderBy(x => x.Key)
                                         .Select(x => new
                                         {
                                             x.Id,
                                             x.Key,
                                             x.Image,
                                             x.ImageName,
                                             x.Value,
                                             x.AdditionalDetails
                                         })
                                         .ToListAsync();

                default:
                    return new List<object>();
            }
        }

        [HttpPost]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> CreateOrUpdate([FromBody] SiteConfigurationDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Type))
                return BadRequest("Type is required.");

            var result = (dto.Id.HasValue && dto.Id > 0)
                            ? await UpdateByTypeAsync(dto)
                            : await CreateByTypeAsync(dto);

            return Ok(new { result.Success, result.Message });
        }

        [HttpDelete("{type}/{id}")]
        [ModuleAuthorize(PermissionType.Delete)]
        public async Task<IActionResult> Delete(string type, int id)
        {
            if (string.IsNullOrWhiteSpace(type))
                return BadRequest("Type is required.");

            var result = type.ToLower().Trim() switch
            {
                "investment-terms" => await DeleteMasterAsync(
                    id,
                    x => x.Type == SiteConfigurationType.StaticValue,
                    async (SiteConfiguration entity) => await _context.Campaigns.AnyAsync(c => c.Terms != null && c.Terms.Contains($"{{{entity.Key}}}")),
                    "Cannot delete this term, it’s being used in investments.",
                    "Configuration deleted successfully."
                ),

                "sourcedby" => await DeleteMasterAsync(
                    id,
                    null,
                    async (ApprovedBy entity) => await _context.Campaigns.AnyAsync(c => ("," + c.ApprovedBy + ",").Contains("," + entity.Id + ",")),
                    "Cannot delete this sourced by, it’s being used in investments.",
                    "Sourced by deleted successfully."
                ),

                "themes" => await DeleteMasterAsync(
                    id,
                    null,
                    async (Theme entity) =>
                    {
                        var themeId = entity.Id.ToString();

                        return await _context.Campaigns.AnyAsync(c => ("," + c.Themes + ",").Contains("," + themeId + ",")) ||
                               await _context.Groups.AnyAsync(g => ("," + g.GroupThemes + ",").Contains("," + themeId + ","));
                    },
                    "Cannot delete this theme, it’s being used in investments or groups.",
                    "Theme deleted successfully."
                ),

                "transaction-type" => await DeleteMasterAsync(
                    id,
                    x => x.Type == SiteConfigurationType.TransactionType,
                    async (SiteConfiguration entity) => await _context.CompletedInvestmentsDetails.AnyAsync(x => x.SiteConfigurationId == entity.Id),
                    "Cannot delete this transaction type, it’s being used in investments.",
                    "Transaction type deleted successfully."
                ),

                "statistics" => await DeleteMasterAsync<SiteConfiguration>(
                    id,
                    x => x.Type.Contains(SiteConfigurationType.Statistics),
                    entity => Task.FromResult(false),
                    "",
                    "Statistic deleted successfully."
                ),

                "meta-information" => await DeleteMasterAsync<SiteConfiguration>(
                    id,
                    x => x.Type.Contains(SiteConfigurationType.MetaInformation),
                    entity => Task.FromResult(false),
                    "",
                    "Meta information deleted successfully."
                ),

                _ => (Success: false, Message: "Invalid configuration type.")
            };

            return Ok(new { result.Success, result.Message });
        }

        [HttpGet("{type}/{id}/investments")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetInvestments(string type, int id)
        {
            if (string.IsNullOrWhiteSpace(type))
                return BadRequest("Type is required.");

            if (id <= 0)
                return BadRequest("Id must be greater than zero.");

            type = type.ToLower();

            var result = await _context.Campaigns
                                       .Select(c => new
                                       {
                                           c.Id,
                                           c.Name,
                                           IsSelected =
                                                    type == "themes"
                                                        ? ("," + (c.Themes ?? "") + ",").Contains("," + id + ",")
                                                    : type == "sourcedby"
                                                        ? ("," + (c.ApprovedBy ?? "") + ",").Contains("," + id + ",")
                                                    : type == "sdgs"
                                                        ? ("," + (c.SDGs ?? "") + ",").Contains("," + id + ",")
                                                    : false
                                       })
                                       .OrderBy(c => c.Name)
                                       .ToListAsync();

            return Ok(result);
        }

        [HttpPost("{type}/{id}/investments/{investmentId}")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> UpdateInvestments(string type, int id, int investmentId)
        {
            if (string.IsNullOrWhiteSpace(type))
                return BadRequest(new { success = false, message = "Type is required." });

            if (id <= 0 || investmentId <= 0)
                return BadRequest(new { success = false, message = "Invalid id." });

            type = type.ToLower();
            bool isAdded;

            var campaign = await _context.Campaigns.FirstOrDefaultAsync(x => x.Id == investmentId);
            if (campaign == null)
                return NotFound(new { success = false, message = "Campaign not found." });

            var column = GetColumnValue(campaign, type);
            var list = string.IsNullOrWhiteSpace(column)
                        ? new List<int>()
                        : column
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(int.Parse)
                            .ToList();

            if (list.Contains(id))
            {
                list.Remove(id);
                isAdded = false;
            }
            else
            {
                list.Add(id);
                isAdded = true;
            }

            SetColumnValue(campaign, type, list.Any() ? string.Join(",", list) : null);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                Success = true,
                Message = isAdded ? "Investment mapping added successfully." : "Investment mapping removed successfully."
            });
        }

        private async Task<(bool Success, string Message)> CreateByTypeAsync(SiteConfigurationDto dto)
        {
            var type = dto.Type.ToLower().Trim();

            switch (type)
            {
                case "investment-terms":

                    if (string.IsNullOrWhiteSpace(dto.Key))
                        return (false, "Key is required.");

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Value is required.");

                    var staticEntity = new SiteConfiguration
                    {
                        Key = dto.Key.Trim(),
                        Value = dto.Value.Trim(),
                        Type = SiteConfigurationType.StaticValue
                    };

                    return await CreateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type == SiteConfigurationType.StaticValue),
                        staticEntity,
                        x => x.Key,
                        "Entered key already exists.",
                        "Configuration created successfully."
                    );

                case "configuration":

                    if (string.IsNullOrWhiteSpace(dto.Key))
                        return (false, "Key is required.");

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Value is required.");

                    var configurationEntity = new SiteConfiguration
                    {
                        Key = dto.Key.Trim(),
                        Value = dto.Value.Trim(),
                        Type = SiteConfigurationType.Configuration
                    };

                    return await CreateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type == SiteConfigurationType.Configuration),
                        configurationEntity,
                        x => x.Key,
                        "Entered key already exists.",
                        "Configuration created successfully."
                    );

                case "meta-information":

                    if (string.IsNullOrWhiteSpace(dto.Key))
                        return (false, "Key is required.");

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Value is required.");

                    if (string.IsNullOrWhiteSpace(dto.AdditionalDetails))
                        return (false, "Additional details is required.");

                    var metaInformationEntity = new SiteConfiguration
                    {
                        Key = dto.Key.Trim(),
                        Value = dto.Value.Trim(),
                        AdditionalDetails = dto.AdditionalDetails.Trim(),
                        Type = SiteConfigurationType.MetaInformation
                    };

                    if (!string.IsNullOrWhiteSpace(dto.Image))
                    {
                        metaInformationEntity.Image = await UploadBase64File(dto.Image);
                        metaInformationEntity.ImageName = !string.IsNullOrWhiteSpace(dto.ImageFileName) ? dto.ImageFileName.Trim() : null;
                    }

                    return await CreateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type == SiteConfigurationType.MetaInformation),
                        metaInformationEntity,
                        x => x.Key,
                        "Entered key already exists.",
                        "Configuration created successfully."
                    );

                case "statistics":

                    if (string.IsNullOrWhiteSpace(dto.Key))
                        return (false, "Key is required.");

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Value is required.");

                    var statisticsEntity = new SiteConfiguration
                    {
                        Key = dto.Key.Trim(),
                        Value = dto.Value.Trim(),
                        Type = $"{SiteConfigurationType.Statistics}-{dto.ItemType}"
                    };

                    return await CreateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type == $"{SiteConfigurationType.Statistics}-{dto.ItemType}"),
                        statisticsEntity,
                        x => x.Key,
                        "Entered key already exists.",
                        "Configuration created successfully."
                    );

                case "sourcedby":

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Sourced by is required.");

                    var sourcedEntity = new ApprovedBy
                    {
                        Name = dto.Value.Trim()
                    };

                    return await CreateMasterAsync(
                        _context.ApprovedBy,
                        sourcedEntity,
                        x => x.Name,
                        "Entered sourced by value already exists.",
                        "Sourced by created successfully."
                    );

                case "themes":

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Theme is required.");

                    if (string.IsNullOrWhiteSpace(dto.Image))
                        return (false, "Image is required.");

                    string fileName = await UploadBase64File(dto.Image);

                    var themeEntity = new Theme
                    {
                        Name = dto.Value.Trim(),
                        ImageFileName = fileName,
                        Description = dto.Description
                    };

                    return await CreateMasterAsync(
                        _context.Themes,
                        themeEntity,
                        x => x.Name,
                        "Entered theme value already exists.",
                        "Theme created successfully."
                    );

                case "transaction-type":

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Transaction type is required.");

                    var transactiontypeEntity = new SiteConfiguration
                    {
                        Key = dto.Value.Trim(),
                        Value = dto.Value.Trim(),
                        Type = SiteConfigurationType.TransactionType
                    };

                    return await CreateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type == SiteConfigurationType.TransactionType),
                        transactiontypeEntity,
                        x => x.Value,
                        "Entered transaction type value already exists.",
                        "Transaction type created successfully."
                    );

                default:
                    return (false, "Invalid configuration type.");
            }
        }

        private async Task<(bool Success, string Message)> UpdateByTypeAsync(SiteConfigurationDto dto)
        {
            var type = dto.Type.ToLower().Trim();

            switch (type)
            {
                case "investment-terms":

                    if (string.IsNullOrWhiteSpace(dto.Key))
                        return (false, "Key is required.");

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Value is required.");

                    return await UpdateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type == SiteConfigurationType.StaticValue),
                        dto.Id!.Value,
                        x => x.Key,
                        (e, val, img) =>
                        {
                            e.Key = dto.Key!.Trim();
                            e.Value = dto.Value!.Trim();
                        },
                        dto.Key,
                        null,
                        "Entered key already exists.",
                        "Configuration updated successfully."
                    );

                case "configuration":

                    if (string.IsNullOrWhiteSpace(dto.Key))
                        return (false, "Key is required.");

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Value is required.");

                    return await UpdateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type == SiteConfigurationType.Configuration),
                        dto.Id!.Value,
                        x => x.Key,
                        (e, val, img) =>
                        {
                            e.Key = dto.Key!.Trim();
                            e.Value = dto.Value!.Trim();
                        },
                        dto.Key,
                        null,
                        "Entered key already exists.",
                        "Configuration updated successfully."
                    );

                case "meta-information":

                    if (string.IsNullOrWhiteSpace(dto.Key))
                        return (false, "Key is required.");

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Value is required.");

                    if (string.IsNullOrWhiteSpace(dto.AdditionalDetails))
                        return (false, "Additional details is required.");

                    string? metaImageFileName = null;

                    if (!string.IsNullOrWhiteSpace(dto.Image) && !string.IsNullOrWhiteSpace(dto.ImageFileName))
                        metaImageFileName = await UploadBase64File(dto.Image);

                    return await UpdateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type.Contains(SiteConfigurationType.MetaInformation)),
                        dto.Id!.Value,
                        x => x.Key,
                        (e, val, img) =>
                        {
                            e.Key = dto.Key!.Trim();
                            e.Value = dto.Value!.Trim();
                            e.AdditionalDetails = dto.AdditionalDetails!.Trim();
                            e.Type = SiteConfigurationType.MetaInformation;

                            if (img != null)
                            {
                                e.ImageName = img;
                                e.Image = dto.ImageFileName;
                            }
                            else if (string.IsNullOrWhiteSpace(dto.Image) && string.IsNullOrWhiteSpace(dto.ImageFileName))
                            {
                                e.ImageName = null;
                                e.Image = null;
                            }
                        },
                        dto.Key,
                        metaImageFileName,
                        "Entered key already exists.",
                        "Configuration updated successfully."
                    );

                case "statistics":

                    if (string.IsNullOrWhiteSpace(dto.Key))
                        return (false, "Key is required.");

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Value is required.");

                    return await UpdateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type.Contains($"{SiteConfigurationType.Statistics}-{dto.ItemType}")),
                        dto.Id!.Value,
                        x => x.Key,
                        (e, val, img) =>
                        {
                            e.Key = dto.Key!.Trim();
                            e.Value = dto.Value!.Trim();
                            e.Type = $"{SiteConfigurationType.Statistics}-{dto.ItemType}";
                        },
                        dto.Key,
                        null,
                        "Entered key already exists.",
                        "Configuration updated successfully."
                    );

                case "sourcedby":

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Sourced by is required.");

                    return await UpdateMasterAsync(
                        _context.ApprovedBy,
                        dto.Id!.Value,
                        x => x.Name,
                        (e, val, img) =>
                        {
                            e.Name = val!;
                        },
                        dto.Value!,
                        null,
                        "Entered sourced by value already exists.",
                        "Sourced by updated successfully."
                    );

                case "transaction-type":

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Transaction type is required.");

                    return await UpdateMasterAsync(
                        _context.SiteConfiguration.Where(x => x.Type == SiteConfigurationType.TransactionType),
                        dto.Id!.Value,
                        x => x.Value,
                        (e, val, img) =>
                        {
                            e.Key = val!;
                            e.Value = val!;
                        },
                        dto.Value!,
                        null,
                        "Entered transaction type value already exists.",
                        "Transaction type updated successfully."
                    );

                case "themes":

                    if (string.IsNullOrWhiteSpace(dto.Value))
                        return (false, "Theme is required.");

                    string? fileName = null;

                    if (!string.IsNullOrWhiteSpace(dto.Image) && !string.IsNullOrWhiteSpace(dto.ImageFileName))
                        fileName = await UploadBase64File(dto.Image);

                    return await UpdateMasterAsync(
                        _context.Themes,
                        dto.Id!.Value,
                        x => x.Name,
                        (e, val, img) =>
                        {
                            e.Name = val!;
                            e.Description = dto.Description;

                            if (img != null)
                                e.ImageFileName = img;
                            else if (string.IsNullOrWhiteSpace(dto.Image) && string.IsNullOrWhiteSpace(dto.ImageFileName))
                                e.ImageFileName = null;
                        },
                        dto.Value,
                        fileName,
                        "Entered theme value already exists.",
                        "Theme updated successfully."
                    );

                default:
                    return (false, "Invalid configuration type.");
            }
        }

        private async Task<(bool Success, string Message)> CreateMasterAsync<T>(
            IQueryable<T> query,
            T entity,
            Expression<Func<T, string>> selector,
            string duplicateMessage,
            string successMessage)
            where T : class
        {
            var value = selector.Compile()(entity).Trim();
            var parameter = selector.Parameters[0];
            var property = selector.Body;
            var trimMethod = typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!;
            var left = Expression.Call(property, trimMethod);
            var right = Expression.Constant(value);
            var body = Expression.Equal(left, right);
            var lambda = Expression.Lambda<Func<T, bool>>(body, parameter);

            if (await query.AnyAsync(lambda))
                return (false, duplicateMessage);

            await _context.Set<T>().AddAsync(entity);
            await _context.SaveChangesAsync();

            return (true, successMessage);
        }

        private async Task<(bool Success, string Message)> UpdateMasterAsync<T>(
            IQueryable<T> query,
            int id,
            Expression<Func<T, string>> selector,
            Action<T, string?, string?> updateAction,
            string? newValue,
            string? image,
            string duplicateMessage,
            string successMessage)
            where T : class
        {
            var entity = await _context.Set<T>().FindAsync(id);

            if (entity == null)
                return (false, "Record not found.");

            var parameter = selector.Parameters[0];
            var property = selector.Body;
            var trimMethod = typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!;
            var left = Expression.Call(property, trimMethod);
            var right = Expression.Constant(newValue!.Trim());
            var equalExpression = Expression.Equal(left, right);
            var idProperty = Expression.Property(parameter, "Id");
            var idConstant = Expression.Constant(id);
            var notEqualId = Expression.NotEqual(idProperty, idConstant);
            var finalBody = Expression.AndAlso(notEqualId, equalExpression);
            var lambda = Expression.Lambda<Func<T, bool>>(finalBody, parameter);

            if (await query.AnyAsync(lambda))
                return (false, duplicateMessage);

            updateAction(entity, newValue?.Trim(), image);

            await _context.SaveChangesAsync();

            return (true, successMessage);
        }

        private async Task<(bool Success, string Message)> DeleteMasterAsync<T>(
            int id,
            Expression<Func<T, bool>>? extraFilter,
            Func<T, Task<bool>> isUsedCheck,
            string usedMessage,
            string successMessage)
            where T : class
        {
            IQueryable<T> query = _context.Set<T>();

            if (extraFilter != null)
                query = query.Where(extraFilter);

            var entity = await query.FirstOrDefaultAsync(e => EF.Property<int>(e, "Id") == id);

            if (entity == null)
                return (false, "Record not found.");

            if (await isUsedCheck(entity))
                return (false, usedMessage);

            _context.Set<T>().Remove(entity);
            await _context.SaveChangesAsync();

            return (true, successMessage);
        }

        private string? GetColumnValue(CampaignDto c, string type)
        {
            return type.ToLower() switch
            {
                "themes" => c.Themes,
                "sourcedby" => c.ApprovedBy,
                "sdgs" => c.SDGs,
                _ => null
            };
        }

        private void SetColumnValue(CampaignDto c, string type, string? value)
        {
            switch (type.ToLower())
            {
                case "themes": c.Themes = value; break;
                case "sourcedby": c.ApprovedBy = value; break;
                case "sdgs": c.SDGs = value; break;
            }
        }

        private async Task<string> UploadBase64File(string base64Data)
        {
            if (string.IsNullOrWhiteSpace(base64Data))
                return string.Empty;

            string fileName = $"{Guid.NewGuid()}.jpg";
            var blob = _blobContainerClient.GetBlockBlobClient(fileName);

            var dataIndex = base64Data.Substring(base64Data.IndexOf(',') + 1);
            var bytes = Convert.FromBase64String(dataIndex);

            using var stream = new MemoryStream(bytes);
            await blob.UploadAsync(stream);

            return fileName;
        }
    }
}
