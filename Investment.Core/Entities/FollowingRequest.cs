namespace Investment.Core.Entities
{
    public class FollowingRequest
    {
        public int Id { get; set; }
        public User? RequestOwner { get; set; }
        public User? UserToFollow { get; set; }
        public Group? GroupToFollow { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; } = DateTime.Now;
    }
}
