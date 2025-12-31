namespace Investment.Extensions
{
    public class KeyVaultConfigService
    {
        //private readonly IConfiguration _configuration;
        //private readonly SecretClient _client;
        private readonly Dictionary<string, string> _secretsValue = new Dictionary<string, string>();
        private readonly string _environmentName;

        //public KeyVaultConfigService(IConfiguration configuration)
        //{
        //    _configuration = configuration;

        //    var vaultName = _configuration["AzureKeyVault:Vault"];
        //    var tenantId = _configuration["AzureKeyVault:TenantId"];
        //    var clientId = _configuration["AzureKeyVault:ClientId"];
        //    var clientSecret = _configuration["AzureKeyVault:ClientSecret"];

        //    _environmentName = _configuration["environment:name"];

        //    var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        //    _client = new SecretClient(new Uri(vaultName), credential);
        //}

        //public async Task InitializeAsync()
        //{
        //    var secretKeys = new List<string>
        //    {
        //        $"{_environmentName}-sql-connection",
        //        $"{_environmentName}-blob-configuration",
        //        $"{_environmentName}-Jwt-Config-Name",
        //        $"{_environmentName}-Jwt-secret",
        //        $"{_environmentName}-Jwt-expires-In",
        //        $"{_environmentName}-admin-email",
        //        $"{_environmentName}-communication-service-connection-string",
        //        $"{_environmentName}-sender-address",
        //        $"{_environmentName}-api-access-token",
        //        $"{_environmentName}-webhook-secret",
        //        "pr-stripe-qa",
        //        "pr-stripe",
        //        "klaviyo-api-key",
        //        "klaviyo-list-key",
        //        "captcha-secret-key"
        //    };

        //    foreach (var key in secretKeys)
        //    {
        //        var secretValue = (await _client.GetSecretAsync(key)).Value.Value;
        //        _secretsValue[key] = secretValue;
        //    }
        //}

        public KeyVaultConfigService(string environmentName = "dev")
        {
            _environmentName = environmentName;

            _secretsValue[$"{_environmentName}-blob-configuration"] = "StaticBlobConfig";
            _secretsValue[$"{_environmentName}-Jwt-Config-Name"] = "StaticJwtConfigName";
            _secretsValue[$"{_environmentName}-Jwt-secret"] = "StaticJwtSecret";
            _secretsValue[$"{_environmentName}-Jwt-expires-In"] = "604800";
            _secretsValue[$"{_environmentName}-admin-email"] = "admin@example.com";
            _secretsValue[$"{_environmentName}-communication-service-connection-string"] = "StaticCommunicationConnectionString";
            _secretsValue[$"{_environmentName}-sender-address"] = "sender@example.com";
            _secretsValue[$"{_environmentName}-api-access-token"] = "StaticApiAccessToken";
            _secretsValue[$"{_environmentName}-webhook-secret"] = "StaticWebhookSecret";
            _secretsValue["pr-stripe-qa"] = "StaticStripeQaSecret";
            _secretsValue["pr-stripe"] = "StaticStripeProdSecret";
            _secretsValue["klaviyo-api-key"] = "StaticKlaviyoApiKey";
            _secretsValue["klaviyo-list-key"] = "StaticKlaviyoListKey";
            _secretsValue["captcha-secret-key"] = "StaticCaptchaSecret";
        }

        public string GetSqlConnectionString()
        {
            return "Server=DESKTOP-UCJPFFR\\SQLEXPRESS;Initial Catalog=InvestmentManagement;Persist Security Info=False;TrustServerCertificate=True;Connection Timeout=30;Integrated Security=True;";
        }
        public string GetBlobConfiguration() => _secretsValue[$"{_environmentName}-blob-configuration"];
        public string GetBlobContainer() => $"{_environmentName}container";
        public string GetJwtConfigName() => _secretsValue[$"{_environmentName}-Jwt-Config-Name"];
        public string GetJwtSecret() => _secretsValue[$"{_environmentName}-Jwt-secret"];
        public string GetJwtExpiresIn() => _secretsValue[$"{_environmentName}-Jwt-expires-In"];
        public string GetAdminEmail() => _secretsValue[$"{_environmentName}-admin-email"];
        public string GetCommunicationServiceConnectionString() => _secretsValue[$"{_environmentName}-communication-service-connection-string"];
        public string GetSenderAddress() => _secretsValue[$"{_environmentName}-sender-address"];
        public string GetApiAccessToken() => _secretsValue[$"{_environmentName}-api-access-token"];
        public string GetStripeSecretKey()
        {
            string secretKeyName = _environmentName switch
            {
                "qa" => "pr-stripe-qa",
                "prod" => "pr-stripe",
                _ => throw new InvalidOperationException($"Unsupported environment: {_environmentName}")
            };

            if (_secretsValue.TryGetValue(secretKeyName, out var secretKey))
            {
                return secretKey;
            }
            else
            {
                throw new KeyNotFoundException($"Stripe Secret Key '{secretKeyName}' not found in Azure Key Vault.");
            }
        }
        public string GetKlaviyoApiKey() => _secretsValue["klaviyo-api-key"];
        public string GetKlaviyoListKey() => _secretsValue["klaviyo-list-key"];
        public string GetCaptchaSecretKey() => _secretsValue["captcha-secret-key"];
        public string GetWebhookSecret() => _secretsValue[$"{_environmentName}-webhook-secret"];
    }
}
