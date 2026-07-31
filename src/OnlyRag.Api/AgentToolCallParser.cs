using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;

namespace OnlyRag.Api;

internal static class AgentToolCallParser
{
    public static AgentToolCall? TryExtractToolCall(string text, ILoggingService? logger = null)
    {
        var calls = TryExtractToolCalls(text, logger);
        return calls.Count > 0 ? calls[0] : null;
    }

    public static List<AgentToolCall> TryExtractToolCalls(string text, ILoggingService? logger = null)
    {
        var list = new List<AgentToolCall>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        // Preemptive cleanup of <think>...</think> or <thought>...</thought> tags to prevent JSON samples in reasoning from interfering with the parser
        string cleanText = Regex.Replace(text, @"<(?:thought|think)>[\s\S]*?</(?:thought|think)>", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(cleanText)) cleanText = text;

        // 1. Look for XML tags (<tool_call>, <tool>, <function_call>)
        var matchTagBlock = Regex.Match(cleanText, @"<(?:tool_call|tool|function_call)>\s*([\s\S]*?)\s*</(?:tool_call|tool|function_call)>", RegexOptions.Singleline);
        if (matchTagBlock.Success)
        {
            ParseAndAddCalls(matchTagBlock.Groups[1].Value, list, logger);
            if (list.Count > 0) return list;
        }

        // 2. Look for markdown code block ```json ... ``` or ``` ... ```
        var matchCodeBlock = Regex.Match(cleanText, @"```(?:json|JSON)?\s*([\s\S]*?)\s*(?:```|$)", RegexOptions.Singleline);
        if (matchCodeBlock.Success)
        {
            ParseAndAddCalls(matchCodeBlock.Groups[1].Value, list, logger);
            if (list.Count > 0) return list;
        }

        // 3. Brace/bracket balancing for unfenced JSON (removing <thought>...</thought> tags to prevent braces in thought text from interfering with the parser)
        string textNoThought = Regex.Replace(text, @"<(?:thought|think)>[\s\S]*?</(?:thought|think)>", "", RegexOptions.IgnoreCase);
        int firstBrace = textNoThought.IndexOf('{');
        int firstBracket = textNoThought.IndexOf('[');

        if (firstBracket != -1 && (firstBrace == -1 || firstBracket < firstBrace))
        {
            int lastBracket = textNoThought.LastIndexOf(']');
            if (lastBracket > firstBracket)
            {
                string jsonCandidate = textNoThought.Substring(firstBracket, lastBracket - firstBracket + 1);
                ParseAndAddCalls(jsonCandidate, list, logger);
                if (list.Count > 0) return list;
            }
        }

        if (firstBrace != -1)
        {
            int openCount = 0;
            int lastBrace = -1;
            for (int i = firstBrace; i < textNoThought.Length; i++)
            {
                if (textNoThought[i] == '{') openCount++;
                else if (textNoThought[i] == '}')
                {
                    openCount--;
                    if (openCount == 0)
                    {
                        lastBrace = i;
                        break;
                    }
                }
            }

            if (lastBrace > firstBrace)
            {
                string jsonCandidate = textNoThought.Substring(firstBrace, lastBrace - firstBrace + 1);
                ParseAndAddCalls(jsonCandidate, list, logger);
                if (list.Count > 0) return list;
            }
        }

        return list;
    }

    private static void ParseAndAddCalls(string rawJson, List<AgentToolCall> targetList, ILoggingService? logger)
    {
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        string sanitizedJson = FixUnescapedControlCharsInJsonStrings(rawJson);
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(sanitizedJson, options);
        }
        catch
        {
            string repairedJson = RepairMalformedJson(sanitizedJson);
            try { doc = JsonDocument.Parse(repairedJson, options); }
            catch { }
        }

        if (doc == null) return;

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var call = ExtractSingleCallFromElement(item);
                    if (call != null) targetList.Add(call);
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                var call = ExtractSingleCallFromElement(root);
                if (call != null) targetList.Add(call);
            }
        }
    }

    private static AgentToolCall? ExtractSingleCallFromElement(JsonElement root)
    {
        JsonElement targetElement = root;
        if (root.TryGetProperty("function", out var fnObj) && fnObj.ValueKind == JsonValueKind.Object)
        {
            targetElement = fnObj;
        }

        string? toolRaw = null;
        if (targetElement.TryGetProperty("tool", out var toolProp)) toolRaw = toolProp.GetString();
        else if (targetElement.TryGetProperty("tool_name", out var toolNameProp)) toolRaw = toolNameProp.GetString();
        else if (targetElement.TryGetProperty("function", out var fnProp) && fnProp.ValueKind == JsonValueKind.String) toolRaw = fnProp.GetString();
        else if (targetElement.TryGetProperty("action", out var actProp)) toolRaw = actProp.GetString();
        else if (targetElement.TryGetProperty("name", out var nameProp)) toolRaw = nameProp.GetString();

        if (string.IsNullOrWhiteSpace(toolRaw)) return null;

        string normalizedTool = NormalizeToolName(toolRaw);
        string argsJson = "{}";

        JsonElement? argsElem = null;
        if (targetElement.TryGetProperty("arguments", out var argsProp)) argsElem = argsProp;
        else if (targetElement.TryGetProperty("args", out var aProp)) argsElem = aProp;
        else if (targetElement.TryGetProperty("parameters", out var pProp)) argsElem = pProp;
        else if (targetElement.TryGetProperty("inputs", out var iProp)) argsElem = iProp;
        else if (root.TryGetProperty("arguments", out var rootArgsProp)) argsElem = rootArgsProp;

        if (argsElem.HasValue)
        {
            if (argsElem.Value.ValueKind == JsonValueKind.String)
            {
                string strVal = argsElem.Value.GetString() ?? "{}";
                argsJson = strVal.Trim().StartsWith('{') ? strVal : "{ \"input\": " + JsonSerializer.Serialize(strVal) + " }";
            }
            else
            {
                argsJson = argsElem.Value.GetRawText();
            }
        }

        string? explanation = root.TryGetProperty("explanation", out var expProp) ? expProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(explanation) && targetElement.TryGetProperty("explanation", out var expProp2))
        {
            explanation = expProp2.GetString();
        }

        return new AgentToolCall(
            CallId: $"call_{Guid.NewGuid():N}"[..10],
            ToolName: normalizedTool,
            ArgumentsJson: argsJson,
            Explanation: explanation);
    }

    public static string RepairMalformedJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return rawJson;
        string repaired = FixUnescapedControlCharsInJsonStrings(rawJson);
        repaired = FixUnescapedStringLiterals(repaired);
        repaired = Regex.Replace(repaired, @"\\(?![\\""/bfnrt]|u[0-9a-fA-F]{4})", @"\\");
        repaired = Regex.Replace(repaired, @"'([^'\\]*(?:\\.[^'\\]*)*?)'", "\"$1\"");
        repaired = Regex.Replace(repaired, @"(?<=[{\s,])([a-zA-Z_][a-zA-Z0-9_]*)\s*:", "\"$1\":");
        repaired = Regex.Replace(repaired, @",\s*([}\]])", "$1");
        repaired = Regex.Replace(repaired, @"(?<=:\s*)True(?=\s*[,}\]])", "true");
        repaired = Regex.Replace(repaired, @"(?<=:\s*)False(?=\s*[,}\]])", "false");
        repaired = Regex.Replace(repaired, @"(?<=:\s*)None(?=\s*[,}\]])", "null");
        return repaired;
    }

    public static string FixUnescapedControlCharsInJsonStrings(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        var sb = new StringBuilder(json.Length + 64);
        bool inString = false;
        bool isEscaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (isEscaped) { sb.Append(c); isEscaped = false; }
                else if (c == '\\') { sb.Append(c); isEscaped = true; }
                else if (c == '"') { sb.Append(c); inString = false; }
                else if (c == '\t') sb.Append("\\t");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inString = true;
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string FixUnescapedStringLiterals(string text)
    {
        return Regex.Replace(text, @"(?<=""(?:content|replacementContent|targetContent|query|commandLine)"":\s*"")([\s\S]*?)(?=""\s*[,}])", m =>
        {
            return m.Value.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n").Replace("\t", "\\t");
        });
    }

    public static bool LooksLikeFailedToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string lower = text.ToLowerInvariant();
        bool hasToolKeyword = lower.Contains("\"tool\"") || lower.Contains("\"tool_name\"") ||
                              lower.Contains("\"action\"") || lower.Contains("\"function\"") ||
                              lower.Contains("<tool_call>") || lower.Contains("<tool>");
        bool hasArgumentsKeyword = lower.Contains("\"arguments\"") || lower.Contains("\"args\"") ||
                                   lower.Contains("\"parameters\"") || lower.Contains("\"inputs\"");
        bool hasJsonBlock = text.Contains("```json") || text.Contains("<tool_call>") || (text.Contains('{') && text.Contains('}'));

        return hasToolKeyword && (hasArgumentsKeyword || hasJsonBlock);
    }

    public static string NormalizeToolName(string toolName)
    {
        string t = toolName.Trim().ToLowerInvariant();
        return t switch
        {
            "list" or "listdir" or "ls" or "dir" or "list_directory" or "list_files" => "list_dir",
            "read" or "readfile" or "read_file_content" or "view_file" or "cat" => "read_file",
            "write" or "writefile" or "create_file" or "create" or "write_to_file" => "write_file",
            "replace" or "replacefile" or "replace_content" or "edit" or "edit_file" => "replace_file_content",
            "multi_replace" or "multi_replace_file_content" or "batch_replace" => "multi_replace_file_content",
            "grep" or "search" or "find" or "grep_search" or "find_in_files" => "grep_search",
            "git_diff" or "git_status" or "git_diff_inspect" or "git" => "git_diff_inspect",
            "run" or "exec" or "execute" or "command" or "terminal" or "run_command" or "cmd" or "powershell" => "run_command",
            "web_search" or "search_web" or "internet_search" or "online_search" or "ddg" or "google" => "web_search",
            "ingest_office" or "ingest_office_doc" or "office_ingest" or "ingest_document" => "ingest_office_doc",
            "generate_image" or "generate_image_onnx" or "image_gen" or "create_image" => "generate_image_onnx",
            "query_retrieval" or "query_retrieval_index" or "search_retrieval" or "vector_search" or "rag_hybrid_search" or "rag_search" => "query_retrieval_index",
            "plan" or "plan_task" or "create_plan" or "update_plan" => "plan_task",
            "reflect" or "reflect_step" or "self_reflection" => "reflect_step",
            "subagent" or "invoke_subagent" or "sub_agent" => "invoke_subagent",
            "task" or "manage_task" => "manage_task",
            _ => t
        };
    }

    public static bool IsReadOnlyTool(AgentToolCall call)
    {
        // Only tools that are truly stateless and idempotent qualify for parallel execution.
        // plan_task and reflect_step are excluded: they mutate agent state (plan checklist, key-facts store).
        string t = call.ToolName.ToLowerInvariant();
        return t is "read_file" or "list_dir" or "grep_search" or "git_diff_inspect" or "web_search" or "query_retrieval_index";
    }
}
