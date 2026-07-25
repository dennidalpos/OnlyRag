using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Logging;

public sealed class LoggingSettingsStore
{
    private const string SettingKey = "logging.logLevel";
    private readonly ISettingsRepository settingsRepository;

    public LoggingSettingsStore(ISettingsRepository settingsRepository)
    {
        this.settingsRepository = settingsRepository;
    }

    public async Task<LoggingSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        string? value = await settingsRepository.GetValueAsync(SettingKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            return new LoggingSettings(AppLogLevel.Trace);
        }

        if (Enum.TryParse<AppLogLevel>(value, ignoreCase: true, out var level))
        {
            return new LoggingSettings(level);
        }

        return new LoggingSettings(AppLogLevel.Trace);
    }

    public async Task SaveAsync(LoggingSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await settingsRepository.UpsertAsync(SettingKey, settings.MinLevel.ToString(), cancellationToken);
    }
}
