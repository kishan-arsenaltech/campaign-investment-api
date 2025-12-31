using System.ComponentModel.DataAnnotations.Schema;

namespace Invest.Core.Entities
{
    public class ApprovedBy
    {
        [Column("Id")]
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}
