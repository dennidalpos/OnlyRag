using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Images;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Infrastructure.Agent.Tools;

public sealed class ExternalServicesToolHandler : IToolHandler
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };
    private readonly IHybridRetrievalService? retrievalService;
    private readonly IDocumentIngestionService? ingestionService;
    private readonly ImageGenerationService? imageGenerationService;
    private readonly ILoggingService? logger;

    public ExternalServicesToolHandler(
        IHybridRetrievalService? retrievalService = null,
        IDocumentIngestionService? ingestionService = null,
        ImageGenerationService? imageGenerationService = null,
        ILoggingService? logger = null)
    {
        this.retrievalService = retrievalService;
        this.ingestionService = ingestionService;
        this.imageGenerationService = imageGenerationService;
        this.logger = logger;
    }

    public bool CanHandle(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "web_search" or "search_web" or "ingest_office_doc" or "generate_image_onnx" or
            "query_retrieval_index" or "rag_hybrid_search" or "rag_search" or "search_docs" => true,
            _ => false
        };
    }

    public async Task<AgentToolResult> ExecuteAsync(
        string callId,
        string toolName,
        JsonElement args,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep = null,
        CancellationToken cancellationToken = default)
    {
        return toolName.ToLowerInvariant() switch
        {
            "web_search" or "search_web" => await WebSearchAsync(callId, toolName, args, cancellationToken),
            "ingest_office_doc" => await IngestOfficeDocAsync(callId, toolName, args, workspaceRoot, cancellationToken),
            "generate_image_onnx" => await GenerateImageOnnxAsync(callId, toolName, args, cancellationToken),
            "query_retrieval_index" or "rag_hybrid_search" or "rag_search" or "search_docs" => await QueryRetrievalIndexAsync(callId, toolName, args, cancellationToken),
            _ => new AgentToolResult(callId, toolName, false, string.Empty, $"Tool '{toolName}' not supported by ExternalServicesToolHandler")
        };
    }

    private async Task<AgentToolResult> WebSearchAsync(string callId, string toolName, JsonElement args, CancellationToken cancellationToken)
    {
        string query = ToolHelper.GetArgString(args, "query", "search", "q", "pattern") ?? "";
        string domain = ToolHelper.GetArgString(args, "domain", "site", "source") ?? "";

        if (string.IsNullOrWhiteSpace(query))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "The 'query' parameter for web search is required.");
        }

        string searchQuery = string.IsNullOrWhiteSpace(domain) ? query : $"{query} site:{domain}";
        logger?.LogInfo("AgentEngine", $"[WEB SEARCH] Query: '{searchQuery}'");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            string searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(searchQuery)}";
            HttpResponseMessage resp = await http.GetAsync(searchUrl, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, $"Web search failed with HTTP code {(int)resp.StatusCode}");
            }

            string html = await resp.Content.ReadAsStringAsync(cancellationToken);
            var results = ParseDuckDuckGoSearchResults(html);

            if (results.Count == 0)
            {
                return new AgentToolResult(callId, toolName, true, $"No results found for web search: '{searchQuery}'. Try rephrasing the query.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[OFFICIAL WEB SEARCH RESULTS: '{searchQuery}']\n");
            int idx = 1;
            foreach (var res in results.Take(6))
            {
                sb.AppendLine($"{idx}. **{res.Title}**");
                sb.AppendLine($"   URL: {res.Url}");
                sb.AppendLine($"   Snippet: {res.Snippet}\n");
                idx++;
            }

            return new AgentToolResult(callId, toolName, true, sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            logger?.LogWarning("AgentEngine", $"Error during web search for '{searchQuery}': {ex.Message}");
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Unable to complete web search: {ex.Message}");
        }
    }

    private static List<(string Title, string Url, string Snippet)> ParseDuckDuckGoSearchResults(string html)
    {
        var results = new List<(string Title, string Url, string Snippet)>();
        if (string.IsNullOrWhiteSpace(html)) return results;

        var matches = Regex.Matches(html, @"<a class=""result__a"" href=""([^""]+)""[^>]*>(.*?)</a>[\s\S]*?<a class=""result__snippet""[^>]*>(.*?)</a>", RegexOptions.Singleline);
        foreach (Match match in matches)
        {
            string rawUrl = match.Groups[1].Value;
            string rawTitle = match.Groups[2].Value;
            string rawSnippet = match.Groups[3].Value;

            string cleanTitle = Regex.Replace(rawTitle, "<.*?>", "").Trim();
            string cleanSnippet = Regex.Replace(rawSnippet, "<.*?>", "").Trim();
            cleanTitle = System.Net.WebUtility.HtmlDecode(cleanTitle);
            cleanSnippet = System.Net.WebUtility.HtmlDecode(cleanSnippet);

            string cleanUrl = rawUrl;
            var uddgMatch = Regex.Match(rawUrl, @"uddg=([^&]+)");
            if (uddgMatch.Success)
            {
                cleanUrl = Uri.UnescapeDataString(uddgMatch.Groups[1].Value);
            }

            if (!string.IsNullOrWhiteSpace(cleanTitle) && !string.IsNullOrWhiteSpace(cleanUrl))
            {
                results.Add((cleanTitle, cleanUrl, cleanSnippet));
            }
        }

        return results;
    }

    private async Task<AgentToolResult> IngestOfficeDocAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = ToolHelper.GetArgString(args, "relativePath", "path", "file", "filepath")
            ?? throw new ArgumentException("The 'relativePath' parameter for the Office/PDF document is required");

        bool forceOcr = args.TryGetProperty("forceOcr", out var f) && f.GetBoolean();
        string safePath = ToolHelper.ResolveSafePath(rootPath, relative);

        if (!File.Exists(safePath))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Document not found on disk: {relative}");
        }

        if (ingestionService is not null)
        {
            try
            {
                var docInfo = new FileInfo(safePath);
                var doc = new ImportedDocument(
                    Id: 0,
                    DocumentUid: Guid.NewGuid().ToString("N"),
                    OriginalFileName: docInfo.Name,
                    OriginalPath: docInfo.FullName,
                    Sha256: null,
                    MimeType: "application/octet-stream",
                    FileExtension: docInfo.Extension,
                    FileSizeBytes: docInfo.Length,
                    Status: DocumentStatus.Imported,
                    PageCount: 0,
                    ChunkCount: 0,
                    CurrentJobId: null,
                    LastError: null,
                    CreatedAtUtc: DateTimeOffset.UtcNow,
                    UpdatedAtUtc: DateTimeOffset.UtcNow);

                var result = await ingestionService.IngestAsync(
                    doc,
                    checkpoint: null,
                    saveProgressAsync: (_, _) => Task.CompletedTask,
                    forceOcr: forceOcr,
                    cancellationToken: cancellationToken);

                string resJson = JsonSerializer.Serialize(new
                {
                    pageCount = result.PageCount,
                    chunkCount = result.ChunkCount,
                    fileName = docInfo.Name
                }, s_indentedOptions);

                return new AgentToolResult(callId, toolName, true, $"Office/PDF ingestion completed for {relative}:\n{resJson}");
            }
            catch (Exception ex)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, $"Error during ingestion of document {relative}: {ex.Message}");
            }
        }

        return new AgentToolResult(callId, toolName, true, $"Document identified for RAG ingestion: {relative} (IngestionService not registered in this test context)");
    }

    private async Task<AgentToolResult> GenerateImageOnnxAsync(string callId, string toolName, JsonElement args, CancellationToken cancellationToken)
    {
        string prompt = ToolHelper.GetArgString(args, "prompt", "text", "description")
            ?? throw new ArgumentException("The 'prompt' parameter for image generation is required");

        string negativePrompt = ToolHelper.GetArgString(args, "negativePrompt", "negative") ?? "";
        int width = ToolHelper.GetArgInt(args, "width") ?? 512;
        int height = ToolHelper.GetArgInt(args, "height") ?? 512;

        if (imageGenerationService is not null)
        {
            try
            {
                var req = new ImageGenerationRequest(
                    Prompt: prompt,
                    NegativePrompt: negativePrompt,
                    ModelId: null,
                    Width: width,
                    Height: height,
                    Steps: 20,
                    BatchSize: 1,
                    Seed: null);

                var resp = await imageGenerationService.GenerateAsync(req, cancellationToken);
                var generatedList = resp.Images.Select(img => new
                {
                    id = img.Id,
                    fileName = img.FileName,
                    mimeType = img.MimeType,
                    prompt = img.Prompt,
                    width = img.Width,
                    height = img.Height
                });

                string json = JsonSerializer.Serialize(new
                {
                    provider = resp.Provider,
                    message = resp.Message,
                    images = generatedList
                }, s_indentedOptions);

                return new AgentToolResult(callId, toolName, true, $"ONNX DirectML image generated successfully:\n{json}");
            }
            catch (Exception ex)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, $"Unable to generate image with ONNX DirectML: {ex.Message}");
            }
        }

        return new AgentToolResult(callId, toolName, true, $"Simulated ONNX image generation request for prompt '{prompt}' (ImageGenerationService not available in the runner)");
    }

    private async Task<AgentToolResult> QueryRetrievalIndexAsync(string callId, string toolName, JsonElement args, CancellationToken cancellationToken)
    {
        string query = ToolHelper.GetArgString(args, "query", "q", "search")
            ?? throw new ArgumentException("The 'query' parameter is required");

        int topK = ToolHelper.GetArgInt(args, "topK", "limit", "k") ?? 5;

        if (retrievalService is not null)
        {
            try
            {
                var searchReq = new DocumentSearchRequest(
                    Query: query,
                    DocumentIds: Array.Empty<long>(),
                    TopK: topK);

                var searchResp = await retrievalService.SearchAsync(searchReq, cancellationToken);
                float topScore = (float)(searchResp.Results.Count > 0 ? (searchResp.Results[0].ReRankScore ?? searchResp.Results[0].Score) : 0f);
                string cragConfidence = topScore >= 0.75f ? "HIGH (Correct)" : topScore >= 0.40f ? "MEDIUM (Ambiguous)" : "LOW (Incorrect)";

                var items = searchResp.Results.Select(r => new
                {
                    documentId = r.DocumentId,
                    documentName = r.DocumentName,
                    score = r.Score,
                    reRankScore = r.ReRankScore,
                    snippet = r.Snippet?[..Math.Min(250, r.Snippet.Length)],
                    chunkId = r.ChunkId
                });

                string json = JsonSerializer.Serialize(new
                {
                    query = query,
                    totalMatches = searchResp.Results.Count,
                    cragConfidence = cragConfidence,
                    topReRankScore = topScore,
                    keywordBackend = searchResp.KeywordBackend,
                    vectorBackend = searchResp.VectorBackend,
                    results = items
                }, s_indentedOptions);

                string cragHint = topScore < 0.40f
                    ? "\n\n[RETRIEVAL WARNING - LOW CRAG CONFIDENCE (Incorrect)] The retrieved snippets have low relevance to the query. It is recommended to refine the search terms or use web_search or read_file to directly access the sources."
                    : topScore < 0.75f
                        ? "\n\n[RETRIEVAL WARNING - MEDIUM CRAG CONFIDENCE (Ambiguous)] Partial relevance identified in the retrieval index. Verify details with read_file or refine the query."
                        : string.Empty;

                return new AgentToolResult(callId, toolName, true, $"Retrieval search results (SQLite FTS5 + Qdrant vectors | CRAG: {cragConfidence}):\n{json}{cragHint}");
            }
            catch (Exception ex)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, $"Error during retrieval index search: {ex.Message}");
            }
        }

        return new AgentToolResult(callId, toolName, true, $"Simulated retrieval search for query '{query}' (HybridRetrievalService not registered in this context)");
    }
}
