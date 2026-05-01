namespace Investment.Core.Entities
{
    public class SiteConfiguration : BaseEntity
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? AdditionalDetails { get; set; }
        public string? ImageName { get; set; }
        public string? Image { get; set; }
    }
}
