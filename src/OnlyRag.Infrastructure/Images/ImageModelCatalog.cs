using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Images;

public static class ImageModelCatalog
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
            DefaultGuidance: 1.0,
            Scheduler: "LCM with trailing timestep spacing",
            CompatibilityNotes: "DirectML GPU preferred on Windows, including NVIDIA GPUs; CPU fallback is supported for slower local validation."),
        new(
            "sdxl-turbo-onnx",
            "SDXL Turbo ONNX",
            "Profilo ultra-veloce (1-4 step) basato su SDXL Turbo per generazioni rapide.",
            "https://huggingface.co/optimum/sdxl-turbo-onnx",
            "OpenRAIL-M",
            8_000_000_000,
            RequiredSdxlSnapshotFiles,
            string.Empty,
            IsBuiltIn: true,
            ModelType: "SDXL Turbo ONNX",
            ModelProfile: "sdxl-turbo",
            SupportedResolutions: ["512x512", "1024x1024"],
            DefaultSteps: 2,
            DefaultGuidance: 0.0,
            Scheduler: "EulerAncestral / Timestep 1",
            CompatibilityNotes: "DirectML GPU consigliato. Genera immagini in pochi secondi con 1-4 step."),
        new(
            "sdxl-base-1.0-onnx",
            "SDXL Base 1.0 ONNX",
            "Modello base SDXL alta fedelta per generazioni dettagliate a 1024x1024.",
            "https://huggingface.co/optimum/sdxl-base-1.0-onnx",
            "OpenRAIL-M",
            12_000_000_000,
            RequiredSdxlSnapshotFiles,
            string.Empty,
            IsBuiltIn: true,
            ModelType: "SDXL Base ONNX",
            ModelProfile: "sdxl-base",
            SupportedResolutions: ["1024x1024", "832x1216", "1216x832"],
            DefaultSteps: 30,
            DefaultGuidance: 5.0,
            Scheduler: "Euler",
            CompatibilityNotes: "Richiede DirectML GPU consigliata con almeno 8GB VRAM.")
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
