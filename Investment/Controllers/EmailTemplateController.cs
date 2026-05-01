using Invest.Authorization.Enums;
using Invest.Core.Entities;
using Invest.Core.Models;
using Investment.Authorization.Attributes;
using Investment.Authorization.Enums;
using Investment.Core.Constants;
using Investment.Core.Dtos;
using Investment.Core.Extensions;
using Investment.Repo.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Investment.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Module(Modules.EmailTemplate)]
    public class EmailTemplateController : ControllerBase
    {
        private readonly RepositoryContext _context;

        public EmailTemplateController(RepositoryContext context)
        {
            _context = context;
        }

        [HttpGet]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetTemplates([FromQuery] PaginationDto pagination, EmailTemplateCategory? category)
        {
            int page = pagination?.CurrentPage ?? 1;
            int pageSize = pagination?.PerPage ?? 10;
            string? sortField = pagination?.SortField?.ToLower();
            bool isAsc = pagination?.SortDirection?.ToLower() == "asc";
            bool? isDeleted = pagination?.IsDeleted;

            var query = _context.EmailTemplate.ApplySoftDeleteFilter(isDeleted).AsQueryable();

            if (!string.IsNullOrWhiteSpace(pagination?.SearchValue))
            {
                string search = pagination.SearchValue.ToLower();
                query = query.Where(x =>
                    x.Name.ToLower().Contains(search) ||
                    x.Subject.ToLower().Contains(search));
            }

            if (category.HasValue)
                query = query.Where(x => x.Category == category.Value);

            if (!string.IsNullOrWhiteSpace(pagination?.Status))
            {
                if (Enum.TryParse<EmailTemplateStatus>(pagination.Status, true, out var statusEnum))
                    query = query.Where(x => x.Status == statusEnum);
            }

            var dataQuery = query.Select(x => new
            {
                x.Id,
                x.Name,
                x.Subject,
                x.Category,
                x.Status,
                x.Receiver,
                x.TriggerAction,
                ModifiedAt = x.ModifiedAt ?? x.CreatedAt,
                x.DeletedAt,
                x.DeletedByUser
            });

            dataQuery = sortField switch
            {
                "name" => isAsc ? dataQuery.OrderBy(x => x.Name) : dataQuery.OrderByDescending(x => x.Name),
                "subject" => isAsc ? dataQuery.OrderBy(x => x.Subject) : dataQuery.OrderByDescending(x => x.Subject),
                "category" => isAsc ? dataQuery.OrderBy(x => (int)x.Category) : dataQuery.OrderByDescending(x => (int)x.Category),
                "status" => isAsc ? dataQuery.OrderBy(x => (int)x.Status) : dataQuery.OrderByDescending(x => (int)x.Status),
                "modifiedat" => isAsc ? dataQuery.OrderBy(x => x.ModifiedAt) : dataQuery.OrderByDescending(x => x.ModifiedAt),
                _ => dataQuery.OrderByDescending(x => x.ModifiedAt)
            };

            int totalRecords = await query.CountAsync();

            var raw = await dataQuery
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

            var data = raw.Select(x => new EmailTemplateDto
            {
                Id = x.Id,
                Name = x.Name,
                Subject = x.Subject,
                Category = x.Category,
                CategoryName = x.Category.GetDisplayName(),
                Status = x.Status,
                StatusName = x.Status.ToString(),
                Receiver = x.Receiver,
                TriggerAction = x.TriggerAction,
                ModifiedAt = x.ModifiedAt,
                DeletedAt = x.DeletedAt,
                DeletedBy = x.DeletedByUser != null
                            ? $"{x.DeletedByUser.FirstName} {x.DeletedByUser.LastName}"
                            : null
            });

            data = sortField switch
            {
                "category" => isAsc ? data.OrderBy(x => x.CategoryName) : data.OrderByDescending(x => x.CategoryName),
                "status" => isAsc ? data.OrderBy(x => x.StatusName) : data.OrderByDescending(x => x.StatusName),
                _ => data
            };

            return Ok(new { totalRecords, items = data.ToList() });
        }

        [HttpGet("{id}")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetById(int id)
        {
            var template = await _context.EmailTemplate
                                         .Where(x => x.Id == id)
                                         .Select(x => new EmailTemplateDto
                                         {
                                             Id = x.Id,
                                             Name = x.Name,
                                             Subject = x.Subject,
                                             BodyHtml = x.BodyHtml,
                                             Category = x.Category,
                                             CategoryName = x.Category.ToString(),
                                             Status = x.Status,
                                             Receiver = x.Receiver,
                                             TriggerAction = x.TriggerAction,
                                             StatusName = x.Status.ToString(),
                                         })
                                         .FirstOrDefaultAsync();

            if (template == null)
                return Ok(new { Success = false, Message = "Template not found." });

            return Ok(template);
        }

        [HttpGet("preview/{id}")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> Preview(int id)
        {
            var template = await _context.EmailTemplate.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

            if (template == null)
                return Ok(new { Success = false, Message = "Template not found." });

            return Ok(new { template.Name, template.Subject, template.BodyHtml });
        }

        [HttpGet("html/{id}")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> GetHtmlSource(int id)
        {
            var template = await _context.EmailTemplate.FirstOrDefaultAsync(x => x.Id == id);

            if (template == null)
                return Ok(new { Success = false, Message = "Template not found." });

            return Ok(template.BodyHtml);
        }

        [HttpGet("duplicate/{id}")]
        [ModuleAuthorize(PermissionType.View)]
        public async Task<IActionResult> Duplicate(int id)
        {
            var existing = await _context.EmailTemplate.FirstOrDefaultAsync(x => x.Id == id);

            if (existing == null)
                return Ok(new { Success = false, Message = "Template not found." });

            var copyDto = new EmailTemplateDto
            {
                Name = existing.Name + " (Copy)",
                Subject = existing.Subject,
                BodyHtml = existing.BodyHtml,
                Category = existing.Category,
                CategoryName = existing.Category.ToString(),
                Status = EmailTemplateStatus.Draft,
                StatusName = EmailTemplateStatus.Draft.ToString()
            };

            return Ok(copyDto);
        }

        [HttpGet("categories")]
        [ModuleAuthorize(PermissionType.View)]
        public IActionResult GetEmailTemplateCategory()
        {
            var categories = Enum.GetValues(typeof(EmailTemplateCategory))
                                 .Cast<EmailTemplateCategory>()
                                 .Select(x => new
                                 {
                                     Id = (int)x,
                                     Name = x.ToString(),
                                     Label = x.GetDisplayName()
                                 })
                                 .OrderBy(x => x.Label)
                                 .ToList();

            return Ok(categories);
        }

        [HttpPost]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> Save([FromBody] EmailTemplateDto dto)
        {
            if (dto == null)
                return BadRequest("Invalid data.");

            var identity = HttpContext.User.Identity as ClaimsIdentity;
            var userId = identity?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;

            if (dto.Status == EmailTemplateStatus.Active)
            {
                bool activeExists = await _context.EmailTemplate
                                                    .AnyAsync(x =>
                                                        x.Category == dto.Category &&
                                                        x.Status == EmailTemplateStatus.Active &&
                                                        x.Id != dto.Id);

                if (activeExists)
                    return Ok(new { Success = false, Message = "An active template already exists for this category." });
            }

            EmailTemplate? template;

            if (dto.Id.HasValue && dto.Id > 0)
            {
                template = await _context.EmailTemplate.FirstOrDefaultAsync(x => x.Id == dto.Id.Value);

                if (template == null)
                    return Ok(new { Success = false, Message = "Template not found." });

                template.Name = dto.Name;
                template.Subject = dto.Subject;
                template.BodyHtml = dto.BodyHtml;
                template.Category = dto.Category;
                template.Status = dto.Status;
                template.Receiver = dto.Receiver;
                template.TriggerAction = dto.TriggerAction;
                template.ModifiedAt = DateTime.Now;
                template.ModifiedBy = userId;
            }
            else
            {
                template = new EmailTemplate
                {
                    Name = dto.Name,
                    Subject = dto.Subject,
                    BodyHtml = dto.BodyHtml,
                    Category = dto.Category,
                    Status = dto.Status,
                    Receiver = dto.Receiver,
                    TriggerAction = dto.TriggerAction,
                    CreatedAt = DateTime.Now,
                    CreatedBy = userId
                };

                _context.EmailTemplate.Add(template);
            }

            await _context.SaveChangesAsync();

            var existingVars = _context.EmailTemplateVariable.Where(v => v.Category == template.Category);

            _context.EmailTemplateVariable.RemoveRange(existingVars);

            var subjectVars = ExtractVariables(template.Subject);
            var bodyVars = ExtractVariables(template.BodyHtml);

            var allVars = subjectVars
                        .Union(bodyVars)
                        .Distinct()
                        .ToList();

            foreach (var variable in allVars)
            {
                _context.EmailTemplateVariable.Add(new EmailTemplateVariable
                {
                    Category = template.Category,
                    VariableName = variable,
                    EmailTemplate = template
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                Success = true,
                Message = dto.Id.HasValue ? "Template updated successfully." : "Template created successfully.",
                Data = template.Id
            });
        }

        [HttpDelete("{id}")]
        [ModuleAuthorize(PermissionType.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var template = await _context.EmailTemplate.FirstOrDefaultAsync(x => x.Id == id);

            if (template == null)
                return Ok(new { Success = false, Message = "Email template not found." });

            _context.EmailTemplate.Remove(template);
            await _context.SaveChangesAsync();

            return Ok(new { Success = true, Message = "Email template deleted successfully." });
        }

        [HttpPut("restore")]
        [ModuleAuthorize(PermissionType.Manage)]
        public async Task<IActionResult> Restore([FromBody] List<int> ids)
        {
            if (ids == null || !ids.Any())
                return Ok(new { Success = false, Message = "No IDs provided." });

            var templates = await _context.EmailTemplate
                                          .IgnoreQueryFilters()
                                          .Where(x => ids.Contains(x.Id))
                                          .ToListAsync();

            if (!templates.Any())
                return Ok(new { Success = false, Message = "Email template not found." });

            var deletedTemplates = templates.Where(x => x.IsDeleted).ToList();

            if (!deletedTemplates.Any())
                return Ok(new { Success = false, Message = "No deleted email templates found to restore." });

            deletedTemplates.RestoreRange();

            await _context.SaveChangesAsync();

            return Ok(new { Success = true, Message = $"{deletedTemplates.Count} email template(s) restored successfully." });
        }

        private List<string> ExtractVariables(string content)
        {
            return System.Text.RegularExpressions.Regex
                .Matches(content ?? "", @"\{\{(.*?)\}\}")
                .Select(x => x.Groups[1].Value.Trim())
                .Distinct()
                .ToList();
        }
    }
}
