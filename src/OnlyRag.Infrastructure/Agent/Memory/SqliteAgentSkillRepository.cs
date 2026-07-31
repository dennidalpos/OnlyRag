using Microsoft.Data.Sqlite;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Agent.Memory;

public sealed class SqliteAgentSkillRepository : IAgentSkillRepository
{
    private static readonly char[] Separators = new[] { ' ', ',', '.', ';', '?', '!' };

    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteAgentSkillRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task SaveSkillAsync(AgentSkillRecord skill, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skill.SkillId) || string.IsNullOrWhiteSpace(skill.Name)) return;

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_skills (skill_id, name, category, pattern_description, solution_template, created_at_utc)
            VALUES ($skillId, $name, $category, $pattern, $solution, $createdAt)
            ON CONFLICT(skill_id) DO UPDATE SET
                name = EXCLUDED.name,
                category = EXCLUDED.category,
                pattern_description = EXCLUDED.pattern_description,
                solution_template = EXCLUDED.solution_template,
                created_at_utc = EXCLUDED.created_at_utc;
            """;
        cmd.AddParameter("$skillId", skill.SkillId);
        cmd.AddParameter("$name", skill.Name);
        cmd.AddParameter("$category", skill.Category);
        cmd.AddParameter("$pattern", skill.PatternDescription);
        cmd.AddParameter("$solution", skill.SolutionTemplate);
        cmd.AddParameter("$createdAt", skill.CreatedAtUtc.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentSkillRecord>> SearchRelevantSkillsAsync(
        string contextQuery,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contextQuery)) return await ListSkillsAsync(cancellationToken);

        var results = new List<AgentSkillRecord>();
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var words = contextQuery.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                                .Where(w => w.Length >= 3)
                                .Take(4)
                                .ToList();

        if (words.Count == 0) return await ListSkillsAsync(cancellationToken);

        foreach (string word in words)
        {
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT skill_id, name, category, pattern_description, solution_template, created_at_utc
                FROM agent_skills
                WHERE name LIKE $pattern OR category LIKE $pattern OR pattern_description LIKE $pattern
                ORDER BY id DESC
                LIMIT $limit;
                """;
            cmd.AddParameter("$pattern", $"%{word}%");
            cmd.AddParameter("$limit", topK);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var item = ParseRecord(reader);
                if (!results.Any(r => r.SkillId.Equals(item.SkillId, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(item);
                }
            }
        }

        return results.Take(topK).ToList();
    }

    public async Task<IReadOnlyList<AgentSkillRecord>> ListSkillsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<AgentSkillRecord>();
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT skill_id, name, category, pattern_description, solution_template, created_at_utc
            FROM agent_skills
            ORDER BY id DESC
            LIMIT 50;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ParseRecord(reader));
        }

        return results;
    }

    private static AgentSkillRecord ParseRecord(SqliteDataReader reader)
    {
        string skillId = reader.GetString(0);
        string name = reader.GetString(1);
        string category = reader.GetString(2);
        string pattern = reader.GetString(3);
        string solution = reader.GetString(4);
        DateTimeOffset createdAt = DateTimeOffset.TryParse(reader.GetString(5), out var dt) ? dt : DateTimeOffset.UtcNow;

        return new AgentSkillRecord(skillId, name, category, pattern, solution, createdAt);
    }
}
