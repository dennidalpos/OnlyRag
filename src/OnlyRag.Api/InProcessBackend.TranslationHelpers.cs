using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static async Task<TranslationDetailResponse?> BuildTranslationDetailAsync(
        long translationId,
        ITranslationRepository translations,
        CancellationToken cancellationToken)
    {
        StoredTranslation? translation = await translations.GetAsync(translationId, cancellationToken);
        if (translation is null)
        {
            return null;
        }

        IReadOnlyList<StoredTranslationUnit> units = await translations.ListUnitsPreviewAsync(
            translationId,
            take: 80,
            cancellationToken);
        return new TranslationDetailResponse(
            translation.ToResponse(),
            units.Select(unit => unit.ToResponse()).ToArray());
    }

    private static async Task<TranslationCompareResponse?> BuildTranslationCompareAsync(
        long translationId,
        int? requestedPage,
        ITranslationRepository translations,
        CancellationToken cancellationToken)
    {
        StoredTranslation? translation = await translations.GetAsync(translationId, cancellationToken);
        if (translation is null)
        {
            return null;
        }

        int[] pages = (await translations.ListUnitPagesAsync(
            translationId,
            cancellationToken)).ToArray();
        if (pages.Length == 0)
        {
            return new TranslationCompareResponse(
                translation.ToResponse(),
                CurrentPage: 1,
                PagePosition: 0,
                PageCount: 0,
                PreviousPage: null,
                NextPage: null,
                Units: []);
        }

        int currentPage = requestedPage.HasValue && pages.Contains(requestedPage.Value)
            ? requestedPage.Value
            : pages[0];
        int pageIndex = Array.IndexOf(pages, currentPage);
        IReadOnlyList<StoredTranslationUnit> pageUnits = await translations.ListUnitsByPageAsync(
            translationId,
            currentPage,
            cancellationToken);
        TranslationUnitResponse[] unitResponses = pageUnits
            .Select(unit => unit.ToResponse())
            .ToArray();

        return new TranslationCompareResponse(
            translation.ToResponse(),
            currentPage,
            pageIndex + 1,
            pages.Length,
            pageIndex > 0 ? pages[pageIndex - 1] : null,
            pageIndex < pages.Length - 1 ? pages[pageIndex + 1] : null,
            unitResponses);
    }
}
