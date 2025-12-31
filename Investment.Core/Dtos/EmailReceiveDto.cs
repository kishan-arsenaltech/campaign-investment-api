// Ignore Spelling: Dto Captcha Dtos

using System.ComponentModel.DataAnnotations;

namespace Invest.Core.Dtos
{
    public class EmailReceiveDto
    {
        [Required(ErrorMessage = "Email is a required field.")]
        public string Email { get; set; } = string.Empty;

        public string? CaptchaToken { get; set; }
    }
}
