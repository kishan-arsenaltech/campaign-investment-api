using Investment.Service.Interfaces;
using NLog;

namespace Investment.Service.Services
{
    public class LoggerManager : ILoggerManager
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        public void LogInfo(string message) => _logger.Info(message);
        public void LogError(string message) => _logger.Error(message);
        public void LogDebug(string message) => _logger.Debug(message);
        public void LogWarn(string message) => _logger.Warn(message);
    }
}
