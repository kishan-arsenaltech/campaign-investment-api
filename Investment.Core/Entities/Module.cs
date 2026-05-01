using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class Module
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
        public int SortOrder { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
