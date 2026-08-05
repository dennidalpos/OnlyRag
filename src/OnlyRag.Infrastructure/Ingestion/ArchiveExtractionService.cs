using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using OnlyRag.Core;
using System.Security.Cryptography;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class ArchiveEntryContent(int entryIndex, string relativePath, long length)
{
    public int EntryIndex { get; } = entryIndex;

    public string RelativePath { get; } = relativePath;

    public long Length { get; } = length;

    public long BytesRead { get; internal set; }

    public string? ContentSha256 { get; internal set; }
}

public sealed class ArchiveExtractionException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>Validates and streams archive entries without extracting them to an uncontrolled directory.</summary>
public sealed class ArchiveExtractionService
{
    public async Task ExtractAsync(
        Stream archiveStream,
        string archiveFileName,
        ArchiveExtractionLimits? configuredLimits,
        Func<ArchiveEntryContent, Stream, CancellationToken, Task> onFile,
        CancellationToken cancellationToken = default)
    {
        await ExtractAsync(archiveStream, archiveFileName, configuredLimits, onFile, onEntryCompleted: null, cancellationToken);
    }

    public async Task ExtractAsync(
        Stream archiveStream,
        string archiveFileName,
        ArchiveExtractionLimits? configuredLimits,
        Func<ArchiveEntryContent, Stream, CancellationToken, Task> onFile,
        Func<ArchiveEntryContent, CancellationToken, Task>? onEntryCompleted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        ArgumentNullException.ThrowIfNull(onFile);
        EnsureSupportedArchiveName(archiveFileName);
        if (!archiveStream.CanRead || !archiveStream.CanSeek)
        {
            throw new ArchiveExtractionException("The archive stream must be readable and seekable.");
        }

        ArchiveExtractionLimits limits = ArchiveExtractionLimits.Normalize(configuredLimits);
        long totalBytes = 0;
        int fileCount = 0;
        try
        {
            using IArchive archive = ArchiveFactory.OpenArchive(archiveStream, new ReaderOptions { LeaveStreamOpen = true });
            foreach (IArchiveEntry entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = ValidateEntryPath(entry.Key, limits.MaxDirectoryDepth);
                if (++fileCount > limits.MaxFileCount)
                {
                    throw new ArchiveExtractionException($"The archive exceeds the configured limit of {limits.MaxFileCount} files.");
                }

                await using Stream source = entry.OpenEntryStream();
                await using LimitedReadStream limited = new(source, limits.MaxFileUncompressedBytes, cancellationToken);
                ArchiveEntryContent entryContent = new(fileCount, relativePath, entry.Size);
                await onFile(entryContent, limited, cancellationToken);
                await limited.CopyToAsync(Stream.Null, cancellationToken);
                long bytesRead = limited.BytesRead;
                entryContent.BytesRead = bytesRead;
                entryContent.ContentSha256 = limited.GetContentSha256();
                totalBytes = checked(totalBytes + bytesRead);
                if (totalBytes > limits.MaxTotalUncompressedBytes)
                {
                    throw new ArchiveExtractionException("The archive exceeds the configured total uncompressed size limit.");
                }

                if (onEntryCompleted is not null)
                {
                    await onEntryCompleted(entryContent, cancellationToken);
                }
            }
        }
        catch (ArchiveExtractionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SharpCompressException or InvalidDataException or IOException or NotSupportedException or ArgumentException)
        {
            throw new ArchiveExtractionException("The archive is invalid, corrupt, encrypted, or uses an unsupported compression method.", ex);
        }
    }

    private static void EnsureSupportedArchiveName(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".zip" and not ".tar" and not ".7z")
        {
            throw new ArchiveExtractionException("Only ZIP, TAR, and 7Z archives are supported.");
        }
    }

    private static string ValidateEntryPath(string? key, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key))
        {
            throw new ArchiveExtractionException("The archive contains an unsafe entry path.");
        }

        string normalized = key.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Length - 1 > maxDepth || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new ArchiveExtractionException("The archive contains an unsafe entry path.");
        }

        return string.Join('/', segments);
    }

    private sealed class LimitedReadStream(Stream source, long maxBytes, CancellationToken cancellationToken) : Stream
    {
        private readonly IncrementalHash contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public long BytesRead { get; private set; }
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = source.Read(buffer, offset, count);
            if (read > 0) contentHash.AppendData(buffer, offset, read);
            RecordBytesRead(read);
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, token);
            int count = await source.ReadAsync(buffer, linked.Token);
            if (count > 0) contentHash.AppendData(buffer.Span[..count]);
            RecordBytesRead(count);
            return count;
        }

        public string GetContentSha256() => Convert.ToHexString(contentHash.GetHashAndReset()).ToLowerInvariant();
        private void RecordBytesRead(int count)
        {
            BytesRead = checked(BytesRead + count);
            if (BytesRead > maxBytes)
            {
                throw new ArchiveExtractionException("An archive entry exceeds the configured uncompressed size limit.");
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) source.Dispose();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await source.DisposeAsync();
            contentHash.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
