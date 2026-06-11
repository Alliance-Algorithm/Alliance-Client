using Alliance.Client.Features.Settings;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Infrastructure.Logging;

public sealed class StartupLogger
{
    private readonly ILogger<StartupLogger> _logger;

    public StartupLogger(ILogger<StartupLogger> logger)
    {
        _logger = logger;
    }

    public void LogFrameworkReady(AppSettings settings)
    {
        _logger.LogInformation(
            "Starting {ApplicationName}. Debug mode: {DebugMode}. External integrations remain disabled in scaffold mode.",
            settings.ApplicationName,
            settings.EnableDebugMode);
    }
}
