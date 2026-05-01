using Investment.Core.Constants;

namespace Investment.Core.Dtos
{
    public class EmailTemplateDto
    {
        public int? Id { get; set; }
        public string Name { get; set; } = null!;
        public string Subject { get; set; } = null!;
        public string BodyHtml { get; set; } = null!;
        public EmailTemplateCategory Category { get; set; }
        public string? CategoryName { get; set; }
        public EmailTemplateStatus Status { get; set; }
        public string? StatusName { get; set; }
        public string? Receiver { get; set; }
        public string? TriggerAction { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }
}
