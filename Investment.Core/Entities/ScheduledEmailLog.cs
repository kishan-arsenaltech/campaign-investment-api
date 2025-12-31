using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class ScheduledEmailLog
    {
        public int Id { get; set; }
        public int PendingGrantId { get; set; }
        public PendingGrants? PendingGrants { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }
        public string? ReminderType { get; set; }
        public string? ErrorMessage { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime SentDate { get; set; } = DateTime.Now;
    }
}
