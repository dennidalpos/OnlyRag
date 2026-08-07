using Microsoft.EntityFrameworkCore;

namespace OnlyRag.Infrastructure.Storage.EF;

public sealed class DocumentEntity
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Sha256Hash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChunkEntity> Chunks { get; set; } = new List<ChunkEntity>();
}

public sealed class ChunkEntity
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string? ParentChunkId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public int StartPage { get; set; }
    public int EndPage { get; set; }
    public DateTime CreatedAt { get; set; }

    public DocumentEntity? Document { get; set; }
}

public sealed class JobEntity
{
    public string Id { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public string? StepMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class SettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public sealed class TranslationEntity
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? TranslatedContent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ChatMessageEntity
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? CitationsJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AgentRunEntity
{
    public string Id { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CurrentPhase { get; set; } = string.Empty;
    public string StateJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ArchiveManifestEntity
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int EntryIndex { get; set; }
    public string EntryPath { get; set; } = string.Empty;
    public long DeclaredSize { get; set; }
    public long ActualSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class GeneratedImageEntity
{
    public string Id { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class PolicyAuditEntity
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public string? DetailsJson { get; set; }
    public DateTime Timestamp { get; set; }
}

public sealed class OcrCacheEntity
{
    public string Id { get; set; } = string.Empty;
    public string ImageSha256 { get; set; } = string.Empty;
    public string OcrResultJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class AgentSkillEntity
{
    public string Id { get; set; } = string.Empty;
    public string SkillName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class OnlyRagDbContext : DbContext
{
    public OnlyRagDbContext(DbContextOptions<OnlyRagDbContext> options)
        : base(options)
    {
    }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<ChunkEntity> Chunks => Set<ChunkEntity>();
    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<SettingEntity> Settings => Set<SettingEntity>();
    public DbSet<TranslationEntity> Translations => Set<TranslationEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<AgentRunEntity> AgentRuns => Set<AgentRunEntity>();
    public DbSet<ArchiveManifestEntity> ArchiveManifestEntries => Set<ArchiveManifestEntity>();
    public DbSet<GeneratedImageEntity> GeneratedImages => Set<GeneratedImageEntity>();
    public DbSet<PolicyAuditEntity> PolicyAudits => Set<PolicyAuditEntity>();
    public DbSet<OcrCacheEntity> OcrCaches => Set<OcrCacheEntity>();
    public DbSet<AgentSkillEntity> AgentSkills => Set<AgentSkillEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
            entity.Property(e => e.FilePath).HasColumnName("file_path").IsRequired();
            entity.Property(e => e.FileSize).HasColumnName("file_size");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.Sha256Hash).HasColumnName("sha256_hash");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.PageCount).HasColumnName("page_count");
            entity.Property(e => e.ChunkCount).HasColumnName("chunk_count");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ChunkEntity>(entity =>
        {
            entity.ToTable("chunks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(e => e.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(e => e.ParentChunkId).HasColumnName("parent_chunk_id");
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.TokenCount).HasColumnName("token_count");
            entity.Property(e => e.StartPage).HasColumnName("start_page");
            entity.Property(e => e.EndPage).HasColumnName("end_page");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(e => e.Document)
                  .WithMany(d => d.Chunks)
                  .HasForeignKey(e => e.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobEntity>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.JobType).HasColumnName("job_type").IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.ProgressPercent).HasColumnName("progress_percent");
            entity.Property(e => e.StepMessage).HasColumnName("step_message");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<SettingEntity>(entity =>
        {
            entity.ToTable("settings");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key");
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<TranslationEntity>(entity =>
        {
            entity.ToTable("translations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.SourceLanguage).HasColumnName("source_language");
            entity.Property(e => e.TargetLanguage).HasColumnName("target_language");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.TranslatedContent).HasColumnName("translated_content");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ChatMessageEntity>(entity =>
        {
            entity.ToTable("chat_messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CitationsJson).HasColumnName("citations_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<AgentRunEntity>(entity =>
        {
            entity.ToTable("agent_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Goal).HasColumnName("goal");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.CurrentPhase).HasColumnName("current_phase");
            entity.Property(e => e.StateJson).HasColumnName("state_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ArchiveManifestEntity>(entity =>
        {
            entity.ToTable("archive_manifest_entries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.EntryIndex).HasColumnName("entry_index");
            entity.Property(e => e.EntryPath).HasColumnName("entry_path");
            entity.Property(e => e.DeclaredSize).HasColumnName("declared_size");
            entity.Property(e => e.ActualSize).HasColumnName("actual_size");
            entity.Property(e => e.Sha256).HasColumnName("sha256");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.Property(e => e.PageCount).HasColumnName("page_count");
            entity.Property(e => e.ChunkCount).HasColumnName("chunk_count");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<GeneratedImageEntity>(entity =>
        {
            entity.ToTable("generated_images");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Prompt).HasColumnName("prompt");
            entity.Property(e => e.ImagePath).HasColumnName("image_path");
            entity.Property(e => e.ModelName).HasColumnName("model_name");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<PolicyAuditEntity>(entity =>
        {
            entity.ToTable("policy_audits");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.Resource).HasColumnName("resource");
            entity.Property(e => e.Allowed).HasColumnName("allowed");
            entity.Property(e => e.DetailsJson).HasColumnName("details_json");
            entity.Property(e => e.Timestamp).HasColumnName("timestamp");
        });

        modelBuilder.Entity<OcrCacheEntity>(entity =>
        {
            entity.ToTable("ocr_cache");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ImageSha256).HasColumnName("image_sha256");
            entity.Property(e => e.OcrResultJson).HasColumnName("ocr_result_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<AgentSkillEntity>(entity =>
        {
            entity.ToTable("agent_skills");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SkillName).HasColumnName("skill_name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Trigger).HasColumnName("trigger");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });
    }
}
