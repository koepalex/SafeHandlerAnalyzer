using Microsoft.Extensions.Logging;

namespace SafeHandleAnalyzer.Configuration;

internal static class LoggingProvider
{
    internal static ILoggerFactory GetLoggerFactory(LogLevel level)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddConsole()
                .SetMinimumLevel(level));

        return loggerFactory;
    }
}