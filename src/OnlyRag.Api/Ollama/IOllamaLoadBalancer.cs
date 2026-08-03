namespace OnlyRag.Api.Ollama;

public interface IOllamaLoadBalancer
{
    Uri SelectNodeEndpoint(string primaryEndpoint);

    void RecordNodeSuccess(Uri endpoint);

    void RecordNodeFailure(Uri endpoint);
}
