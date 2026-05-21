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

        Assert.Contains("Risposta JSON OCR non valida", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostic", error.Message, StringComparison.OrdinalIgnoreCase);
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
            return new TempBridge(root, scriptPath, timeout ?? TimeSpan.FromSeconds(5));
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
