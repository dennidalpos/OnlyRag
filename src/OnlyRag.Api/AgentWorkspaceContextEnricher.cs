using System.Text;

namespace OnlyRag.Api;

internal static class AgentWorkspaceContextEnricher
{
    public static string EnrichGoalWithWorkspaceContext(string goal, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            var noWsSb = new StringBuilder();
            noWsSb.AppendLine(goal);
            noWsSb.AppendLine();
            noWsSb.AppendLine("[NO ACTIVE WORKSPACE]");
            noWsSb.AppendLine("Operating in workspace-free chat mode. No local project directory is authorized.");
            noWsSb.AppendLine("AGENT INSTRUCTIONS:");
            noWsSb.AppendLine("1. Answer conversational questions, planning requests, and general coding questions directly in text.");
            noWsSb.AppendLine("2. Do NOT attempt to read local files, list directories, or run local terminal commands on disk unless the user authorizes a workspace.");
            return noWsSb.ToString();
        }

        var sb = new StringBuilder();
        sb.AppendLine(goal);
        sb.AppendLine();
        sb.AppendLine("[ACTIVE WORKSPACE CONTEXT]");
        sb.AppendLine($"Project root folder: {workspaceRoot}");

        try
        {
            if (Directory.Exists(workspaceRoot))
            {
                var detectedItems = new List<string>();
                if (File.Exists(Path.Combine(workspaceRoot, "AGENTS.md"))) detectedItems.Add("- AGENTS.md (General repository instructions and conventions)");
                if (File.Exists(Path.Combine(workspaceRoot, "PROJECT_STATUS.json"))) detectedItems.Add("- PROJECT_STATUS.json (Active todos and project status)");
                if (File.Exists(Path.Combine(workspaceRoot, "workspace_settings.json"))) detectedItems.Add("- workspace_settings.json (Active workspace settings and switches)");
                if (File.Exists(Path.Combine(workspaceRoot, "README.md"))) detectedItems.Add("- README.md (Repository overview and guide)");
                if (Directory.Exists(Path.Combine(workspaceRoot, "skills"))) detectedItems.Add("- skills/ (Domain skills and guidelines directory)");

                if (detectedItems.Count > 0)
                {
                    sb.AppendLine("Context and configuration files identified at root:");
                    foreach (var item in detectedItems) sb.AppendLine(item);
                }

                string statusPath = Path.Combine(workspaceRoot, "PROJECT_STATUS.json");
                if (File.Exists(statusPath))
                {
                    try
                    {
                        string statusJson = File.ReadAllText(statusPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(statusJson) && statusJson.Length < 3000)
                        {
                            sb.AppendLine("\nCurrent contents of PROJECT_STATUS.json:");
                            sb.AppendLine(statusJson.Trim());
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        sb.AppendLine($"\n[PROJECT_STATUS READ FAILED] {ex.Message}");
                    }
                }

                string settingsPath = Path.Combine(workspaceRoot, "workspace_settings.json");
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        string settingsJson = File.ReadAllText(settingsPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(settingsJson) && settingsJson.Length < 2000)
                        {
                            sb.AppendLine("\nWorkspace settings/configuration from workspace_settings.json:");
                            sb.AppendLine(settingsJson.Trim());
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        sb.AppendLine($"\n[WORKSPACE SETTINGS READ FAILED] {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            sb.AppendLine($"\n[WORKSPACE CONTEXT READ FAILED] {ex.Message}");
        }

        sb.AppendLine("\nAGENT INSTRUCTIONS:");
        sb.AppendLine("1. Explore files and project structure only when strictly necessary. If the structure is already known or provided in context, skip list_dir and proceed directly with the task.");
        sb.AppendLine("2. If present, read and follow AGENTS.md and PROJECT_STATUS.json as priorities.");

        return sb.ToString();
    }
}
