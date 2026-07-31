using System.Security.Cryptography;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed record RerankerModelInfo(
    string Id,
    string Name,
    string Description,
    string ModelFileName,
    string DownloadUrl,
    long FileSizeBytes,
    string Sha256Hash,
    bool IsDownloaded,
    double DownloadProgress,
    bool IsDownloading,
    string? DownloadError);

public sealed class RerankerModelManager
{
    public const string DefaultModelId = "bge-reranker-base";
    public const string DefaultModelFileName = "bge-reranker-base.onnx";
    public const string DefaultDownloadUrl = "https://huggingface.co/BAAI/bge-reranker-base/resolve/main/onnx/model.onnx";

    private readonly AppStoragePaths storagePaths;
    private readonly HttpClient httpClient;
    private readonly object lockObj = new();
    private CancellationTokenSource? currentDownloadCts;

    private double currentProgress;
    private bool isDownloading;
    private string? lastDownloadError;

    public RerankerModelManager(
        AppStoragePaths storagePaths,
        HttpClient? httpClient = null)
    {
        this.storagePaths = storagePaths;
        this.httpClient = httpClient ?? new HttpClient();
    }

    public string GetModelDirectory() => storagePaths.RerankerModelsDirectory;

    public string GetDefaultModelPath() => Path.Combine(GetModelDirectory(), DefaultModelFileName);

    public string GetVocabPath() => Path.Combine(GetModelDirectory(), "vocab.txt");

    public Task<RerankerModelInfo> GetModelStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string modelPath = GetDefaultModelPath();
        bool downloaded = File.Exists(modelPath);
        long fileSize = downloaded ? new FileInfo(modelPath).Length : 560_000_000L;

        lock (lockObj)
        {
            return Task.FromResult(new RerankerModelInfo(
                DefaultModelId,
                "BGE Re-Ranker Base (ONNX)",
                "Neural 2nd-stage Cross-Encoder re-ranker model for high-precision vector-keyword result scoring.",
                DefaultModelFileName,
                DefaultDownloadUrl,
                fileSize,
                "",
                downloaded,
                isDownloading ? currentProgress : (downloaded ? 1.0d : 0.0d),
                isDownloading,
                lastDownloadError));
        }
    }

    public async Task<bool> DownloadModelAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string modelDirectory = GetModelDirectory();
        Directory.CreateDirectory(modelDirectory);

        string targetPath = GetDefaultModelPath();
        string tempPath = targetPath + ".tmp";

        lock (lockObj)
        {
            if (isDownloading)
            {
                throw new InvalidOperationException("Download ONNX re-ranker già in corso.");
            }
            isDownloading = true;
            currentProgress = 0.0d;
            lastDownloadError = null;
            currentDownloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        CancellationToken token = currentDownloadCts.Token;

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                DefaultDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                token);

            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            using (Stream source = await response.Content.ReadAsStreamAsync(token))
            using (FileStream target = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await source.ReadAsync(buffer, token)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                    totalRead += bytesRead;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double p = (double)totalRead / totalBytes.Value;
                        lock (lockObj)
                        {
                            currentProgress = Math.Clamp(p, 0.0d, 1.0d);
                        }
                        progress?.Report(currentProgress);
                    }
                }
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            lock (lockObj)
            {
                currentProgress = 1.0d;
                isDownloading = false;
            }
            progress?.Report(1.0d);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            lock (lockObj)
            {
                lastDownloadError = ex.Message;
                isDownloading = false;
            }
            throw;
        }
        finally
        {
            lock (lockObj)
            {
                isDownloading = false;
                currentDownloadCts?.Dispose();
                currentDownloadCts = null;
            }
        }
    }

    public Task CancelDownloadAsync()
    {
        lock (lockObj)
        {
            currentDownloadCts?.Cancel();
        }
        return Task.CompletedTask;
    }

    public Task<bool> DeleteModelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = GetDefaultModelPath();
        if (File.Exists(path))
        {
            File.Delete(path);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
