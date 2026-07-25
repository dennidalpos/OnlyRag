using Microsoft.Data.Sqlite;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Export;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed class PdfExportSettingsStoreTests : IAsyncLifetime
{
    private TempStorage? storage;

    public async Task InitializeAsync()
    {
        storage = await TempStorage.CreateInitializedAsync();
    }

    public Task DisposeAsync()
    {
        storage?.Dispose();
        return Task.CompletedTask;
    }

    private TempStorage Storage => storage ?? throw new InvalidOperationException("Storage not initialized.");

    [Fact]
    public async Task GetAsync_ReturnsDefaultsWhenNotConfigured()
    {
        PdfExportSettingsStore store = new(Storage.Settings);

        PdfExportSettings settings = await store.GetAsync();

        Assert.Null(settings.LibreOfficePath);
        Assert.Equal(120, settings.ConversionTimeoutSeconds);
    }

    [Fact]
    public async Task UpdateAsync_PersistsAndNormalizesSettings()
    {
        PdfExportSettingsStore store = new(Storage.Settings);

        PdfExportSettings updated = await store.UpdateAsync(new PdfExportSettings(
            LibreOfficePath: "  C:\\Tools\\LibreOffice\\soffice.exe  ",
            ConversionTimeoutSeconds: 300));

        Assert.Equal("C:\\Tools\\LibreOffice\\soffice.exe", updated.LibreOfficePath);
        Assert.Equal(300, updated.ConversionTimeoutSeconds);

        PdfExportSettings loaded = await store.GetAsync();
        Assert.Equal("C:\\Tools\\LibreOffice\\soffice.exe", loaded.LibreOfficePath);
        Assert.Equal(300, loaded.ConversionTimeoutSeconds);
    }

    [Fact]
    public async Task UpdateAsync_ClampsInvalidTimeout()
    {
        PdfExportSettingsStore store = new(Storage.Settings);

        PdfExportSettings updated = await store.UpdateAsync(new PdfExportSettings(
            LibreOfficePath: null,
            ConversionTimeoutSeconds: -50));

        Assert.Equal(120, updated.ConversionTimeoutSeconds);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsMissingStatusWhenCustomPathNotFound()
    {
        PdfExportSettingsStore store = new(Storage.Settings);
        await store.UpdateAsync(new PdfExportSettings("C:\\NonExistent\\soffice.exe", 120));

        PdfExportConverterStatusResponse status = await store.GetStatusAsync();

        Assert.Equal("Missing", status.State);
        Assert.False(status.IsAvailable);
        Assert.Equal("C:\\NonExistent\\soffice.exe", status.ExecutablePath);
        Assert.Equal(120, status.ConversionTimeoutSeconds);
        Assert.Equal("LibreOffice non installato.", status.Message);
        Assert.NotNull(status.Suggestion);
    }

    [Fact]
    public void ResolveLibreOfficeExecutable_ReturnsNullWhenCustomPathMissing()
    {
        string? resolved = PdfExportSettingsStore.ResolveLibreOfficeExecutable(
            customPath: "C:\\Fake\\soffice.exe",
            fileExists: _ => false,
            directoryExists: _ => false);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveLibreOfficeExecutable_ResolvesCustomPathWhenFileExists()
    {
        string currentAssembly = typeof(PdfExportSettingsStoreTests).Assembly.Location;
        string? resolved = PdfExportSettingsStore.ResolveLibreOfficeExecutable(
            customPath: currentAssembly,
            fileExists: path => path == currentAssembly);

        Assert.Equal(Path.GetFullPath(currentAssembly), resolved);
    }

    private sealed class TempStorage : IDisposable
    {
        private TempStorage(string root)
        {
            Root = root;
            Paths = AppStoragePaths.FromRoot(root);
            Descriptor = new LocalSqliteStoreDescriptor(Paths);
            ConnectionFactory = new LocalSqliteConnectionFactory(Descriptor);
            Settings = new SqliteSettingsRepository(ConnectionFactory);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public LocalSqliteStoreDescriptor Descriptor { get; }

        public LocalSqliteConnectionFactory ConnectionFactory { get; }

        public SqliteSettingsRepository Settings { get; }

        public static async Task<TempStorage> CreateInitializedAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.PdfExport.Tests", Guid.NewGuid().ToString("N"));
            TempStorage storage = new(root);
            LocalSqliteStorageService service = new(
                storage.Descriptor,
                new LocalSqliteSchemaInitializer(storage.Descriptor, storage.ConnectionFactory));
            await service.InitializeAsync();
            return storage;
        }

        public void Dispose()
        {
            try
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
