using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class DocumentTranslationJobHandlerTests
{
    [Fact]
    public void BuildMessages_IncludesTranslationConstraints()
    {
        StoredTranslationUnit unit = new(
            10,
            1,
            0,
            "paragraph",
            5,
            2,
            "Totale {amount}: 123,45 EUR",
            "hash",
            "{}",
            null,
            null,
            "Pending",
            false,
            null,
            null,
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        IReadOnlyList<OllamaChatMessage> messages = DocumentTranslationPromptBuilder.BuildMessages("English", unit);

        Assert.Equal(2, messages.Count);
        Assert.Contains("Translate only the text inside", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("Preserve numbers, dates, codes, placeholders", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("Totale {amount}: 123,45 EUR", messages[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_FailsWhenPlaceholderChanges()
    {
        TranslationValidationResult result = TranslationOutputValidator.Validate(
            "Ordine {orderId} del 2026-04-25: 123,45 EUR",
            "Order {order} on 25/04/2026: EUR");

        Assert.False(result.IsValid);
        Assert.Contains("{orderId}", result.Warnings, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_SucceedsWhenNumbersChangeButPlaceholdersPreserved()
    {
        TranslationValidationResult result = TranslationOutputValidator.Validate(
            "Total: 184783 items on 2024-01-15",
            "Totale: 184.783 articoli il 15/01/2024");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ExecuteAsync_TranslatesUnitsAndCheckpointsAfterEachUnit()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        IReadOnlyList<TranslationSourceUnit> units = await translationRepository.BuildSourceUnitsAsync(document.Id);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            units,
            CancellationToken.None);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentTranslationJobHandler.DocumentTranslationJobType,
            JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                translation.Id,
                document.Id,
                "English",
                "llama-test")),
            MaxRetries: 2));
        await translationRepository.UpdateTranslationJobAsync(translation.Id, created.Id, "Queued", null);
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        EchoTranslationClient ollamaClient = new();
        StubPerformanceSettingsService performanceSettings = new(new PerformanceSettings(
            1,
            1,
            1,
            2,
            8,
            60,
            false));
        DocumentTranslationJobHandler handler = new(
            translationRepository,
            ollamaClient,
            performanceSettings,
            new StubOllamaSettingsService());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        StoredTranslation completed = (await translationRepository.GetAsync(translation.Id))!;
        IReadOnlyList<StoredTranslationUnit> completedUnits = await translationRepository.ListUnitsAsync(translation.Id);

        Assert.Equal(JobStatus.Running, stored.Status);
        Assert.Equal(100, stored.ProgressPercent);
        Assert.Contains("\"Mode\":\"completed\"", stored.CheckpointJson, StringComparison.Ordinal);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal(2, completed.CompletedUnitCount);
        Assert.Equal(2, ollamaClient.CallCount);
        Assert.Equal(1, performanceSettings.GetCallCount);
        Assert.All(completedUnits, unit => Assert.Equal("Completed", unit.Status));
        Assert.All(completedUnits, unit => Assert.NotNull(unit.TranslatedText));
        Assert.All(completedUnits, unit => Assert.Equal(unit.MachineTranslatedText, unit.TranslatedText));
        Assert.All(completedUnits, unit => Assert.False(unit.ManuallyEdited));
    }

    [Fact]
    public async Task ExecuteAsync_RepairsMissingPlaceholderBeforeSavingUnit()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            await translationRepository.BuildSourceUnitsAsync(document.Id),
            CancellationToken.None);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentTranslationJobHandler.DocumentTranslationJobType,
            JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                translation.Id,
                document.Id,
                "English",
                "llama-test"))));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        RepairingTranslationClient ollamaClient = new();
        DocumentTranslationJobHandler handler = new(
            translationRepository,
            ollamaClient,
            new StubPerformanceSettingsService(new PerformanceSettings(1, 1, 1, 1, 8, 60, false)),
            new StubOllamaSettingsService());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        IReadOnlyList<StoredTranslationUnit> translatedUnits = await translationRepository.ListUnitsAsync(translation.Id);

        Assert.Equal(3, ollamaClient.CallCount);
        Assert.True(ollamaClient.SawRepairPrompt);
        Assert.All(translatedUnits, unit => Assert.Equal("Completed", unit.Status));
        Assert.Contains("{name}", translatedUnits[0].TranslatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RepairsFolderNamePlaceholderBeforeFailingUnit()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(
            documentRepository,
            tempStorage.Root,
            "Important\nDo not include special characters in the filename for {Folder1name}.");
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "Italian",
            "llama-test",
            jobId: null,
            await translationRepository.BuildSourceUnitsAsync(document.Id),
            CancellationToken.None);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentTranslationJobHandler.DocumentTranslationJobType,
            JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                translation.Id,
                document.Id,
                "Italian",
                "llama-test"))));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        FolderPlaceholderRepairTranslationClient ollamaClient = new();
        DocumentTranslationJobHandler handler = new(
            translationRepository,
            ollamaClient,
            new StubPerformanceSettingsService(new PerformanceSettings(1, 1, 1, 1, 8, 60, false)),
            new StubOllamaSettingsService());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        StoredTranslation completed = (await translationRepository.GetAsync(translation.Id))!;
        StoredTranslationUnit translatedUnit = (await translationRepository.ListUnitsAsync(translation.Id)).Single();

        Assert.Equal(JobStatus.Running, stored.Status);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal("Completed", translatedUnit.Status);
        Assert.True(ollamaClient.SawRepairPrompt);
        Assert.Contains("{Folder1name}", translatedUnit.TranslatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesTimeoutBeforeSavingUnit()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            await translationRepository.BuildSourceUnitsAsync(document.Id),
            CancellationToken.None);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentTranslationJobHandler.DocumentTranslationJobType,
            JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                translation.Id,
                document.Id,
                "English",
                "llama-test"))));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        TimeoutThenSuccessTranslationClient ollamaClient = new();
        DocumentTranslationJobHandler handler = new(
            translationRepository,
            ollamaClient,
            new StubPerformanceSettingsService(new PerformanceSettings(1, 1, 1, 1, 8, 60, false)),
            new StubOllamaSettingsService());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        StoredTranslation completed = (await translationRepository.GetAsync(translation.Id))!;

        Assert.Equal(JobStatus.Running, stored.Status);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal(3, ollamaClient.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_FailsValidationWithoutGlobalRetryAfterLocalExhaustion()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            await translationRepository.BuildSourceUnitsAsync(document.Id),
            CancellationToken.None);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentTranslationJobHandler.DocumentTranslationJobType,
            JsonSerializer.Serialize(new DocumentTranslationJobPayload(
                translation.Id,
                document.Id,
                "English",
                "llama-test"))));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        AlwaysInvalidPlaceholderTranslationClient ollamaClient = new();
        DocumentTranslationJobHandler handler = new(
            translationRepository,
            ollamaClient,
            new StubPerformanceSettingsService(new PerformanceSettings(1, 1, 1, 1, 8, 60, false)),
            new StubOllamaSettingsService());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        StoredTranslation failed = (await translationRepository.GetAsync(translation.Id))!;
        IReadOnlyList<StoredTranslationUnit> units = await translationRepository.ListUnitsAsync(translation.Id);

        Assert.Equal(JobStatus.Failed, stored.Status);
        Assert.Null(stored.NextAttemptAtUtc);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("Failed", units[0].Status);
        Assert.Contains("testo sorgente originale", units[0].Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, ollamaClient.CallCount);
    }

    [Fact]
    public async Task Repository_UpdateUnitText_SavesManualCorrection()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteTranslationRepository translationRepository = new(tempStorage.CreateConnectionFactory());
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        IReadOnlyList<TranslationSourceUnit> units = await translationRepository.BuildSourceUnitsAsync(document.Id);
        StoredTranslation translation = await translationRepository.CreateAsync(
            document.Id,
            "English",
            "llama-test",
            jobId: null,
            units,
            CancellationToken.None);
        StoredTranslationUnit firstUnit = (await translationRepository.ListUnitsAsync(translation.Id))[0];
        await translationRepository.SaveUnitSuccessAsync(firstUnit.Id, "Machine translation 123 {name}", null);

        StoredTranslationUnit? corrected = await translationRepository.UpdateUnitTextAsync(
            translation.Id,
            firstUnit.Id,
            "Manual correction 123 {name}");

        Assert.NotNull(corrected);
        Assert.Equal("Corrected", corrected.Status);
        Assert.Equal("Manual correction 123 {name}", corrected.TranslatedText);
        Assert.Equal("Machine translation 123 {name}", corrected.MachineTranslatedText);
        Assert.True(corrected.ManuallyEdited);

        StoredTranslation refreshed = (await translationRepository.GetAsync(translation.Id))!;
        Assert.Equal(1, refreshed.CompletedUnitCount);
        Assert.NotEqual("Completed", refreshed.Status);
    }

}
