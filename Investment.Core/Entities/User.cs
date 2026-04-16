using Investment.Core.Dtos;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace Investment.Core.Entities
{
    public class User : IdentityUser, IBaseEntity
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public decimal? AccountBalance { get; set; }
        public string? PictureFileName { get; set; }
        public string? Address { get; set; }
        public bool? IsApprouveRequired { get; set; } = false;
        public bool? IsUserHidden { get; set; } = false;
        public bool? EmailFromGroupsOn { get; set; } = false;
        public bool? EmailFromUsersOn { get; set; } = false;
        public bool? OptOutEmailNotifications { get; set; } = false;
        public bool? IsActive { get; set; } = false;
        public DateTime? DateCreated { get; set; }
        public bool? IsFreeUser { get; set; } = false;
        public bool? IsAnonymousInvestment { get; set; }
        public bool ConsentToShowAvatar { get; set; } = true;
        public bool IsExcludeUserBalance { get; set; } = false;
        public string? AlternateEmail { get; set; }
        public string? ZipCode { get; set; }

        public bool IsDeleted { get; set; } = false;
        public string? DeletedBy { get; set; }
        public User? DeletedByUser { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime? DeletedAt { get; set; }

        public ICollection<Group>? Groups { get; set; }
        public ICollection<FollowingRequest>? Requests { get; set; }
        public ICollection<FollowingRequest>? RequestsToAccept { get; set; }
        public ICollection<UsersNotification>? Notifications { get; set; }
        public InvestmentFeedback? InvestmentFeedbacks { get; set; }
        [NotMapped]
        public GroupAccountBalanceDto? GroupAccountBalance { get; set; }

        public ICollection<LeaderGroup> LeaderGroups { get; set; } = new List<LeaderGroup>();
        public ICollection<GroupAccountBalance>? GroupBalances { get; set; } = new List<GroupAccountBalance>();
    }
}
