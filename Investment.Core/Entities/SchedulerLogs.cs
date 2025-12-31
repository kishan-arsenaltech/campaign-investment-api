using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class SchedulerLogs
    {
        public int Id { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime StartTime { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime EndTime { get; set; }

        public int Day3EmailCount { get; set; }
        public int Week2EmailCount { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
