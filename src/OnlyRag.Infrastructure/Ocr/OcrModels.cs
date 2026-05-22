using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Ocr;

public sealed record OcrEngineAvailability(
    bool IsConfigured,
    string EngineName,
    string EngineVersion,
    string? Message,
    bool? CompiledWithCuda = null,
    int? CudaDeviceCount = null,
    string? ActiveDevice = null,
    IReadOnlyDictionary<string, string>? PackageVersions = null);

public sealed record OcrPagePreparationRequest(
    string SourcePath,
    string SourceKind,
    int PageNumber,
    string OutputDirectory,
    string PreprocessVersion,
    OcrSettings Settings);

public sealed record OcrPagePreparation(
    string PreparedImagePath,
    string PageHash,
    int Width,
    int Height);

public sealed record OcrRecognitionRequest(
    string PreparedImagePath,
    string Language,
    OcrSettings Settings);

public sealed record OcrPageResult(
    string Text,
    IReadOnlyList<OcrTextBox> Boxes,
    double? AverageConfidence,
    string EngineName,
    string EngineVersion,
    string Language);

public sealed record OcrTextBox(
    string Text,
    double? Confidence,
    IReadOnlyList<OcrPoint> Points);

public sealed record OcrPoint(double X, double Y);

public sealed class OcrEngineUnavailableException : InvalidOperationException
{
    public OcrEngineUnavailableException(string message)
        : base(message)
    {
    }

    public OcrEngineUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
