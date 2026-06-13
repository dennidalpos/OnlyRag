using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal static class ImageModelCatalog
{
    public const string DefaultModelId = "onlyrag-sdxl-quality-directml";
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
            "SDXL Base qualita",
            "Ritratti, persone e composizioni dove contano coerenza e dettagli; piu lento dei modelli Turbo.",
            "https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0",
            "CreativeML Open RAIL++-M / verificare termini upstream",
            0,
            RequiredSdxlCoreSnapshotFiles,
            string.Empty,
            IsBuiltIn: true),
        new(
            "onlyrag-sdxl-turbo-directml",
            "SDXL Turbo rapido",
            "Bozze veloci, idee, oggetti e ambienti semplici; meno adatto ad anatomia e mani.",
            "https://huggingface.co/softwareweaver/Sdxl-Turbo-Olive-Onnx",
            "CC-BY-NC-4.0 / verificare termini upstream",
            0,
            RequiredSdxlSnapshotFiles,
            string.Empty,
            IsBuiltIn: true),
        new(
            "lcm-sdxl-olive-onnx",
            "LCM SDXL bozza",
            "Iterazioni molto rapide e concept a pochi step; usare per provare prompt prima del modello qualità.",
            "https://huggingface.co/softwareweaver/Latent-Consistency-xl-Olive-Onnx",
            "OpenRAIL++ / verificare termini upstream",
            0,
            RequiredSdxlSnapshotFiles,
            string.Empty,
            IsBuiltIn: true),
        new(
            "ffusion-sdxl-base-directml",
            "FFusionXL creativo",
            "Illustrazioni e scene creative basate su SDXL; verificare stile e licenza del modello upstream.",
            "https://huggingface.co/FFusion/FFusionXL-BASE",
            "OpenRAIL++ / verificare termini upstream",
            0,
            RequiredSdxlSnapshotFiles,
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
