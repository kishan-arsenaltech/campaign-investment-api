namespace Investment.Core.Entities
{
    public class DAFProviders
    {
        public int Id { get; set; }
        public string? ProviderName { get; set; }
        public string? ProviderURL { get; set; }
        public bool IsActive { get; set; }
    }
}
