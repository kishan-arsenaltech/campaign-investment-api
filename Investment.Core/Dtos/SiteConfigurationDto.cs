namespace Investment.Core.Dtos
{
    public class SiteConfigurationDto
    {
        public int? Id { get; set; }
        public string? Key { get; set; }
        public string? Value { get; set; }
        public string? Image { get; set; }
        public string? ImageFileName { get; set; }
        public string? Description { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? ItemType { get; set; }
        public string? AdditionalDetails { get; set; }
    }
}
