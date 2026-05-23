using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace OnlyRag.Infrastructure.Ocr;

public sealed class PaddleOcrEngine : IOcrEngine
{
    private const string PythonEnvVar = "ONLYRAG_OCR_PYTHON";
    private const string BridgeEnvVar = "ONLYRAG_OCR_BRIDGE";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PrepareTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RecognizeTimeout = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string pythonPath;
    private readonly string bridgePath;
    private readonly TimeSpan checkTimeout;
    private readonly TimeSpan prepareTimeout;
    private readonly TimeSpan recognizeTimeout;

    public PaddleOcrEngine()
        : this(ResolvePythonPath(), ResolveBridgePath(), CheckTimeout, PrepareTimeout, RecognizeTimeout)
    {
    }

    internal PaddleOcrEngine(
        string pythonPath,
        string bridgePath,
        TimeSpan checkTimeout,
        TimeSpan prepareTimeout,
        TimeSpan recognizeTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgePath);

        this.pythonPath = pythonPath;
        this.bridgePath = bridgePath;
        this.checkTimeout = ValidateTimeout(checkTimeout, nameof(checkTimeout));
        this.prepareTimeout = ValidateTimeout(prepareTimeout, nameof(prepareTimeout));
        this.recognizeTimeout = ValidateTimeout(recognizeTimeout, nameof(recognizeTimeout));
    }

    public string EngineName => "PaddleOCR";

    public string EngineVersion => "paddleocr";

    public string PreprocessVersion => "onlyrag-preprocess-v1";

    public async Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        return await CheckAvailabilityAsync("cpu", cancellationToken);
    }

    public async Task<OcrEngineAvailability> CheckAvailabilityAsync(
        string device,
        CancellationToken cancellationToken = default)
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
                ["--mode", "check", "--device", NormalizeDevice(device)],
                checkTimeout,
                cancellationToken);
            return new OcrEngineAvailability(
                response.Available,
                EngineName,
                string.IsNullOrWhiteSpace(response.EngineVersion) ? EngineVersion : response.EngineVersion,
                FormatAvailabilityMessage(response.Message),
                response.CompiledWithCuda,
                response.CudaDeviceCount,
                response.ActiveDevice,
                response.PackageVersions);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return new OcrEngineAvailability(false, EngineName, EngineVersion, FormatAvailabilityMessage(ex.Message));
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
            prepareTimeout,
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
            recognizeTimeout,
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
        TimeSpan timeout,
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
                $"Python OCR non configurato. Apri Impostazioni > Diagnostica e usa Configura OCR, oppure imposta {PythonEnvVar}.",
                ex);
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            throw new OcrEngineUnavailableException(
                $"Timeout bridge PaddleOCR dopo {timeout.TotalSeconds:0} secondi.");
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new OcrEngineUnavailableException(
                string.IsNullOrWhiteSpace(detail)
                    ? "Bridge PaddleOCR terminato con errore."
                    : detail.Trim());
        }

        T? result;
        try
        {
            result = JsonSerializer.Deserialize<T>(stdout, JsonOptions);
        }
        catch (JsonException ex)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? "Risposta JSON OCR non valida."
                    : $"Risposta JSON OCR non valida: {detail.Trim()}",
                ex);
        }

        return result ?? throw new InvalidOperationException("Risposta JSON OCR non valida.");
    }

    private static void KillProcessTree(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Il timeout OCR deve essere positivo.");
        }

        return timeout;
    }

    private static string NormalizeDevice(string value)
    {
        return string.Equals(value, "gpu", StringComparison.OrdinalIgnoreCase) ? "gpu" : "cpu";
    }

    private static string? FormatAvailabilityMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        if (IsBrokenPaddleRuntimeMessage(message))
        {
            return "Runtime OCR locale incompleto o danneggiato. " +
                "Apri Impostazioni > Diagnostica e premi Configura OCR per reinstallare PaddleOCR e il runtime PaddlePaddle corretto.";
        }

        return message;
    }

    private static bool IsBrokenPaddleRuntimeMessage(string message)
    {
        return message.Contains("partially initialized module 'paddle", StringComparison.OrdinalIgnoreCase)
            || message.Contains("partially initialized module \"paddle", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot import name 'backward'", StringComparison.OrdinalIgnoreCase)
            || message.Contains("No module named 'paddle'", StringComparison.OrdinalIgnoreCase)
            || message.Contains("paddlepaddle-gpu", StringComparison.OrdinalIgnoreCase)
                && message.Contains("not-installed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("paddlepaddle", StringComparison.OrdinalIgnoreCase)
                && message.Contains("not-installed", StringComparison.OrdinalIgnoreCase);
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

    private sealed record BridgeCheckResponse(
        bool Available,
        string EngineVersion,
        string? Message,
        bool? CompiledWithCuda,
        int? CudaDeviceCount,
        string? ActiveDevice,
        IReadOnlyDictionary<string, string>? PackageVersions);

    private sealed record BridgePrepareResponse(string PreparedImagePath, string PageHash, int Width, int Height);

    private sealed record BridgeOcrResponse(string Text, BridgeBox[] Boxes, double? Confidence, string EngineVersion);

    private sealed record BridgeBox(string Text, double? Confidence, BridgePoint[] Points);

    private sealed record BridgePoint(double X, double Y);
}
