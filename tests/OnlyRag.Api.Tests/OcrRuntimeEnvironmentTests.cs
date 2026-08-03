using System.Text.Json;
using OnlyRag.Api;

namespace OnlyRag.Api.Tests;

public sealed class OcrRuntimeEnvironmentTests
{
    [Fact]
    public void Inspect_ReportsMissingAndInvalidEnvironmentWithoutThrowing()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        OcrRuntimeEnvironment environment = new(directory.Path);

        Assert.Equal("missing", environment.Inspect().State);

        string venv = Path.Combine(directory.Path, ".venv");
        Directory.CreateDirectory(Path.Combine(venv, "Scripts"));
        File.WriteAllText(Path.Combine(venv, "Scripts", "python.exe"), string.Empty);
        File.WriteAllText(Path.Combine(venv, ".requirements-stamp"), "not json");

        Assert.Equal("corrupt", environment.Inspect().State);
    }

    [Fact]
    public void Commit_PublishesVerifiedStagingEnvironmentAndReplacesPreviousRuntime()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        OcrRuntimeEnvironment environment = new(directory.Path);
        string livePython = Path.Combine(directory.Path, ".venv", "Scripts", "python.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(livePython)!);
        File.WriteAllText(livePython, "old");
        File.WriteAllText(Path.Combine(directory.Path, ".venv", ".requirements-stamp"), "{}");

        using (OcrRuntimeEnvironmentTransaction transaction = environment.BeginTransaction())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(transaction.PythonPath)!);
            File.WriteAllText(transaction.PythonPath, "new");
            transaction.Commit("cpu", "requirements-cpu.txt");
        }

        Assert.Equal("new", File.ReadAllText(livePython));
        Assert.Equal("ready", environment.Inspect().State);
        using JsonDocument stamp = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.Path, ".venv", ".requirements-stamp")));
        Assert.Equal("cpu", stamp.RootElement.GetProperty("runtimeName").GetString());
        Assert.DoesNotContain(Directory.GetDirectories(directory.Path),
            path => !string.Equals(Path.GetFileName(path), ".venv", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FailedTransaction_LeavesExistingRuntimeUntouched()
    {
        using TemporaryDirectory directory = TemporaryDirectory.Create();
        OcrRuntimeEnvironment environment = new(directory.Path);
        string livePython = Path.Combine(directory.Path, ".venv", "Scripts", "python.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(livePython)!);
        File.WriteAllText(livePython, "old");

        using (environment.BeginTransaction())
        {
        }

        Assert.Equal("old", File.ReadAllText(livePython));
        Assert.Empty(Directory.GetDirectories(directory.Path, ".venv.staging-*"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }
        public static TemporaryDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OnlyRag.OcrEnvironment.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
