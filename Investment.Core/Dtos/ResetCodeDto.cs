// Ignore Spelling: Dto Dtos

using System.ComponentModel.DataAnnotations;

namespace Invest.Core.Dtos
{
    public class ResetCodeDto
    {
        [Required(ErrorMessage = "Email is a required field.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Code is a required field.")]
        public int Code { get; set; }
    }
}
