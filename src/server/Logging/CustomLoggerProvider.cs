using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace server.Logging;

public class CustomLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();
    private readonly IExternalScopeProvider? _scopeProvider;

    public CustomLoggerProvider(IExternalScopeProvider? scopeProvider = null)
    {
        _scopeProvider = scopeProvider;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new CustomLogger(name, this, _scopeProvider));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}

