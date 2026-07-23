namespace OnlyRag.Core;

public sealed record CodingTaskRequest(
    string Prompt,
    string? Model = null,
    string? Persona = "architect",
    string? Language = "csharp",
    string? CodeContext = null,
    string? TargetFilePath = null,
    string? Mode = "write",
    string? WorkspaceSummary = null);

public sealed record CodingTaskResponse(
    string GeneratedCode,
    string Explanation,
    string Language,
    string? TargetFilePath = null,
    IReadOnlyList<string>? ExecutionSuggestions = null);

public sealed record CodeRefactorRequest(
    string OriginalCode,
    string Goal,
    string? Model = null,
    string? Language = "csharp",
    string? Instructions = null);

public sealed record CodeRefactorResponse(
    string OriginalCode,
    string ModifiedCode,
    string Explanation,
    string Language);

public sealed record CodeDiagnoseRequest(
    string ErrorLog,
    string? Model = null,
    string? CodeContext = null,
    string? Language = "csharp");

public sealed record CodeDiagnoseResponse(
    string RootCauseAnalysis,
    string SuggestedFixCode,
    string FixedCodeDiff,
    string Language);
