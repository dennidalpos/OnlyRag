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
    // BGE Re-Ranker Base is an XLM-RoBERTa model.  It uses Hugging Face's
    // tokenizer.json (SentencePiece Unigram), not a BERT vocab.txt file.
    public const string VocabFileName = "tokenizer.json";
    public const string VocabDownloadUrl = "https://huggingface.co/BAAI/bge-reranker-base/resolve/main/tokenizer.json";

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

    public string GetVocabPath() => Path.Combine(GetModelDirectory(), VocabFileName);

    public Task<RerankerModelInfo> GetModelStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string modelPath = GetDefaultModelPath();
        string vocabPath = GetVocabPath();
        bool downloaded = File.Exists(modelPath) && File.Exists(vocabPath);
        long fileSize = downloaded ? (new FileInfo(modelPath).Length + new FileInfo(vocabPath).Length) : 560_000_000L;

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

    public Task<bool> DownloadModelAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string modelDirectory = GetModelDirectory();
        Directory.CreateDirectory(modelDirectory);

        string targetModelPath = GetDefaultModelPath();
        string targetVocabPath = GetVocabPath();
        string tempModelPath = targetModelPath + ".tmp";
        string tempVocabPath = targetVocabPath + ".tmp";

        lock (lockObj)
        {
            if (isDownloading)
            {
                throw new InvalidOperationException("Download ONNX re-ranker già in corso.");
            }
            isDownloading = true;
            currentProgress = 0.0d;
            lastDownloadError = null;
            currentDownloadCts = new CancellationTokenSource();
        }

        CancellationToken token = currentDownloadCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                bool modelAlreadyAvailable = File.Exists(targetModelPath) && new FileInfo(targetModelPath).Length > 0;
                if (!modelAlreadyAvailable)
                {
                    await DownloadFileWithProgressAsync(DefaultDownloadUrl, tempModelPath, 0.0d, 0.9d, progress, token);
                }
                else
                {
                    lock (lockObj)
                    {
                        currentProgress = 0.9d;
                    }
                    progress?.Report(0.9d);
                }

                await DownloadFileWithProgressAsync(VocabDownloadUrl, tempVocabPath, 0.9d, 1.0d, progress, token);

                if (!modelAlreadyAvailable)
                {
                    if (File.Exists(targetModelPath)) File.Delete(targetModelPath);
                    File.Move(tempModelPath, targetModelPath);
                }

                if (File.Exists(targetVocabPath)) File.Delete(targetVocabPath);
                File.Move(tempVocabPath, targetVocabPath);

                lock (lockObj)
                {
                    currentProgress = 1.0d;
                    isDownloading = false;
                }
                progress?.Report(1.0d);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (File.Exists(tempModelPath)) { try { File.Delete(tempModelPath); } catch { } }
                if (File.Exists(tempVocabPath)) { try { File.Delete(tempVocabPath); } catch { } }

                lock (lockObj)
                {
                    lastDownloadError = ex.Message;
                    isDownloading = false;
                }
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
        }, CancellationToken.None);

        return Task.FromResult(true);
    }

    private async Task DownloadFileWithProgressAsync(
        string url,
        string destinationPath,
        double startProgress,
        double endProgress,
        IProgress<double>? progress,
        CancellationToken token)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            token);

        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        using (Stream source = await response.Content.ReadAsStreamAsync(token))
        using (FileStream target = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
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
                    double fileP = (double)totalRead / totalBytes.Value;
                    double overallP = startProgress + fileP * (endProgress - startProgress);
                    lock (lockObj)
                    {
                        currentProgress = Math.Clamp(overallP, 0.0d, 1.0d);
                    }
                    progress?.Report(currentProgress);
                }
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
        string vocabPath = GetVocabPath();
        bool deleted = false;
        if (File.Exists(path))
        {
            File.Delete(path);
            deleted = true;
        }
        if (File.Exists(vocabPath))
        {
            File.Delete(vocabPath);
            deleted = true;
        }
        return Task.FromResult(deleted);
    }

    public static double CalculateDynamicCutoffThreshold(double baseThreshold, double cragConfidenceScore)
    {
        double clampedBase = Math.Clamp(baseThreshold, 0.05, 0.95);
        double clampedCrag = Math.Clamp(cragConfidenceScore, 0.0, 1.0);

        if (clampedCrag >= 0.75)
        {
            return Math.Min(0.90, clampedBase + 0.10);
        }
        else if (clampedCrag >= 0.35)
        {
            return Math.Max(0.15, clampedBase - 0.10);
        }

        return clampedBase;
    }
}
