// Ignore Spelling: Middleware Middlewares Api

using Investment.Extensions;

namespace Invest.Middlewares
{
    public class ApiAccessTokenMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly KeyVaultConfigService _keyVaultConfigService;

        public ApiAccessTokenMiddleware(RequestDelegate next, KeyVaultConfigService keyVaultConfigService)
        {
            _next = next;
            _keyVaultConfigService = keyVaultConfigService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string apiAccessToken = _keyVaultConfigService.GetApiAccessToken();

            if (context.Request.Path.StartsWithSegments("/api/Payment/stripe-webhook", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                if (!context.Request.Headers.TryGetValue("api-access-token", out var token) || token != apiAccessToken)
                {
                    return;
                }
            }

            await _next(context);
        }
    }
}
