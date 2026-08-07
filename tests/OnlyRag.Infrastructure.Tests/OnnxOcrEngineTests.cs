using OnlyRag.Infrastructure.Ocr;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class OnnxOcrEngineTests
{
    [Fact]
    public async Task OnnxDirectMlOcrEngine_CheckAvailabilityAsync_ReturnsConfigured()
    {
        var engine = new OnnxDirectMlOcrEngine();

        var availability = await engine.CheckAvailabilityAsync("directml");

        Assert.True(availability.IsConfigured);
        Assert.Equal("ONNX DirectML Native C# OCR Engine", availability.EngineName);
        Assert.Contains("DirectML", availability.Message);
    }

    [Fact]
    public async Task OnnxDirectMlOcrEngine_PreparePageAsync_ReturnsPreparedPage()
    {
        var engine = new OnnxDirectMlOcrEngine();
        var request = new OcrPagePreparationRequest("sample_test_path.png", 1);

        var prep = await engine.PreparePageAsync(request);

        Assert.Equal("sample_test_path.png", prep.PreparedImagePath);
        Assert.NotNull(prep.PageHash);
        Assert.True(prep.Width > 0);
        Assert.True(prep.Height > 0);
    }

    [Fact]
    public async Task OnnxDirectMlOcrEngine_RecognizeAsync_ReturnsValidPageResult()
    {
        var engine = new OnnxDirectMlOcrEngine();
        var request = new OcrRecognitionRequest("sample_test_path.png", "it");

        var result = await engine.RecognizeAsync(request);

        Assert.Equal("ONNX DirectML Native C# OCR Engine", result.EngineName);
        Assert.Equal("it", result.Language);
        Assert.True(result.AverageConfidence > 0);
    }
}
