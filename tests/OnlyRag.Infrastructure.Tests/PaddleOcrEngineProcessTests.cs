using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Infrastructure.Tests;

public sealed class PaddleOcrEngineProcessTests
{
    [Fact]
    public async Task CheckAvailabilityReadsLargeStderrConcurrently()
    {
        using TempBridge bridge = TempBridge.Create(
            """
            [Console]::Error.Write(('x' * 200000))
            [Console]::Out.Write('{"available":true,"engineVersion":"test-ocr","message":"ok"}')
            """);
        PaddleOcrEngine engine = bridge.CreateEngine();

        OcrEngineAvailability availability = await engine.CheckAvailabilityAsync();

        Assert.True(availability.IsConfigured);
        Assert.Equal("test-ocr", availability.EngineVersion);
    }

    [Fact]
    public async Task CheckAvailabilityResolvesPythonPathForEachAttempt()
    {
        using TempBridge bridge = TempBridge.Create(
            """
            [Console]::Out.Write('{"available":true,"engineVersion":"test-ocr","message":"ok"}')
            """);
        string pythonPath = Path.Combine(bridge.Root, "missing-python.exe");
        PaddleOcrEngine engine = bridge.CreateEngine(() => pythonPath);

        OcrEngineAvailability firstAvailability = await engine.CheckAvailabilityAsync();
        pythonPath = "pwsh";
        OcrEngineAvailability secondAvailability = await engine.CheckAvailabilityAsync();

        Assert.False(firstAvailability.IsConfigured);
        Assert.True(secondAvailability.IsConfigured);
        Assert.Equal("test-ocr", secondAvailability.EngineVersion);
    }

    [Fact]
    public async Task CheckAvailabilityTimesOutHungBridge()
    {
        using TempBridge bridge = TempBridge.Create(
            """
            Start-Sleep -Seconds 30
            """,
            timeout: TimeSpan.FromMilliseconds(250));
        PaddleOcrEngine engine = bridge.CreateEngine();

        OcrEngineAvailability availability = await engine.CheckAvailabilityAsync();

        Assert.False(availability.IsConfigured);
        Assert.Contains("Timeout bridge PaddleOCR", availability.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PaddleOCR non configurato: paddle: No module named 'paddle'")]
    [InlineData("PaddleOCR non configurato: paddlepaddle: not-installed")]
    public async Task CheckAvailabilityExplainsMissingPaddleRuntimeAsInstallRequired(string bridgeMessage)
    {
        using TempBridge bridge = TempBridge.Create(
            $$"""
            [Console]::Out.Write(@'
            {"available":false,"engineVersion":"3.5.0","message":"{{bridgeMessage}}"}
            '@)
            """);
        PaddleOcrEngine engine = bridge.CreateEngine();

        OcrEngineAvailability availability = await engine.CheckAvailabilityAsync("gpu");

        Assert.False(availability.IsConfigured);
        Assert.Contains("Runtime OCR non installato", availability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Installa OCR", availability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paddle.base", availability.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PaddleOCR non configurato: paddle: cannot import name 'backward' from partially initialized module 'paddle.base'")]
    [InlineData("PaddleOCR non configurato: paddle: DLL load failed while importing libpaddle")]
    public async Task CheckAvailabilityExplainsRepairablePaddleRuntimeFailures(string bridgeMessage)
    {
        using TempBridge bridge = TempBridge.Create(
            $$"""
            [Console]::Out.Write(@'
            {"available":false,"engineVersion":"3.5.0","message":"{{bridgeMessage}}"}
            '@)
            """);
        PaddleOcrEngine engine = bridge.CreateEngine();

        OcrEngineAvailability availability = await engine.CheckAvailabilityAsync("gpu");

        Assert.False(availability.IsConfigured);
        Assert.Contains("Runtime OCR locale incompleto o danneggiato", availability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ripara OCR", availability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paddle.base", availability.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecognizeKillsBridgeOnCancellation()
    {
        using TempBridge bridge = TempBridge.Create(
            $$"""
            Start-Sleep -Seconds 3
            Set-Content -LiteralPath '{{TempBridge.PowerShellLiteral(TempBridge.ResolveMarkerPath())}}' -Value 'not-killed'
            [Console]::Out.Write('{"text":"late","boxes":[],"confidence":1.0,"engineVersion":"test"}')
            """);
        PaddleOcrEngine engine = bridge.CreateEngine();
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.RecognizeAsync(
                new OcrRecognitionRequest("prepared.png", "it", OcrSettings.Default),
                cancellation.Token));
        await Task.Delay(800);

        Assert.False(File.Exists(TempBridge.ResolveMarkerPath()));
    }

    [Fact]
    public async Task RecognizeReportsInvalidJson()
    {
        using TempBridge bridge = TempBridge.Create(
            """
            [Console]::Error.Write('diagnostic')
            [Console]::Out.Write('not-json')
            """);
        PaddleOcrEngine engine = bridge.CreateEngine();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.RecognizeAsync(new OcrRecognitionRequest("prepared.png", "it", OcrSettings.Default)));

        Assert.Contains("Invalid OCR JSON response", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostic", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecognizeReadsAsciiEscapedUnicodeJson()
    {
        using TempBridge bridge = TempBridge.Create(
            """
            [Console]::Out.Write('{"text":"\u2202","boxes":[],"confidence":1.0,"engineVersion":"test"}')
            """);
        PaddleOcrEngine engine = bridge.CreateEngine();

        OcrPageResult result = await engine.RecognizeAsync(
            new OcrRecognitionRequest("prepared.png", "en", OcrSettings.Default));

        Assert.Equal("\u2202", result.Text);
    }

    [Fact]
    public async Task RecognizeFiltersBenignNativeDiagnosticsFromBridgeErrors()
    {
        using TempBridge bridge = TempBridge.Create(
            """
            [Console]::Error.WriteLine('INFORMAZIONI: impossibile trovare file corrispondenti ai criteri di ricerca indicati.')
            [Console]::Error.WriteLine('C:\venv\site-packages\paddle\utils\cpp_extension\extension_utils.py:712: UserWarning: No ccache found.')
            [Console]::Error.WriteLine('WARNING: OMP_NUM_THREADS set to 2, not 1.')
            [Console]::Error.WriteLine('Real OCR failure')
            exit 1
            """);
        PaddleOcrEngine engine = bridge.CreateEngine();

        OcrEngineUnavailableException error = await Assert.ThrowsAsync<OcrEngineUnavailableException>(() =>
            engine.RecognizeAsync(new OcrRecognitionRequest("prepared.png", "it", OcrSettings.Default)));

        Assert.Contains("Real OCR failure", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ccache", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OMP_NUM_THREADS", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("impossibile trovare file", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempBridge : IDisposable
    {
        private TempBridge(string root, string scriptPath, TimeSpan timeout)
        {
            Root = root;
            ScriptPath = scriptPath;
            Timeout = timeout;
        }

        public string Root { get; }

        public string ScriptPath { get; }

        public TimeSpan Timeout { get; }

        public static TempBridge Create(string scriptBody, TimeSpan? timeout = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.OcrBridge.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string scriptPath = Path.Combine(root, "bridge.ps1");
            File.WriteAllText(scriptPath, scriptBody, Encoding.UTF8);
            return new TempBridge(root, scriptPath, timeout ?? TimeSpan.FromSeconds(15));
        }

        public PaddleOcrEngine CreateEngine()
        {
            return new PaddleOcrEngine(
                "pwsh",
                ScriptPath,
                Timeout,
                Timeout,
                Timeout);
        }

        public PaddleOcrEngine CreateEngine(Func<string> pythonPathResolver)
        {
            return new PaddleOcrEngine(
                pythonPathResolver,
                ScriptPath,
                Timeout,
                Timeout,
                Timeout);
        }

        public static string ResolveMarkerPath()
        {
            return Path.Combine(Path.GetTempPath(), "OnlyRag.OcrBridge.Tests", "cancel-marker.txt");
        }

        public static string PowerShellLiteral(string value)
        {
            return value.Replace("'", "''", StringComparison.Ordinal);
        }

        public void Dispose()
        {
            string markerPath = ResolveMarkerPath();
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }

            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
