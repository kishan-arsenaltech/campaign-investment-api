namespace Invest.Core.Entities
{
    public class ApiErrorLog
    {
        public int Id { get; set; }
        public string? Message { get; set; }
        public string? InnerExceptionMessage { get; set; }
        public string? StackTrace { get; set; }
        public string? InnerExceptionStackTrace { get; set; }
        public string? Path { get; set; }
        public string? Method { get; set; }
        public string? Controller { get; set; }
        public string? Action { get; set; }
        public string? Parameters { get; set; }
        public string? RequestBody { get; set; }
        public string? UserName { get; set; }
        public string? OperatingSystem { get; set; }
        public string? DeviceType { get; set; }
        public string? Browser { get; set; }
        public string? Country { get; set; }
        public string? Region { get; set; }
        public string? ClientIp { get; set; }
        public string? ProxyIpChain { get; set; }
        public string? Environment { get; set; }
        public string? TraceId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
