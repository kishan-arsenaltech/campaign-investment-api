using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class InvestmentType
    {
        [Column("Id")]
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}
