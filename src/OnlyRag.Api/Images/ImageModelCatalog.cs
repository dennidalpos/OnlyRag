using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal static class ImageModelCatalog
{
    public const string DefaultModelId = "onlyrag-sdxl-turbo-directml";
    public const string RequiredModelFileName = "model.onnx";
    public const string PlaceholderModelContent = "OnlyRag integrated image model placeholder v1\n";
    public static readonly IReadOnlyList<string> RequiredSdxlSnapshotFiles =
    [
        "model_index.json",
        "scheduler/scheduler_config.json",
        "text_encoder/model.onnx",
        "text_encoder_2/model.onnx",
        "text_encoder_2/model.onnx.data",
        "tokenizer/merges.txt",
        "tokenizer/special_tokens_map.json",
        "tokenizer/tokenizer_config.json",
        "tokenizer/vocab.json",
        "tokenizer_2/merges.txt",
        "tokenizer_2/special_tokens_map.json",
        "tokenizer_2/tokenizer_config.json",
        "tokenizer_2/vocab.json",
        "unet/model.onnx",
        "unet/model.onnx.data",
        "vae_decoder/model.onnx",
        "vae_encoder/model.onnx"
    ];

    private static readonly ImageModelCatalogEntry[] DefaultModels =
    [
        new(
            DefaultModelId,
            "SDXL Turbo Olive ONNX",
            "ONNX ottimizzato Olive per DirectML; richiede pipeline multi-file",
            "https://huggingface.co/softwareweaver/Sdxl-Turbo-Olive-Onnx",
            "CC-BY-NC-4.0 / verificare termini upstream",
            0,
            RequiredSdxlSnapshotFiles,
            string.Empty,
            IsBuiltIn: true),
        new(
            "lcm-sdxl-olive-onnx",
            "LCM SDXL Olive ONNX",
            "ONNX ottimizzato Olive per generazione a pochi step; richiede pipeline multi-file",
            "https://huggingface.co/softwareweaver/Latent-Consistency-xl-Olive-Onnx",
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
