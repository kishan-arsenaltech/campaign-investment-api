// Ignore Spelling: Middleware Middlewares Api

using Invest.Core.Settings;
using Investment.Extensions;

namespace Invest.Middlewares
{
    public class ApiAccessTokenMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AppSecrets _appSecrets;

        public ApiAccessTokenMiddleware(RequestDelegate next, AppSecrets appSecrets)
        {
            _next = next;
            _appSecrets = appSecrets;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (_appSecrets.IsDevelopment
                || context.Request.Path.StartsWithSegments("/api/Payment/stripe-webhook", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                if (!context.Request.Headers.TryGetValue("api-access-token", out var token) || token != _appSecrets.ApiAccessToken)
                    return;
            }

            await _next(context);
        }
    }
}
