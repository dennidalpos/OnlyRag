using Microsoft.EntityFrameworkCore;

namespace OnlyRag.Infrastructure.Storage.EF;

public sealed class DocumentEntity
{
    public long Id { get; set; }
    public string DocumentUid { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
    public string? MimeType { get; set; }
    public string? FileExtension { get; set; }
    public long FileSizeBytes { get; set; }
    public string Status { get; set; } = "Imported";
    public int PageCount { get; set; }
    public string? CurrentJobId { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<DocumentPageEntity> Pages { get; set; } = new List<DocumentPageEntity>();
    public ICollection<ChunkEntity> Chunks { get; set; } = new List<ChunkEntity>();
    public ICollection<ArchiveManifestEntity> ArchiveEntries { get; set; } = new List<ArchiveManifestEntity>();
    public ICollection<TranslationEntity> Translations { get; set; } = new List<TranslationEntity>();
    public ICollection<DocumentGraphNodeEntity> GraphNodes { get; set; } = new List<DocumentGraphNodeEntity>();
}

public sealed class DocumentPageEntity
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public int PageNumber { get; set; }
    public string? RenderPath { get; set; }
    public string? OcrCachePath { get; set; }
    public string? TextContent { get; set; }
    public string? OcrStatus { get; set; }
    public string? OcrEngine { get; set; }
    public string? OcrLanguage { get; set; }
    public double? OcrConfidence { get; set; }
    public string? OcrBoxesJson { get; set; }
    public string? OcrError { get; set; }
    public DateTime? OcrCompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public DocumentEntity? Document { get; set; }
    public ICollection<ChunkEntity> Chunks { get; set; } = new List<ChunkEntity>();
    public ICollection<TranslationUnitEntity> TranslationUnits { get; set; } = new List<TranslationUnitEntity>();
}

public sealed class ArchiveManifestEntity
{
    public long Id { get; set; }
    public long ContainerDocumentId { get; set; }
    public int EntryIndex { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public long DeclaredSizeBytes { get; set; }
    public long UncompressedSizeBytes { get; set; }
    public string? ContentSha256 { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Error { get; set; }
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public DocumentEntity? ContainerDocument { get; set; }
}

public sealed class ChunkEntity
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public long? DocumentPageId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int? TokenCount { get; set; }
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public long? ParentChunkId { get; set; }
    public string ChunkLevel { get; set; } = "Parent";
    public string? SectionHeading { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public DocumentEntity? Document { get; set; }
    public DocumentPageEntity? DocumentPage { get; set; }
    public ChunkEntity? ParentChunk { get; set; }
    public ICollection<ChunkEntity> ChildChunks { get; set; } = new List<ChunkEntity>();
    public ICollection<ChunkVectorIndexStatusEntity> VectorIndexStatuses { get; set; } = new List<ChunkVectorIndexStatusEntity>();
}

public sealed class ChunkVectorIndexStatusEntity
{
    public long Id { get; set; }
    public long ChunkId { get; set; }
    public string Model { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string QdrantCollection { get; set; } = string.Empty;
    public string QdrantPointId { get; set; } = string.Empty;
    public DateTime? IndexedAtUtc { get; set; }
    public string Status { get; set; } = "Pending";
    public string? LastError { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ChunkEntity? Chunk { get; set; }
}

public sealed class JobEntity
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int ProgressPercent { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string CheckpointJson { get; set; } = "{}";
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ChatConversationEntity
{
    public string ConversationId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ChatMessageEntity> Messages { get; set; } = new List<ChatMessageEntity>();
}

public sealed class ChatMessageEntity
{
    public long Id { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ChatConversationEntity? Conversation { get; set; }
}

public sealed class TranslationEntity
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? JobId { get; set; }
    public int UnitCount { get; set; }
    public int CompletedUnitCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public DocumentEntity? Document { get; set; }
    public ICollection<TranslationUnitEntity> TranslationUnits { get; set; } = new List<TranslationUnitEntity>();
}

public sealed class TranslationUnitEntity
{
    public long Id { get; set; }
    public long TranslationId { get; set; }
    public long? DocumentPageId { get; set; }
    public int UnitIndex { get; set; }
    public string UnitKind { get; set; } = "paragraph";
    public int? PageNumber { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string LayoutMetadataJson { get; set; } = "{}";
    public string? MachineTranslatedText { get; set; }
    public string? TranslatedText { get; set; }
    public int ManuallyEdited { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ValidationWarnings { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public TranslationEntity? Translation { get; set; }
    public DocumentPageEntity? DocumentPage { get; set; }
}

public sealed class SettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = "string";
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class GeneratedImageEntity
{
    public long Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? NegativePrompt { get; set; }
    public string? Model { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Steps { get; set; }
    public int BatchSize { get; set; }
    public long? Seed { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class OcrCacheEntity
{
    public string CacheKey { get; set; } = string.Empty;
    public string PageHash { get; set; } = string.Empty;
    public string EngineName { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string PreprocessVersion { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
    public string? BoxesJson { get; set; }
    public double? Confidence { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class DocumentGraphNodeEntity
{
    public long Id { get; set; }
    public string NodeUid { get; set; } = string.Empty;
    public long? DocumentId { get; set; }
    public long? ChunkId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Concept";
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public DocumentEntity? Document { get; set; }
}

public sealed class DocumentGraphEdgeEntity
{
    public long Id { get; set; }
    public string EdgeUid { get; set; } = string.Empty;
    public long SourceNodeId { get; set; }
    public long TargetNodeId { get; set; }
    public string RelationType { get; set; } = "relates_to";
    public double Weight { get; set; } = 1.0;
    public long? ChunkId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public DocumentGraphNodeEntity? SourceNode { get; set; }
    public DocumentGraphNodeEntity? TargetNode { get; set; }
}

public sealed class AgentEpisodicMemoryEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string KeyFactsJson { get; set; } = "[]";
    public string? QdrantPointId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AgentSkillEntity
{
    public long Id { get; set; }
    public string SkillId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string PatternDescription { get; set; } = string.Empty;
    public string SolutionTemplate { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class SubagentReportCacheEntity
{
    public string CacheKey { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string PromptHash { get; set; } = string.Empty;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string ReportMarkdown { get; set; } = string.Empty;
    public string KeyFactsJson { get; set; } = "[]";
    public string ModifiedFilesJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AgentRunEntity
{
    public string RunId { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string BudgetJson { get; set; } = string.Empty;
    public int ToolCallsUsed { get; set; }
    public int EstimatedTokensUsed { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? LastError { get; set; }
    public string? FinalResponse { get; set; }
    public string MessagesJson { get; set; } = "[]";
    public string CompletionCriteriaJson { get; set; } = "[]";
    public string CompletionVerificationsJson { get; set; } = "[]";

    public ICollection<AgentRunTransitionEntity> Transitions { get; set; } = new List<AgentRunTransitionEntity>();
    public ICollection<AgentRunTraceEventEntity> TraceEvents { get; set; } = new List<AgentRunTraceEventEntity>();
    public ICollection<AgentMctsCheckpointEntity> MctsCheckpoints { get; set; } = new List<AgentMctsCheckpointEntity>();
}

public sealed class AgentRunTransitionEntity
{
    public long Id { get; set; }
    public string RunId { get; set; } = string.Empty;
    public string FromPhase { get; set; } = string.Empty;
    public string ToPhase { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }

    public AgentRunEntity? Run { get; set; }
}

public sealed class AgentRunTraceEventEntity
{
    public long Id { get; set; }
    public string RunId { get; set; } = string.Empty;
    public int Step { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string? Decision { get; set; }
    public string? ToolName { get; set; }
    public string? ToolCallId { get; set; }
    public int? Success { get; set; }
    public string? Observation { get; set; }
    public string? Error { get; set; }
    public int? EstimatedTokens { get; set; }
    public int? ToolCallsUsed { get; set; }
    public double? LatencyMs { get; set; }
    public string? Evidence { get; set; }
    public string? Outcome { get; set; }

    public AgentRunEntity? Run { get; set; }
}

public sealed class AgentPolicyAuditLogEntity
{
    public long Id { get; set; }
    public string CallId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public int Allowed { get; set; }
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = string.Empty;
    public string? OutputOrError { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public sealed class AgentMctsCheckpointEntity
{
    public long Id { get; set; }
    public string RunId { get; set; } = string.Empty;
    public int StepNumber { get; set; }
    public string ActiveNodeId { get; set; } = string.Empty;
    public string TreeStateJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public AgentRunEntity? Run { get; set; }
}

public sealed class OnlyRagDbContext : DbContext
{
    public OnlyRagDbContext(DbContextOptions<OnlyRagDbContext> options)
        : base(options)
    {
    }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<DocumentPageEntity> DocumentPages => Set<DocumentPageEntity>();
    public DbSet<ArchiveManifestEntity> ArchiveManifestEntries => Set<ArchiveManifestEntity>();
    public DbSet<ChunkEntity> Chunks => Set<ChunkEntity>();
    public DbSet<ChunkVectorIndexStatusEntity> ChunkVectorIndexStatuses => Set<ChunkVectorIndexStatusEntity>();
    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<ChatConversationEntity> ChatConversations => Set<ChatConversationEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<TranslationEntity> Translations => Set<TranslationEntity>();
    public DbSet<TranslationUnitEntity> TranslationUnits => Set<TranslationUnitEntity>();
    public DbSet<SettingEntity> Settings => Set<SettingEntity>();
    public DbSet<GeneratedImageEntity> GeneratedImages => Set<GeneratedImageEntity>();
    public DbSet<OcrCacheEntity> OcrCaches => Set<OcrCacheEntity>();
    public DbSet<DocumentGraphNodeEntity> DocumentGraphNodes => Set<DocumentGraphNodeEntity>();
    public DbSet<DocumentGraphEdgeEntity> DocumentGraphEdges => Set<DocumentGraphEdgeEntity>();
    public DbSet<AgentEpisodicMemoryEntity> AgentEpisodicMemories => Set<AgentEpisodicMemoryEntity>();
    public DbSet<AgentSkillEntity> AgentSkills => Set<AgentSkillEntity>();
    public DbSet<SubagentReportCacheEntity> SubagentReportCaches => Set<SubagentReportCacheEntity>();
    public DbSet<AgentRunEntity> AgentRuns => Set<AgentRunEntity>();
    public DbSet<AgentRunTransitionEntity> AgentRunTransitions => Set<AgentRunTransitionEntity>();
    public DbSet<AgentRunTraceEventEntity> AgentRunTraceEvents => Set<AgentRunTraceEventEntity>();
    public DbSet<AgentPolicyAuditLogEntity> AgentPolicyAuditLogs => Set<AgentPolicyAuditLogEntity>();
    public DbSet<AgentMctsCheckpointEntity> AgentMctsCheckpoints => Set<AgentMctsCheckpointEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentUid).HasColumnName("document_uid").IsRequired();
            entity.Property(e => e.OriginalFileName).HasColumnName("original_file_name").IsRequired();
            entity.Property(e => e.OriginalPath).HasColumnName("original_path").IsRequired();
            entity.Property(e => e.Sha256).HasColumnName("sha256");
            entity.Property(e => e.MimeType).HasColumnName("mime_type");
            entity.Property(e => e.FileExtension).HasColumnName("file_extension");
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.PageCount).HasColumnName("page_count");
            entity.Property(e => e.CurrentJobId).HasColumnName("current_job_id");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<DocumentPageEntity>(entity =>
        {
            entity.ToTable("document_pages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.PageNumber).HasColumnName("page_number");
            entity.Property(e => e.RenderPath).HasColumnName("render_path");
            entity.Property(e => e.OcrCachePath).HasColumnName("ocr_cache_path");
            entity.Property(e => e.TextContent).HasColumnName("text_content");
            entity.Property(e => e.OcrStatus).HasColumnName("ocr_status");
            entity.Property(e => e.OcrEngine).HasColumnName("ocr_engine");
            entity.Property(e => e.OcrLanguage).HasColumnName("ocr_language");
            entity.Property(e => e.OcrConfidence).HasColumnName("ocr_confidence");
            entity.Property(e => e.OcrBoxesJson).HasColumnName("ocr_boxes_json");
            entity.Property(e => e.OcrError).HasColumnName("ocr_error");
            entity.Property(e => e.OcrCompletedAtUtc).HasColumnName("ocr_completed_at_utc");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");

            entity.HasOne(e => e.Document)
                  .WithMany(d => d.Pages)
                  .HasForeignKey(e => e.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArchiveManifestEntity>(entity =>
        {
            entity.ToTable("archive_manifest_entries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ContainerDocumentId).HasColumnName("container_document_id");
            entity.Property(e => e.EntryIndex).HasColumnName("entry_index");
            entity.Property(e => e.RelativePath).HasColumnName("relative_path").IsRequired();
            entity.Property(e => e.DeclaredSizeBytes).HasColumnName("declared_size_bytes");
            entity.Property(e => e.UncompressedSizeBytes).HasColumnName("uncompressed_size_bytes");
            entity.Property(e => e.ContentSha256).HasColumnName("content_sha256");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.PageCount).HasColumnName("page_count");
            entity.Property(e => e.ChunkCount).HasColumnName("chunk_count");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");

            entity.HasOne(e => e.ContainerDocument)
                  .WithMany(d => d.ArchiveEntries)
                  .HasForeignKey(e => e.ContainerDocumentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChunkEntity>(entity =>
        {
            entity.ToTable("chunks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.DocumentPageId).HasColumnName("document_page_id");
            entity.Property(e => e.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.TokenCount).HasColumnName("token_count");
            entity.Property(e => e.PageStart).HasColumnName("page_start");
            entity.Property(e => e.PageEnd).HasColumnName("page_end");
            entity.Property(e => e.ContentHash).HasColumnName("content_hash");
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json");
            entity.Property(e => e.ParentChunkId).HasColumnName("parent_chunk_id");
            entity.Property(e => e.ChunkLevel).HasColumnName("chunk_level");
            entity.Property(e => e.SectionHeading).HasColumnName("section_heading");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");

            entity.HasOne(e => e.Document)
                  .WithMany(d => d.Chunks)
                  .HasForeignKey(e => e.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DocumentPage)
                  .WithMany(p => p.Chunks)
                  .HasForeignKey(e => e.DocumentPageId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ParentChunk)
                  .WithMany(c => c.ChildChunks)
                  .HasForeignKey(e => e.ParentChunkId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChunkVectorIndexStatusEntity>(entity =>
        {
            entity.ToTable("chunk_vector_index_status");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChunkId).HasColumnName("chunk_id");
            entity.Property(e => e.Model).HasColumnName("model").IsRequired();
            entity.Property(e => e.Dimensions).HasColumnName("dimensions");
            entity.Property(e => e.ContentHash).HasColumnName("content_hash");
            entity.Property(e => e.QdrantCollection).HasColumnName("qdrant_collection").IsRequired();
            entity.Property(e => e.QdrantPointId).HasColumnName("qdrant_point_id").IsRequired();
            entity.Property(e => e.IndexedAtUtc).HasColumnName("indexed_at_utc");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");

            entity.HasOne(e => e.Chunk)
                  .WithMany(c => c.VectorIndexStatuses)
                  .HasForeignKey(e => e.ChunkId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobEntity>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Type).HasColumnName("type").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.ProgressPercent).HasColumnName("progress_percent");
            entity.Property(e => e.CurrentStep).HasColumnName("current_step").IsRequired();
            entity.Property(e => e.PayloadJson).HasColumnName("payload_json").IsRequired();
            entity.Property(e => e.CheckpointJson).HasColumnName("checkpoint_json").IsRequired();
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(e => e.MaxRetries).HasColumnName("max_retries");
            entity.Property(e => e.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<ChatConversationEntity>(entity =>
        {
            entity.ToTable("chat_conversations");
            entity.HasKey(e => e.ConversationId);
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<ChatMessageEntity>(entity =>
        {
            entity.ToTable("chat_messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id").IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.Model).HasColumnName("model");
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasOne(e => e.Conversation)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(e => e.ConversationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TranslationEntity>(entity =>
        {
            entity.ToTable("translations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.SourceLanguage).HasColumnName("source_language").IsRequired();
            entity.Property(e => e.TargetLanguage).HasColumnName("target_language").IsRequired();
            entity.Property(e => e.Model).HasColumnName("model").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.JobId).HasColumnName("job_id");
            entity.Property(e => e.UnitCount).HasColumnName("unit_count");
            entity.Property(e => e.CompletedUnitCount).HasColumnName("completed_unit_count");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");

            entity.HasOne(e => e.Document)
                  .WithMany(d => d.Translations)
                  .HasForeignKey(e => e.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TranslationUnitEntity>(entity =>
        {
            entity.ToTable("translation_units");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TranslationId).HasColumnName("translation_id");
            entity.Property(e => e.DocumentPageId).HasColumnName("document_page_id");
            entity.Property(e => e.UnitIndex).HasColumnName("unit_index");
            entity.Property(e => e.UnitKind).HasColumnName("unit_kind").IsRequired();
            entity.Property(e => e.PageNumber).HasColumnName("page_number");
            entity.Property(e => e.SourceText).HasColumnName("source_text").IsRequired();
            entity.Property(e => e.SourceHash).HasColumnName("source_hash").IsRequired();
            entity.Property(e => e.LayoutMetadataJson).HasColumnName("layout_metadata_json").IsRequired();
            entity.Property(e => e.MachineTranslatedText).HasColumnName("machine_translated_text");
            entity.Property(e => e.TranslatedText).HasColumnName("translated_text");
            entity.Property(e => e.ManuallyEdited).HasColumnName("manually_edited");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.ValidationWarnings).HasColumnName("validation_warnings");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
            entity.Property(e => e.CompletedAtUtc).HasColumnName("completed_at_utc");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");

            entity.HasOne(e => e.Translation)
                  .WithMany(t => t.TranslationUnits)
                  .HasForeignKey(e => e.TranslationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DocumentPage)
                  .WithMany(p => p.TranslationUnits)
                  .HasForeignKey(e => e.DocumentPageId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SettingEntity>(entity =>
        {
            entity.ToTable("settings");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Value).HasColumnName("value").IsRequired();
            entity.Property(e => e.ValueType).HasColumnName("value_type").IsRequired();
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<GeneratedImageEntity>(entity =>
        {
            entity.ToTable("generated_images");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Provider).HasColumnName("provider").IsRequired();
            entity.Property(e => e.Prompt).HasColumnName("prompt").IsRequired();
            entity.Property(e => e.NegativePrompt).HasColumnName("negative_prompt");
            entity.Property(e => e.Model).HasColumnName("model");
            entity.Property(e => e.Width).HasColumnName("width");
            entity.Property(e => e.Height).HasColumnName("height");
            entity.Property(e => e.Steps).HasColumnName("steps");
            entity.Property(e => e.BatchSize).HasColumnName("batch_size");
            entity.Property(e => e.Seed).HasColumnName("seed");
            entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
            entity.Property(e => e.RelativePath).HasColumnName("relative_path").IsRequired();
            entity.Property(e => e.MimeType).HasColumnName("mime_type").IsRequired();
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<OcrCacheEntity>(entity =>
        {
            entity.ToTable("ocr_cache");
            entity.HasKey(e => e.CacheKey);
            entity.Property(e => e.CacheKey).HasColumnName("cache_key");
            entity.Property(e => e.PageHash).HasColumnName("page_hash").IsRequired();
            entity.Property(e => e.EngineName).HasColumnName("engine_name").IsRequired();
            entity.Property(e => e.EngineVersion).HasColumnName("engine_version").IsRequired();
            entity.Property(e => e.Language).HasColumnName("language").IsRequired();
            entity.Property(e => e.PreprocessVersion).HasColumnName("preprocess_version").IsRequired();
            entity.Property(e => e.TextContent).HasColumnName("text_content").IsRequired();
            entity.Property(e => e.BoxesJson).HasColumnName("boxes_json");
            entity.Property(e => e.Confidence).HasColumnName("confidence");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<DocumentGraphNodeEntity>(entity =>
        {
            entity.ToTable("document_graph_nodes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.NodeUid).HasColumnName("node_uid").IsRequired();
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.ChunkId).HasColumnName("chunk_id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Type).HasColumnName("type").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasOne(e => e.Document)
                  .WithMany(d => d.GraphNodes)
                  .HasForeignKey(e => e.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentGraphEdgeEntity>(entity =>
        {
            entity.ToTable("document_graph_edges");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EdgeUid).HasColumnName("edge_uid").IsRequired();
            entity.Property(e => e.SourceNodeId).HasColumnName("source_node_id");
            entity.Property(e => e.TargetNodeId).HasColumnName("target_node_id");
            entity.Property(e => e.RelationType).HasColumnName("relation_type").IsRequired();
            entity.Property(e => e.Weight).HasColumnName("weight");
            entity.Property(e => e.ChunkId).HasColumnName("chunk_id");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasOne(e => e.SourceNode)
                  .WithMany()
                  .HasForeignKey(e => e.SourceNodeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.TargetNode)
                  .WithMany()
                  .HasForeignKey(e => e.TargetNodeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentEpisodicMemoryEntity>(entity =>
        {
            entity.ToTable("agent_episodic_memories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.Goal).HasColumnName("goal").IsRequired();
            entity.Property(e => e.Summary).HasColumnName("summary").IsRequired();
            entity.Property(e => e.KeyFactsJson).HasColumnName("key_facts_json").IsRequired();
            entity.Property(e => e.QdrantPointId).HasColumnName("qdrant_point_id");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<AgentSkillEntity>(entity =>
        {
            entity.ToTable("agent_skills");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SkillId).HasColumnName("skill_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Category).HasColumnName("category").IsRequired();
            entity.Property(e => e.PatternDescription).HasColumnName("pattern_description").IsRequired();
            entity.Property(e => e.SolutionTemplate).HasColumnName("solution_template").IsRequired();
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<SubagentReportCacheEntity>(entity =>
        {
            entity.ToTable("subagent_report_cache");
            entity.HasKey(e => e.CacheKey);
            entity.Property(e => e.CacheKey).HasColumnName("cache_key");
            entity.Property(e => e.Role).HasColumnName("role").IsRequired();
            entity.Property(e => e.PromptHash).HasColumnName("prompt_hash").IsRequired();
            entity.Property(e => e.WorkspaceRoot).HasColumnName("workspace_root").IsRequired();
            entity.Property(e => e.ReportMarkdown).HasColumnName("report_markdown").IsRequired();
            entity.Property(e => e.KeyFactsJson).HasColumnName("key_facts_json").IsRequired();
            entity.Property(e => e.ModifiedFilesJson).HasColumnName("modified_files_json").IsRequired();
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");
        });

        modelBuilder.Entity<AgentRunEntity>(entity =>
        {
            entity.ToTable("agent_runs");
            entity.HasKey(e => e.RunId);
            entity.Property(e => e.RunId).HasColumnName("run_id");
            entity.Property(e => e.Goal).HasColumnName("goal").IsRequired();
            entity.Property(e => e.Mode).HasColumnName("mode").IsRequired();
            entity.Property(e => e.Model).HasColumnName("model");
            entity.Property(e => e.WorkspaceRoot).HasColumnName("workspace_root").IsRequired();
            entity.Property(e => e.Phase).HasColumnName("phase").IsRequired();
            entity.Property(e => e.BudgetJson).HasColumnName("budget_json").IsRequired();
            entity.Property(e => e.ToolCallsUsed).HasColumnName("tool_calls_used");
            entity.Property(e => e.EstimatedTokensUsed).HasColumnName("estimated_tokens_used");
            entity.Property(e => e.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.FinalResponse).HasColumnName("final_response");
            entity.Property(e => e.MessagesJson).HasColumnName("messages_json").IsRequired();
            entity.Property(e => e.CompletionCriteriaJson).HasColumnName("completion_criteria_json").IsRequired();
            entity.Property(e => e.CompletionVerificationsJson).HasColumnName("completion_verifications_json").IsRequired();
        });

        modelBuilder.Entity<AgentRunTransitionEntity>(entity =>
        {
            entity.ToTable("agent_run_transitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RunId).HasColumnName("run_id").IsRequired();
            entity.Property(e => e.FromPhase).HasColumnName("from_phase").IsRequired();
            entity.Property(e => e.ToPhase).HasColumnName("to_phase").IsRequired();
            entity.Property(e => e.Reason).HasColumnName("reason").IsRequired();
            entity.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc");

            entity.HasOne(e => e.Run)
                  .WithMany(r => r.Transitions)
                  .HasForeignKey(e => e.RunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentRunTraceEventEntity>(entity =>
        {
            entity.ToTable("agent_run_trace_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RunId).HasColumnName("run_id").IsRequired();
            entity.Property(e => e.Step).HasColumnName("step");
            entity.Property(e => e.EventType).HasColumnName("event_type").IsRequired();
            entity.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc");
            entity.Property(e => e.Phase).HasColumnName("phase").IsRequired();
            entity.Property(e => e.Decision).HasColumnName("decision");
            entity.Property(e => e.ToolName).HasColumnName("tool_name");
            entity.Property(e => e.ToolCallId).HasColumnName("tool_call_id");
            entity.Property(e => e.Success).HasColumnName("success");
            entity.Property(e => e.Observation).HasColumnName("observation");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.EstimatedTokens).HasColumnName("estimated_tokens");
            entity.Property(e => e.ToolCallsUsed).HasColumnName("tool_calls_used");
            entity.Property(e => e.LatencyMs).HasColumnName("latency_ms");
            entity.Property(e => e.Evidence).HasColumnName("evidence");
            entity.Property(e => e.Outcome).HasColumnName("outcome");

            entity.HasOne(e => e.Run)
                  .WithMany(r => r.TraceEvents)
                  .HasForeignKey(e => e.RunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentPolicyAuditLogEntity>(entity =>
        {
            entity.ToTable("agent_policy_audit_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CallId).HasColumnName("call_id").IsRequired();
            entity.Property(e => e.ToolName).HasColumnName("tool_name").IsRequired();
            entity.Property(e => e.RiskLevel).HasColumnName("risk_level").IsRequired();
            entity.Property(e => e.Allowed).HasColumnName("allowed");
            entity.Property(e => e.WorkspaceRoot).HasColumnName("workspace_root").IsRequired();
            entity.Property(e => e.ArgumentsJson).HasColumnName("arguments_json").IsRequired();
            entity.Property(e => e.OutputOrError).HasColumnName("output_or_error");
            entity.Property(e => e.TimestampUtc).HasColumnName("timestamp_utc");
        });

        modelBuilder.Entity<AgentMctsCheckpointEntity>(entity =>
        {
            entity.ToTable("agent_mcts_checkpoints");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RunId).HasColumnName("run_id").IsRequired();
            entity.Property(e => e.StepNumber).HasColumnName("step_number");
            entity.Property(e => e.ActiveNodeId).HasColumnName("active_node_id").IsRequired();
            entity.Property(e => e.TreeStateJson).HasColumnName("tree_state_json").IsRequired();
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasOne(e => e.Run)
                  .WithMany(r => r.MctsCheckpoints)
                  .HasForeignKey(e => e.RunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
