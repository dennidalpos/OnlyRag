namespace OnlyRag.Core;

public sealed record DiagnosticsResponse(
    string AppVersion,
    string DatabasePath,
    string LogsDirectory,
    string OllamaStatus,
    bool OllamaIsReachable,
    string OcrStatus,
    bool OcrIsConfigured,
    string OcrEngineName);
