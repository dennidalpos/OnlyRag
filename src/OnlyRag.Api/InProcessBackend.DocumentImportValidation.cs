using Microsoft.AspNetCore.Http;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void ValidateImportBatch(IFormFileCollection files, LocalDocumentStorageGuard storageGuard)
    {
        long totalBytes = 0;
        foreach (IFormFile file in files)
        {
            storageGuard.EnsureFileWithinLimits(file.FileName, file.Length);
            totalBytes = checked(totalBytes + file.Length);
        }

        storageGuard.EnsureBatchWithinLimits(files.Count, totalBytes);
        storageGuard.EnsureStorageAvailableForBytes(totalBytes);
    }

    private static IResult MapDocumentStorageLimitException(DocumentStorageLimitException exception)
    {
        int statusCode = exception.Kind == DocumentStorageLimitKind.TooManyFiles
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status413PayloadTooLarge;
        return CreateProblem(
            exception.Title,
            exception.Message,
            statusCode,
            exception.Kind == DocumentStorageLimitKind.TooManyFiles
                ? "document_too_many_files"
                : "document_storage_limit");
    }
}
