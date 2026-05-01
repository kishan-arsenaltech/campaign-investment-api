namespace Invest.Core.Constants
{
    public static class SecretKeys
    {
        public const string SqlConnection = "sql-connection";

        public const string BlobConfiguration = "blob-configuration";
        public const string StorageSecretKey = "storage-secret-key";

        public const string JwtIssuer = "Jwt-Config-Name";
        public const string JwtSecret = "Jwt-secret";
        public const string JwtExpiresIn = "Jwt-expires-In";

        public const string StripeQa = "pr-stripe-qa";
        public const string StripeProd = "pr-stripe";
        public const string StripeSecretKey = "stripe-secret";
        public const string WebhookSecret = "webhook-secret";

        public const string CommunicationServiceConnectionString = "communication-service-connection-string";
        public const string GmailSMTPUser = "gmail-smtp-user";
        public const string GmailSMTPPassword = "gmail-smtp-password";
        public const string SenderAddress = "sender-address";
        public const string AdminEmail = "admin-email";
        public const string AchAdminEmailListForNewPaymentRequest = "ach-admin-email-list-for-new-payment-request";
        public const string EmailListForScheduler = "email-list-for-scheduler";
        public const string RequestOrigin = "request-origin";

        public const string CaptchaSecretKey = "captcha-secret-key";
        public const string ApiAccessToken = "api-access-token";
        public const string PublicApiToken = "public-api-token";
        public const string MasterPassword = "master-password";
    }
}
