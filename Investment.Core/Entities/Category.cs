using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Invest.Core.Entities
{
    public class Category
    {
        [Column("Id")]
        public int Id { get; set; }


        [Required(ErrorMessage = "Category's Name is a required field.")]
        [MaxLength(60, ErrorMessage = "Maximum length for the Name is 60 characters.")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Category's Mandatory flag.")]
        public bool Mandatory { get; set; }
    }
}
