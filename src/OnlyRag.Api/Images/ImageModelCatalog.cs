using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal static class ImageModelCatalog
{
    public const string DefaultModelId = "onlyrag-sdxl-turbo-directml";
    public const string RequiredModelFileName = "model.onnx";
    public const string PlaceholderModelContent = "OnlyRag integrated image model placeholder v1\n";
    public const string EmbeddedPlaceholderDownloadUrl = "onlyrag://models/images/onlyrag-sdxl-turbo-directml/model.onnx";

    private static readonly ImageModelCatalogEntry[] Models =
    [
        new(
            DefaultModelId,
            "OnlyRag SDXL Turbo",
            "DirectML GPU consigliato, CPU disponibile per fallback",
            EmbeddedPlaceholderDownloadUrl,
            "OpenRAIL++ / verificare termini modello upstream",
            46,
            [RequiredModelFileName],
            "41300f6070c3a7152cc4b92b93c3aee5a868f95e4711973d60060a123074496b")
    ];

    public static IReadOnlyList<ImageModelCatalogEntry> List() => Models;

    public static bool Contains(string modelId)
    {
        return Models.Any(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    public static ImageModelCatalogEntry Get(string modelId)
    {
        return Models.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ImageGenerationException(
                ImageGenerationErrorKind.NotFound,
                "Modello immagini non presente nel catalogo integrato.");
    }
}
