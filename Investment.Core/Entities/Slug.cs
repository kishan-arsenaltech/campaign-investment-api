using Invest.Core.Constants;
using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class Slug
    {
        public int Id { get; set; }
        public int ReferenceId { get; set; }
        public SlugType Type { get; set; }
        public string? Value { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
