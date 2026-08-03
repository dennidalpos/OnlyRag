using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace OnlyRag.Api.Ollama;

public sealed class OllamaQueryEmbeddingCache
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<float>> _l1Cache = new();
    private const int MaxL1Items = 5000;

    public bool TryGet(string model, string query, out IReadOnlyList<float> embedding)
    {
        string key = ComputeKey(model, query);
        return _l1Cache.TryGetValue(key, out embedding!);
    }

    public void Set(string model, string query, IReadOnlyList<float> embedding)
    {
        if (embedding == null || embedding.Count == 0) return;

        if (_l1Cache.Count >= MaxL1Items)
        {
            _l1Cache.Clear();
        }

        string key = ComputeKey(model, query);
        _l1Cache[key] = embedding;
    }

    private static string ComputeKey(string model, string query)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($"{model.Trim().ToLowerInvariant()}:{query.Trim()}");
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
