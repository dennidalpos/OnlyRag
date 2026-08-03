using System.Collections.Concurrent;

namespace OnlyRag.Api.Ollama;

public sealed class OllamaLoadBalancer : IOllamaLoadBalancer
{
    private readonly ConcurrentDictionary<string, NodeHealth> _nodes = new(StringComparer.OrdinalIgnoreCase);

    private sealed class NodeHealth
    {
        public bool IsHealthy { get; set; } = true;
        public DateTime LastFailureTime { get; set; } = DateTime.MinValue;
        public int ConsecutiveFailures { get; set; }
    }

    public Uri SelectNodeEndpoint(string primaryEndpoint)
    {
        if (!Uri.TryCreate(primaryEndpoint, UriKind.Absolute, out Uri? uri))
        {
            uri = new Uri("http://127.0.0.1:11434");
        }

        string key = uri.ToString();
        NodeHealth health = _nodes.GetOrAdd(key, _ => new NodeHealth());

        // Passive recovery after 30 seconds
        if (!health.IsHealthy && (DateTime.UtcNow - health.LastFailureTime).TotalSeconds > 30)
        {
            health.IsHealthy = true;
            health.ConsecutiveFailures = 0;
        }

        return uri;
    }

    public void RecordNodeSuccess(Uri endpoint)
    {
        string key = endpoint.ToString();
        if (_nodes.TryGetValue(key, out NodeHealth? health))
        {
            health.IsHealthy = true;
            health.ConsecutiveFailures = 0;
        }
    }

    public void RecordNodeFailure(Uri endpoint)
    {
        string key = endpoint.ToString();
        NodeHealth health = _nodes.GetOrAdd(key, _ => new NodeHealth());
        health.ConsecutiveFailures++;
        health.LastFailureTime = DateTime.UtcNow;
        if (health.ConsecutiveFailures >= 3)
        {
            health.IsHealthy = false;
        }
    }
}
