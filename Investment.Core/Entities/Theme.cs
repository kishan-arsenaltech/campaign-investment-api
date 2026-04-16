using Investment.Core.Entities;

namespace Invest.Core.Entities
{
    public class Theme: BaseEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ImageFileName { get; set; }
        public string? Description { get; set; }
        public bool Mandatory { get; set; }
    }
}
