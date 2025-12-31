namespace Investment.Core.Entities
{
    public class UsersNotification
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string UrlToRedirect { get; set; } = string.Empty;
        public bool isRead { get; set; }
        public string? PictureFileName { get; set; }
        public User TargetUser { get; set; } = null!;
    }
}
