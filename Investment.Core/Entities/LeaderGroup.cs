namespace Investment.Core.Entities
{
    public class LeaderGroup
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;
        public int GroupId { get; set; }
        public Group Group { get; set; } = null!;
    }
}
