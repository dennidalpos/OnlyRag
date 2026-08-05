using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;

namespace OnlyRag.Infrastructure.Tests;

public sealed class ArchiveExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_Zip_StreamsValidEntries()
    {
        await using MemoryStream archive = CreateZip(("notes/one.txt", "first"), ("two.md", "second"));
        List<(string Path, string Text)> extracted = [];

        await new ArchiveExtractionService().ExtractAsync(
            archive,
            "documents.zip",
            ArchiveExtractionLimits.Default,
            async (entry, content, cancellationToken) =>
            {
                using StreamReader reader = new(content, leaveOpen: true);
                extracted.Add((entry.RelativePath, await reader.ReadToEndAsync(cancellationToken)));
            });

        Assert.Equal([("notes/one.txt", "first"), ("two.md", "second")], extracted);
    }

    [Fact]
    public async Task ExtractAsync_Tar_StreamsValidEntries()
    {
        await using MemoryStream archive = CreateTar(("readme.txt", "tar content"));
        List<string> extracted = [];

        await new ArchiveExtractionService().ExtractAsync(
            archive,
            "documents.tar",
            ArchiveExtractionLimits.Default,
            async (_, content, cancellationToken) =>
            {
                using StreamReader reader = new(content, leaveOpen: true);
                extracted.Add(await reader.ReadToEndAsync(cancellationToken));
            });

        Assert.Equal(["tar content"], extracted);
    }

    [Fact]
    public async Task ExtractAsync_SevenZip_StreamsValidEntries()
    {
        await using MemoryStream archive = CreateSevenZip(("notes.txt", "7z content"));
        List<string> extracted = [];

        await new ArchiveExtractionService().ExtractAsync(
            archive,
            "documents.7z",
            ArchiveExtractionLimits.Default,
            async (_, content, cancellationToken) =>
            {
                using StreamReader reader = new(content, leaveOpen: true);
                extracted.Add(await reader.ReadToEndAsync(cancellationToken));
            });

        Assert.Equal(["7z content"], extracted);
    }

    [Fact]
    public async Task ExtractAsync_CorruptArchive_ReturnsSafeDomainError()
    {
        await using MemoryStream archive = new(Encoding.UTF8.GetBytes("not an archive"));

        ArchiveExtractionException exception = await Assert.ThrowsAsync<ArchiveExtractionException>(() =>
            new ArchiveExtractionService().ExtractAsync(
                archive,
                "broken.zip",
                ArchiveExtractionLimits.Default,
                (_, _, _) => Task.CompletedTask));

        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_TotalLimit_IsEnforcedEvenWhenConsumerSkipsContent()
    {
        await using MemoryStream archive = CreateZip(("first.txt", new string('a', 12)), ("second.txt", new string('b', 12)));
        ArchiveExtractionLimits limits = ArchiveExtractionLimits.Default with
        {
            MaxFileUncompressedBytes = 16,
            MaxTotalUncompressedBytes = 16
        };

        ArchiveExtractionException exception = await Assert.ThrowsAsync<ArchiveExtractionException>(() =>
            new ArchiveExtractionService().ExtractAsync(
                archive,
                "total.zip",
                limits,
                (_, _, _) => Task.CompletedTask));

        Assert.Contains("total", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("nested/../../outside.txt")]
    [InlineData("C:\\outside.txt")]
    public async Task ExtractAsync_UnsafePath_IsRejected(string entryPath)
    {
        await using MemoryStream archive = CreateZip((entryPath, "unsafe"));

        ArchiveExtractionException exception = await Assert.ThrowsAsync<ArchiveExtractionException>(() =>
            new ArchiveExtractionService().ExtractAsync(
                archive, "unsafe.zip", ArchiveExtractionLimits.Default, (_, _, _) => Task.CompletedTask));

        Assert.Contains("unsafe entry path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_EntryOverLimit_IsRejectedWhileStreaming()
    {
        await using MemoryStream archive = CreateZip(("large.txt", new string('a', 32)));
        ArchiveExtractionLimits limits = ArchiveExtractionLimits.Default with { MaxFileUncompressedBytes = 16 };

        ArchiveExtractionException exception = await Assert.ThrowsAsync<ArchiveExtractionException>(() =>
            new ArchiveExtractionService().ExtractAsync(
                archive,
                "large.zip",
                limits,
                async (_, content, cancellationToken) =>
                {
                    await content.CopyToAsync(Stream.Null, cancellationToken);
                }));

        Assert.Contains("size limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_TooManyFiles_IsRejected()
    {
        await using MemoryStream archive = CreateZip(("one.txt", "1"), ("two.txt", "2"));
        ArchiveExtractionLimits limits = ArchiveExtractionLimits.Default with { MaxFileCount = 1 };

        await Assert.ThrowsAsync<ArchiveExtractionException>(() =>
            new ArchiveExtractionService().ExtractAsync(
                archive, "many.zip", limits, (_, _, _) => Task.CompletedTask));
    }

    private static MemoryStream CreateZip(params (string Path, string Content)[] files)
    {
        MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string content) in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using StreamWriter writer = new(entry.Open(), Encoding.UTF8, leaveOpen: false);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateTar(params (string Path, string Content)[] files)
    {
        MemoryStream stream = new();
        using (TarWriter writer = new(stream, leaveOpen: true))
        {
            foreach ((string path, string content) in files)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
                });
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateSevenZip(params (string Path, string Content)[] files)
    {
        MemoryStream stream = new();
        using (IWriter writer = WriterFactory.OpenWriter(
            stream,
            ArchiveType.SevenZip,
            new SevenZipWriterOptions(CompressionType.LZMA)
            {
                LeaveStreamOpen = true
            }))
        {
            foreach ((string path, string content) in files)
            {
                writer.Write(path, new MemoryStream(Encoding.UTF8.GetBytes(content)));
            }
        }

        stream.Position = 0;
        return stream;
    }
}
