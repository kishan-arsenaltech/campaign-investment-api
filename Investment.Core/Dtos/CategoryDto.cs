using System.ComponentModel.DataAnnotations;

namespace Investment.Core.Dtos
{
    public class CategoryDto
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Category name is a required field.")]
        public string? Name { get; set; }
        public bool Mandatory { get; set; }
    }
}
