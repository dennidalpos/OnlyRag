using OnlyRag.Core;
using OnlyRag.Infrastructure.Export;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class ChatReportExportServiceTests : IDisposable
{
    private readonly string _testTempDir;

    public ChatReportExportServiceTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"onlyrag_export_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTempDir);
    }

    [Fact]
    public async Task ExportChatReportAsync_DocxFormat_CreatesValidFile()
    {
        var storagePaths = AppStoragePaths.FromRoot(_testTempDir);
        var service = new ChatReportExportService(storagePaths);

        var request = new ExportChatReportRequest(
            Title: "Test Chat Export",
            Format: ExportReportFormat.Docx,
            Messages: [
                new ExportMessageItem("user", "Quali sono i requisiti di sistema?"),
                new ExportMessageItem("assistant", "L'applicazione richiede Windows 11 e 8GB RAM.", [
                    new ExportCitationItem("doc1.pdf", 1, 2, 101, "Requisiti minimi: Windows 11")
                ])
            ]);

        ExportReportResult result = await service.ExportChatReportAsync(request);

        Assert.NotNull(result);
        Assert.True(File.Exists(result.FilePath));
        Assert.True(result.FileSizeBytes > 0);
        Assert.EndsWith(".docx", result.FileName, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testTempDir))
        {
            try { Directory.Delete(_testTempDir, recursive: true); } catch { }
        }
    }
}
