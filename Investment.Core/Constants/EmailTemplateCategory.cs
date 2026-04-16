using System.ComponentModel.DataAnnotations;

namespace Investment.Core.Constants
{
    public enum EmailTemplateCategory
    {
        [Display(Name = "Welcome Anonymous User")]
        WelcomeAnonymousUser = 1,

        [Display(Name = "Welcome Registered User")]
        WelcomeRegisteredUser = 2,

        [Display(Name = "Password Reset")]
        PasswordReset = 3,

        [Display(Name = "DAF Donation Instructions")]
        DAFDonationInstructions = 4,

        [Display(Name = "Foundation Donation Instructions")]
        FoundationDonationInstructions = 5,

        [Display(Name = "Donation Receipt")]
        DonationReceipt = 6,

        [Display(Name = "ACH Payment Request")]
        ACHPaymentRequest = 7,

        [Display(Name = "Donation Confirmation")]
        DonationConfirmation = 8,

        [Display(Name = "Grant Received")]
        GrantReceived = 9,

        [Display(Name = "DAF Reminder (Day 3)")]
        DAFReminderDay3 = 10,

        [Display(Name = "Foundation Reminder (Week 2)")]
        FoundationReminderWeek2 = 11,

        [Display(Name = "Group Investment Notification")]
        GroupInvestmentNotification = 12,

        [Display(Name = "Investment Activity Notification")]
        InvestmentActivityNotification = 13,

        [Display(Name = "Follower Influence Notification")]
        FollowerInfluenceNotification = 14,

        [Display(Name = "Campaign Owner Funding Notification")]
        CampaignOwnerFundingNotification = 15,

        [Display(Name = "Investment Under Review")]
        InvestmentUnderReview = 16,

        [Display(Name = "Investment QR Code")]
        InvestmentQRCode = 17,

        [Display(Name = "Investment Note Mention")]
        InvestmentNoteMention = 18,

        [Display(Name = "Investment Approved")]
        InvestmentApproved = 19,

        [Display(Name = "Compliance Review Notification")]
        ComplianceReviewNotification = 20,

        [Display(Name = "Investment Published")]
        InvestmentPublished = 21,

        [Display(Name = "Disbursement Request")]
        DisbursementRequest = 22,

        [Display(Name = "Investment Submission Notification")]
        InvestmentSubmissionNotification = 23,

        [Display(Name = "Pending Grant Notification")]
        PendingGrantNotification = 24,

        [Display(Name = "ACH Failure Notification")]
        ACHFailureNotification = 25,

        [Display(Name = "ACH Payment Request (Admin)")]
        ACHPaymentRequestAdmin = 26,

        [Display(Name = "Asset Donation Request")]
        AssetDonationRequest = 27,

        [Display(Name = "Group Join Request Notification")]
        GroupJoinRequestNotification = 28,

        [Display(Name = "DAF Donation Instructions ImpactAssets")]
        DAFDonationInstructionsImpactAssets = 29,

        [Display(Name = "Campaign Investment Notification")]
        CampaignInvestmentNotification = 30,

        [Display(Name = "DAF Reminder ImpactAssets (Day 3)")]
        DAFReminderImpactAssetsDay3 = 31,

        [Display(Name = "DAF Reminder ImpactAssets (Week 2)")]
        DAFReminderImpactAssetsWeek2 = 32,

        [Display(Name = "DAF Reminder (Week 2)")]
        DAFReminderWeek2 = 33,

        [Display(Name = "Foundation Reminder (Day 3)")]
        FoundationReminderDay3 = 34,

        [Display(Name = "Login Verification Code")]
        TwoFactorAuthentication = 35,
    }
}
