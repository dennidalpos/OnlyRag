namespace OnlyRag.Core;

public enum AppLogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    None = 5
}

public sealed record LoggingSettings(
    AppLogLevel MinLevel = AppLogLevel.Trace);
