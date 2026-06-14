using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal static class ImageModelCatalog
{
    public const string DefaultModelId = "lcm-sdxl-olive-onnx";
    public const string RequiredModelFileName = "model.onnx";
    public const string PlaceholderModelContent = "OnlyRag integrated image model placeholder v1\n";
    public static readonly IReadOnlyList<string> RequiredSdxlCoreSnapshotFiles =
    [
        "model_index.json",
        "scheduler/scheduler_config.json",
        "text_encoder/model.onnx",
        "text_encoder_2/model.onnx",
        "tokenizer/merges.txt",
        "tokenizer/special_tokens_map.json",
        "tokenizer/tokenizer_config.json",
        "tokenizer/vocab.json",
        "tokenizer_2/merges.txt",
        "tokenizer_2/special_tokens_map.json",
        "tokenizer_2/tokenizer_config.json",
        "tokenizer_2/vocab.json",
        "unet/model.onnx",
        "vae_decoder/model.onnx",
        "vae_encoder/model.onnx"
    ];

    public static readonly IReadOnlyList<string> RequiredSdxlSnapshotFiles =
    [
        ..RequiredSdxlCoreSnapshotFiles,
        "text_encoder_2/model.onnx.data",
        "unet/model.onnx.data"
    ];

    private static readonly ImageModelCatalogEntry[] DefaultModels =
    [
        new(
            DefaultModelId,
            "LCM SDXL Olive ONNX",
            "Profilo ONNX DirectML/CPU locale per qualita, bilanciato e performance.",
            "https://huggingface.co/softwareweaver/Latent-Consistency-xl-Olive-Onnx",
            "OpenRAIL++ / verificare termini upstream",
            8_000_000_000,
            RequiredSdxlSnapshotFiles,
            string.Empty,
            IsBuiltIn: true,
            ModelType: "SDXL Turbo/LCM ONNX",
            ModelProfile: "lcm-sdxl-olive",
            SupportedResolutions: ["1024x1024", "832x1216", "1216x832"],
            DefaultSteps: 6,
            DefaultGuidance: 0,
            Scheduler: "Euler Ancestral with trailing timestep spacing",
            CompatibilityNotes: "DirectML GPU preferred on Windows, including NVIDIA GPUs; CPU fallback is supported for slower local validation.")
    ];

    private static readonly HashSet<string> ObsoleteBuiltInModelIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "onlyrag-sdxl-quality-directml",
        "onlyrag-sdxl-turbo-directml",
        "ffusion-sdxl-base-directml",
        "onnxruntime-sdxl-turbo-cuda"
    };

    public static IReadOnlyList<ImageModelCatalogEntry> ListDefaults() => DefaultModels;

    public static bool IsBuiltIn(string modelId)
    {
        return DefaultModels.Any(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    public static ImageModelCatalogEntry? GetDefault(string modelId)
    {
        return DefaultModels.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsObsoleteBuiltIn(string modelId)
    {
        return ObsoleteBuiltInModelIds.Contains(modelId);
    }
}
