using Microsoft.Extensions.Logging;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Logging;

public sealed class OnlyRagLoggerProvider : ILoggerProvider
{
    private readonly ILoggingService _loggingService;

    public OnlyRagLoggerProvider(ILoggingService loggingService)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new OnlyRagLogger(categoryName, _loggingService);
    }

    public void Dispose() { }

    private sealed class OnlyRagLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _originalCategoryName;
        private readonly ILoggingService _loggingService;
        private readonly bool _isNoisyFrameworkCategory;

        public OnlyRagLogger(string categoryName, ILoggingService loggingService)
        {
            _originalCategoryName = categoryName ?? string.Empty;
            _categoryName = SimplifyCategoryName(categoryName ?? string.Empty);
            _loggingService = loggingService;
            _isNoisyFrameworkCategory = IsNoisyCategory(_originalCategoryName);
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None) return false;

            // Suppress verbose framework logs (CORS, routing, request start/finish, HTTP client)
            // to eliminate ~90% of log volume while keeping Warning/Error for real issues
            if (_isNoisyFrameworkCategory && logLevel < LogLevel.Warning) return false;

            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception == null) return;

            AppLogLevel appLogLevel = logLevel switch
            {
                LogLevel.Trace => AppLogLevel.Trace,
                LogLevel.Debug => AppLogLevel.Debug,
                LogLevel.Information => AppLogLevel.Information,
                LogLevel.Warning => AppLogLevel.Warning,
                LogLevel.Error => AppLogLevel.Error,
                LogLevel.Critical => AppLogLevel.Error,
                _ => AppLogLevel.Information
            };

            _loggingService.Log(appLogLevel, _categoryName, message, exception);
        }

        private static bool IsNoisyCategory(string categoryName)
        {
            return categoryName.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase)
                || categoryName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.OrdinalIgnoreCase)
                || categoryName.StartsWith("System.Net.Http.", StringComparison.OrdinalIgnoreCase);
        }

        private static string SimplifyCategoryName(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return "General";

            if (categoryName.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase))
            {
                string sub = categoryName["Microsoft.AspNetCore.".Length..];
                return $"AspNetCore.{sub}";
            }

            int lastDot = categoryName.LastIndexOf('.');
            return lastDot >= 0 && lastDot < categoryName.Length - 1
                ? categoryName[(lastDot + 1)..].Trim()
                : categoryName.Trim();
        }
    }
}
