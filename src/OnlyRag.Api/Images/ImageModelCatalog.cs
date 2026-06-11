using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal static class ImageModelCatalog
{
    public const string DefaultModelId = "onlyrag-sdxl-turbo-directml";
    public const string RequiredModelFileName = "model.onnx";
    public const string PlaceholderModelContent = "OnlyRag integrated image model placeholder v1\n";

    private static readonly ImageModelCatalogEntry[] DefaultModels =
    [
        new(
            DefaultModelId,
            "SDXL Turbo Olive ONNX",
            "ONNX ottimizzato Olive per DirectML; richiede pipeline multi-file",
            "https://huggingface.co/softwareweaver/Sdxl-Turbo-Olive-Onnx",
            "CC-BY-NC-4.0 / verificare termini upstream",
            0,
            ["model_index.json"],
            string.Empty,
            IsBuiltIn: true),
        new(
            "onnxruntime-sdxl-turbo-cuda",
            "SDXL Turbo ONNX Runtime CUDA",
            "ONNX Runtime CUDA per GPU NVIDIA; non compatibile con CPU/DirectML",
            "https://huggingface.co/onnxruntime/sdxl-turbo",
            "Stability AI Non-Commercial Community License",
            0,
            ["model_index.json"],
            string.Empty,
            IsBuiltIn: true),
        new(
            "lcm-sdxl-olive-onnx",
            "LCM SDXL Olive ONNX",
            "ONNX ottimizzato Olive per generazione a pochi step; richiede pipeline multi-file",
            "https://huggingface.co/softwareweaver/Latent-Consistency-xl-Olive-Onnx",
            "OpenRAIL++ / verificare termini upstream",
            0,
            ["model_index.json"],
            string.Empty,
            IsBuiltIn: true)
    ];

    public static IReadOnlyList<ImageModelCatalogEntry> ListDefaults() => DefaultModels;

    public static bool IsBuiltIn(string modelId)
    {
        return DefaultModels.Any(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    public static ImageModelCatalogEntry? GetDefault(string modelId)
    {
        return DefaultModels.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }
}
