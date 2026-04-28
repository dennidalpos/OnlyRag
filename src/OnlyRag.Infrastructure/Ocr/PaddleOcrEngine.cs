using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace OnlyRag.Infrastructure.Ocr;

public sealed class PaddleOcrEngine : IOcrEngine
{
    private const string PythonEnvVar = "ONLYRAG_OCR_PYTHON";
    private const string BridgeEnvVar = "ONLYRAG_OCR_BRIDGE";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string pythonPath;
    private readonly string bridgePath;

    public PaddleOcrEngine()
    {
        pythonPath = ResolvePythonPath();
        bridgePath = ResolveBridgePath();
    }

    public string EngineName => "PaddleOCR";

    public string EngineVersion => "paddleocr";

    public string PreprocessVersion => "onlyrag-preprocess-v1";

    public async Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(bridgePath))
        {
            return new OcrEngineAvailability(
                false,
                EngineName,
                EngineVersion,
                $"Bridge OCR non trovato: {bridgePath}.");
        }

        try
        {
            BridgeCheckResponse response = await RunBridgeAsync<BridgeCheckResponse>(
                ["--mode", "check"],
                cancellationToken);
            return new OcrEngineAvailability(
                response.Available,
                EngineName,
                string.IsNullOrWhiteSpace(response.EngineVersion) ? EngineVersion : response.EngineVersion,
                response.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return new OcrEngineAvailability(false, EngineName, EngineVersion, ex.Message);
        }
    }

    public async Task<OcrPagePreparation> PreparePageAsync(
        OcrPagePreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(request.OutputDirectory);

        BridgePrepareResponse response = await RunBridgeAsync<BridgePrepareResponse>(
            [
                "--mode", "prepare",
                "--input", request.SourcePath,
                "--kind", request.SourceKind,
                "--page", request.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--output-dir", request.OutputDirectory,
                "--preprocess-version", request.PreprocessVersion,
                "--dpi", request.Settings.PdfDpi.ToString(CultureInfo.InvariantCulture)
            ],
            cancellationToken);

        return new OcrPagePreparation(
            response.PreparedImagePath,
            response.PageHash,
            response.Width,
            response.Height);
    }

    public async Task<OcrPageResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        BridgeOcrResponse response = await RunBridgeAsync<BridgeOcrResponse>(
            BuildRecognizeArguments(request),
            cancellationToken);

        IReadOnlyList<OcrTextBox> boxes = response.Boxes
            .Select(box => new OcrTextBox(
                box.Text,
                box.Confidence,
                box.Points.Select(point => new OcrPoint(point.X, point.Y)).ToArray()))
            .ToArray();

        return new OcrPageResult(
            response.Text,
            boxes,
            response.Confidence,
            EngineName,
            string.IsNullOrWhiteSpace(response.EngineVersion) ? EngineVersion : response.EngineVersion,
            request.Language);
    }

    internal static string[] BuildRecognizeArguments(OcrRecognitionRequest request)
    {
        OnlyRag.Core.OcrSettings settings = OnlyRag.Core.OcrSettings.Normalize(request.Settings);
        return
        [
            "--mode", "ocr",
            "--input", request.PreparedImagePath,
            "--language", request.Language,
            "--profile", settings.Profile,
            "--model-preset", settings.ModelPreset,
            "--model-version", settings.ModelVersion,
            "--detection-side-limit", settings.DetectionSideLimit.ToString(CultureInfo.InvariantCulture),
            "--detection-threshold", settings.DetectionThreshold.ToString(CultureInfo.InvariantCulture),
            "--detection-box-threshold", settings.DetectionBoxThreshold.ToString(CultureInfo.InvariantCulture),
            "--detection-unclip-ratio", settings.DetectionUnclipRatio.ToString(CultureInfo.InvariantCulture),
            "--recognition-score-threshold", settings.RecognitionScoreThreshold.ToString(CultureInfo.InvariantCulture),
            "--use-textline-orientation", settings.UseTextlineOrientation ? "true" : "false",
            "--use-document-orientation-classification", settings.UseDocumentOrientationClassification ? "true" : "false",
            "--use-document-unwarping", settings.UseDocumentUnwarping ? "true" : "false",
            "--recognition-batch-size", settings.RecognitionBatchSize.ToString(CultureInfo.InvariantCulture),
            "--cpu-threads", settings.CpuThreads.ToString(CultureInfo.InvariantCulture),
            "--device", settings.Device
        ];
    }

    private async Task<T> RunBridgeAsync<T>(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = pythonPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(bridgePath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new()
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                throw new OcrEngineUnavailableException("Impossibile avviare il processo Python OCR.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new OcrEngineUnavailableException(
                $"Python OCR non configurato. Esegui scripts\\Bootstrap-Prerequisites.ps1 oppure imposta {PythonEnvVar}.",
                ex);
        }

        string stdout;
        string stderr;
        try
        {
            stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            throw;
        }

        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new OcrEngineUnavailableException(
                string.IsNullOrWhiteSpace(detail)
                    ? "Bridge PaddleOCR terminato con errore."
                    : detail.Trim());
        }

        T? result = JsonSerializer.Deserialize<T>(stdout, JsonOptions);
        return result ?? throw new InvalidOperationException("Risposta JSON OCR non valida.");
    }

    private static string ResolvePythonPath()
    {
        string? configured = Environment.GetEnvironmentVariable(PythonEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string venvPython = Path.Combine(localAppData, "OnlyRag", "ocr-python", ".venv", "Scripts", "python.exe");
        return File.Exists(venvPython) ? venvPython : "python";
    }

    private static string ResolveBridgePath()
    {
        string? configured = Environment.GetEnvironmentVariable(BridgeEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string outputBridge = Path.Combine(AppContext.BaseDirectory, "scripts", "ocr", "paddle_ocr_bridge.py");
        if (File.Exists(outputBridge))
        {
            return outputBridge;
        }

        string repoBridge = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "ocr", "paddle_ocr_bridge.py"));
        return repoBridge;
    }

    private sealed record BridgeCheckResponse(bool Available, string EngineVersion, string? Message);

    private sealed record BridgePrepareResponse(string PreparedImagePath, string PageHash, int Width, int Height);

    private sealed record BridgeOcrResponse(string Text, BridgeBox[] Boxes, double? Confidence, string EngineVersion);

    private sealed record BridgeBox(string Text, double? Confidence, BridgePoint[] Points);

    private sealed record BridgePoint(double X, double Y);
}
