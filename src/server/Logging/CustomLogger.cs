using Microsoft.Extensions.Logging;

namespace server.Logging;

public class CustomLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ILoggerProvider _provider;
    private readonly IExternalScopeProvider? _scopeProvider;

    public CustomLogger(string categoryName, ILoggerProvider provider, IExternalScopeProvider? scopeProvider = null)
    {
        _categoryName = categoryName;
        _provider = provider;
        _scopeProvider = scopeProvider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider?.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // Accept all log levels except None
        // The logging framework will filter based on configuration
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        
        // Always log - let the framework handle filtering via IsEnabled
        // But we check IsEnabled here as an extra safeguard
        if (!IsEnabled(logLevel))
        {
            //Console.WriteLine($"[CustomLogger.Log] Filtered out (IsEnabled returned false)");
            return;
        }

        var message = formatter(state, exception);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff Z");
        var category = _categoryName;

        string logLevelString = logLevel switch {
            LogLevel.Error =>       "[   ERROR   ]",
            LogLevel.Critical =>    "[  CRITICAL ]",
            LogLevel.Warning =>     "[  WARNING  ]",
            LogLevel.Information => "[INFORMATION]",
            LogLevel.Debug =>       "[   DEBUG   ]",
            LogLevel.Trace =>       "[   TRACE   ]"
        };

        // Custom logging logic here
        // You can write to file, database, external service, etc.
        // Also write to standard error for errors and critical messages
        Console.Write($"[{timestamp}] ");
        if (logLevel >= LogLevel.Error)
        {
            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(logLevelString);
            Console.ForegroundColor = color;
        }
        else
        {
            Console.Write(logLevelString);
        }
        Console.WriteLine($"             {message}]");


        if (exception != null)
        {
            //Console.WriteLine($"\nException: {exception}");
        }



        
    }


}

