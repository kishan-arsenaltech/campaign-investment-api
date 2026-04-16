namespace Invest.Core.Settings
{
    public class AppSecrets
    {
        public bool IsDevelopment { get; set; } = false;
        public bool IsProduction { get; set; } = false;

        public string DefaultPassword { get; set; } = default!;
        public string MasterPassword { get; set; } = default!;

        public string SqlConnection { get; set; } = default!;

        public string BlobConfiguration { get; set; } = default!;
        public string StorageSecretKey { get; set; } = default!;

        public string JwtIssuer { get; set; } = default!;
        public string JwtSecret { get; set; } = default!;
        public int JwtExpiresIn { get; set; }

        public string StripeSecretKey { get; set; } = default!;
        public string WebhookSecret { get; set; } = default!;

        public string CommunicationServiceConnectionString { get; set; } = default!;
        public string GmailSMTPUser { get; set; } = default!;
        public string GmailSMTPPassword { get; set; } = default!;
        public string SenderAddress { get; set; } = default!;
        public string AdminEmail { get; set; } = default!;
        public string AchAdminEmailListForNewPaymentRequest { get; set; } = default!;
        public string EmailListForScheduler { get; set; } = default!;
        public string RequestOrigin { get; set; } = default!;

        public string CaptchaSecretKey { get; set; } = default!;
        public string ApiAccessToken { get; set; } = default!;
        public string PublicApiToken { get; set; } = default!;
    }
}
