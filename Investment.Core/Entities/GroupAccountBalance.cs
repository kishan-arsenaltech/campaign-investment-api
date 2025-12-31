namespace Investment.Core.Entities
{
    public class GroupAccountBalance
    {
        public int Id { get; set; }
        public User User { get; set; } = null!;
        public Group Group { get; set; } = null!;
        public decimal Balance { get; set; }
        public DateTime? LastUpdated { get; set; }
    }
}
