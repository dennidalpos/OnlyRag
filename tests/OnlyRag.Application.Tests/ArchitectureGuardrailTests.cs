using System.Text.RegularExpressions;

namespace OnlyRag.Application.Tests;

public sealed class ArchitectureGuardrailTests
{
    [Fact]
    public void ProjectReferences_follow_expected_architecture_boundaries()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "src");

        Dictionary<string, IReadOnlyCollection<string>> allowedReferences = new(StringComparer.OrdinalIgnoreCase)
        {
            ["OnlyRag.App"] = new[] { "OnlyRag.Api", "OnlyRag.Core", "OnlyRag.Infrastructure" },
            ["OnlyRag.Api"] = new[] { "OnlyRag.Application", "OnlyRag.Core", "OnlyRag.Infrastructure", "OnlyRag.Jobs.Abstractions" },
            ["OnlyRag.Application"] = new[] { "OnlyRag.Core", "OnlyRag.Infrastructure", "OnlyRag.Jobs.Abstractions" },
            ["OnlyRag.Core"] = Array.Empty<string>(),
            ["OnlyRag.Infrastructure"] = new[] { "OnlyRag.Core", "OnlyRag.Jobs.Abstractions" },
            ["OnlyRag.Jobs.Abstractions"] = Array.Empty<string>()
        };

        string[] projectFiles = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(projectFiles);

        foreach (string projectFile in projectFiles)
        {
            string projectName = Path.GetFileNameWithoutExtension(projectFile);

            if (!allowedReferences.ContainsKey(projectName))
            {
                continue;
            }

            string[] referencedProjects = ReadReferencedProjects(projectFile);
            HashSet<string> allowed = new(allowedReferences[projectName], StringComparer.OrdinalIgnoreCase);

            foreach (string referencedProject in referencedProjects)
            {
                Assert.True(
                    allowed.Contains(referencedProject),
                    $"{projectName} references {referencedProject}, but only {string.Join(", ", allowed)} are allowed.");
            }
        }
    }

    private static string[] ReadReferencedProjects(string projectFile)
    {
        string content = File.ReadAllText(projectFile);
        MatchCollection matches = Regex.Matches(content, @"<ProjectReference Include=""(?<path>[^""]+)""", RegexOptions.CultureInvariant);

        return matches
            .Select(match => match.Groups["path"].Value)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(projectName => !string.IsNullOrWhiteSpace(projectName))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "OnlyRag.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root from the test output directory.");
    }
}
