// Ignore Spelling: Middlewares Middleware

using Invest.Core.Entities;
using Investment.Repo.Context;
using System.Diagnostics;
using System.Text.Json;
using UAParser;

namespace Invest.Middlewares
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IServiceScopeFactory _scopeFactory;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger, IWebHostEnvironment environment, IServiceScopeFactory scopeFactory)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
            _scopeFactory = scopeFactory;
        }

        public async Task Invoke(HttpContext context)
        {
            string? requestBody = null;

            context.Request.EnableBuffering();

            if (context.Request.ContentLength > 0 &&
                (context.Request.Method == "POST" ||
                 context.Request.Method == "PUT" ||
                 context.Request.Method == "PATCH"))
            {
                using var reader = new StreamReader(
                    context.Request.Body,
                    encoding: System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);

                var rawBody = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;

                requestBody = SanitizeRequestBody(rawBody);
            }

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

                if (routeData != null)
                    foreach (var (key, value) in routeData!.Values)
                        parameters[$"route:{key}"] = value?.ToString() ?? string.Empty;

                var (clientIp, proxyChain) = GetIpInfo(context);

                var (country, region) = await GetGeoInfo(clientIp);

                var (os, deviceType, browser) = GetUserDeviceInfo(context);

                await SaveErrorToDatabase(new ApiErrorLog
                {
                    Message = ex.Message,
                    InnerExceptionMessage = GetInnerExceptionMessages(ex),
                    StackTrace = ex.StackTrace,
                    InnerExceptionStackTrace = GetInnerExceptionStackTraces(ex),
                    Path = path,
                    Method = method,
                    Controller = controller,
                    Action = action,
                    UserName = user,
                    OperatingSystem = os,
                    DeviceType = deviceType,
                    Browser = browser,
                    ClientIp = clientIp,
                    ProxyIpChain = !string.IsNullOrEmpty(proxyChain) ? proxyChain : null,
                    TraceId = traceId,
                    Parameters = JsonSerializer.Serialize(parameters),
                    RequestBody = requestBody,
                    Environment = environmentName,
                    CreatedAt = DateTime.Now
                });

                using (_logger.BeginScope(new Dictionary<string, object>
                {
                    ["Path"] = path,
                    ["Method"] = method,
                    ["Controller"] = controller,
                    ["Action"] = action,
                    ["UserName"] = user,
                    ["TraceId"] = traceId,
                    ["Parameters"] = JsonSerializer.Serialize(parameters),
                    ["RequestBody"] = requestBody ?? "",
                    ["Environment"] = environmentName,
                    ["ClientIp"] = clientIp ?? "",
                    ["ProxyIpChain"] = proxyChain ?? "",
                    ["OperatingSystem"] = os ?? "",
                    ["DeviceType"] = deviceType ?? "",
                    ["Browser"] = browser ?? ""
                }))
                {
                    _logger.LogError(ex, ex.Message);
                }

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    Success = false,
                    Message = $"An unexpected error occurred: {traceId}",
                }));
            }
        }

        private static string? GetInnerExceptionMessages(Exception ex)
        {
            if (ex == null) return null;

            var messages = new List<string>();
            var current = ex.InnerException;

            while (current != null)
            {
                messages.Add(current.Message);
                current = current.InnerException;
            }

            return string.Join(" --> ", messages);
        }

        private static string? GetInnerExceptionStackTraces(Exception ex)
        {
            if (ex == null) return null;

            var stacks = new List<string>();
            var current = ex.InnerException;

            while (current != null)
            {
                stacks.Add(current.StackTrace ?? "");
                current = current.InnerException;
            }

            return string.Join("\n\n--- INNER ---\n\n", stacks);
        }

        private (string? OS, string? DeviceType, string? Browser) GetUserDeviceInfo(HttpContext context)
        {
            var userAgent = context.Request.Headers["User-Agent"]
                                   .FirstOrDefault();

            if (string.IsNullOrEmpty(userAgent))
                return (null, null, null);

            var parser = Parser.GetDefault();
            ClientInfo client = parser.Parse(userAgent);

            var os = $"{client.OS.Family} {client.OS.Major}";
            var browser = $"{client.UA.Family} {client.UA.Major}";

            string deviceType;

            if (client.Device.IsSpider)
                deviceType = "Bot";
            else if (client.Device.Family.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
                deviceType = "Mobile";
            else if (client.Device.Family.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
                deviceType = "Tablet";
            else
                deviceType = "Desktop";

            return (os, deviceType, browser);
        }

        private async Task<(string? Country, string? Region)> GetGeoInfo(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return (null, null);

            if (IsPrivateIp(ip))
                return ("Local/Private", "Local/Private");

            try
            {
                using var client = new HttpClient();

                var json = await client.GetStringAsync(
                    $"https://ip-api.com/json/{ip}?fields=status,country,regionName,message");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var status = root.GetProperty("status").GetString();

                if (status != "success")
                    return (null, null);

                var country = root.GetProperty("country").GetString();
                var region = root.GetProperty("regionName").GetString();

                return (country, region);
            }
            catch
            {
                return (null, null);
            }
        }

        private bool IsPrivateIp(string ip)
        {
            return ip.StartsWith("127.") ||
                   ip == "::1" ||
                   ip.StartsWith("10.") ||
                   ip.StartsWith("192.168.") ||
                   ip.StartsWith("172.16.") ||
                   ip.StartsWith("172.17.") ||
                   ip.StartsWith("172.18.") ||
                   ip.StartsWith("172.19.") ||
                   ip.StartsWith("172.2") ||
                   ip.StartsWith("172.30.") ||
                   ip.StartsWith("172.31.");
        }

        private (string? ClientIp, string? ProxyChain) GetIpInfo(HttpContext context)
        {
            var headers = context.Request.Headers;

            string? ip = headers["CF-Connecting-IP"].FirstOrDefault()
                      ?? headers["X-Real-IP"].FirstOrDefault()
                      ?? headers["X-Forwarded-For"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(ip))
            {
                var ips = ip.Split(',')
                            .Select(x => CleanIp(x.Trim()))
                            .ToList();

                return (ips.FirstOrDefault(), string.Join(" → ", ips.Skip(1)));
            }

            var remoteIp = context.Connection.RemoteIpAddress?.ToString();

            return (CleanIp(remoteIp), null);
        }

        private string? CleanIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return ip;

            if (ip.StartsWith("["))
            {
                var end = ip.IndexOf("]");
                if (end > 0)
                    return ip[1..end];
            }

            var colonIndex = ip.IndexOf(":");

            if (colonIndex > -1)
                return ip[..colonIndex];

            return ip;
        }

        private string SanitizeRequestBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                return body;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var cleaned = SanitizeElement(doc.RootElement);

                return JsonSerializer.Serialize(cleaned,
                new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false
                });
            }
            catch
            {
                return body.Length > 5000
                        ? body.Substring(0, 5000) + "...[TRUNCATED]"
                        : body;
            }
        }

        private object? SanitizeElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:

                    var dict = new Dictionary<string, object?>();

                    foreach (var prop in element.EnumerateObject())
                    {
                        if (IsSensitiveField(prop.Name))
                            dict[prop.Name] = "[MASKED]";
                        else if (IsBase64Field(prop.Name, prop.Value))
                            dict[prop.Name] = "[BASE64_SKIPPED]";
                        else
                            dict[prop.Name] = SanitizeElement(prop.Value);
                    }

                    return dict;

                case JsonValueKind.Array:
                    return element.EnumerateArray().Select(SanitizeElement).ToList();

                case JsonValueKind.String:
                    return element.GetString();

                case JsonValueKind.Number:
                    return element.GetRawText();

                case JsonValueKind.True:
                case JsonValueKind.False:
                    return element.GetBoolean();

                default:
                    return null;
            }
        }

        private bool IsSensitiveField(string name)
        {
            var lower = name.ToLower();

            return lower.Contains("password") ||
                   lower.Contains("token") ||
                   lower.Contains("secret") ||
                   lower.Contains("apikey") ||
                   lower.Contains("authorization");
        }

        private bool IsBase64Field(string name, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String)
                return false;

            var str = value.GetString();

            if (string.IsNullOrWhiteSpace(str))
                return false;

            if (str.Length < 500)
                return false;

            if (str.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var commaIndex = str.IndexOf(',');
                if (commaIndex >= 0)
                    str = str[(commaIndex + 1)..];
            }

            if (str.Length % 4 != 0)
                return false;

            try
            {
                Span<byte> buffer = new Span<byte>(new byte[str.Length]);
                return Convert.TryFromBase64String(str, buffer, out _);
            }
            catch
            {
                return false;
            }
        }

        private async Task SaveErrorToDatabase(ApiErrorLog log)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

                db.ApiErrorLog.Add(log);
                await db.SaveChangesAsync();
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Failed to save error log to database");
            }
        }
    }
}
