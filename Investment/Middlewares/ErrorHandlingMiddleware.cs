// Ignore Spelling: Middlewares Middleware

using System.Diagnostics;
using System.Text.Json;

namespace Invest.Middlewares
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;
        private readonly IWebHostEnvironment _environment;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger, IWebHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
                var method = context.Request.Method;
                var path = context.Request.Path;
                var user = context.User?.Identity?.IsAuthenticated == true
                                        ? context.User.Identity?.Name ?? "anonymous"
                                        : "anonymous";
                var environmentName = _environment.EnvironmentName ?? "unknown";

                var routeData = context.GetRouteData();
                var controller = routeData?.Values["controller"]?.ToString() ?? "unknown";
                var action = routeData?.Values["action"]?.ToString() ?? "unknown";

                var parameters = new Dictionary<string, string>();

                foreach (var (key, value) in context.Request.Query)
                    parameters[$"query:{key}"] = value!;

                foreach (var (key, value) in routeData!.Values)
                    parameters[$"route:{key}"] = value?.ToString() ?? string.Empty;

                string requestBody = null!;
                if (method is "POST" or "PUT")
                {
                    context.Request.EnableBuffering();
                    using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                    requestBody = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;
                }

                using (_logger.BeginScope(new Dictionary<string, object>
                {
                    ["RequestPath"] = path,
                    ["RequestMethod"] = method,
                    ["Controller"] = controller,
                    ["Action"] = action,
                    ["User"] = user,
                    ["TraceId"] = traceId,
                    ["Parameters"] = JsonSerializer.Serialize(parameters),
                    ["RequestBody"] = requestBody,
                    ["Environment"] = environmentName
                }))
                {
                    _logger.LogError(ex, "Unhandled exception occurred");
                }

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "An unexpected error occurred.",
                    traceId = traceId
                }));
            }
        }
    }
}
