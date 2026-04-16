using Invest.Core.Entities;
using Investment.Core.Constants;

namespace Invest.Core.Models
{
    public class EmailTemplateVariable
    {
        public int Id { get; set; }
        public EmailTemplateCategory Category { get; set; }
        public string VariableName { get; set; } = null!;
        public EmailTemplate EmailTemplate { get; set; } = null!;
    }
}
