namespace Investment.Core.Dtos
{
    public class UserDetailDto
    {
        public string Id { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string PictureFileName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsFollowing { get; set; }
        public bool IsFollowPending { get; set; }
        public bool IsOwner { get; set; }
    }
}
