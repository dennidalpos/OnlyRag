using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

const string defaultSessionToken = "playwright-token";

int port = ReadIntArgument(args, "--port", 49153);
string sessionToken = ReadStringArgument(args, "--session-token", defaultSessionToken);
string storageRoot = ReadStringArgument(
    args,
    "--storage-root",
    Path.Combine(Path.GetTempPath(), "OnlyRag.PlaywrightBackendHost", port.ToString()));

ResetStorageRoot(storageRoot);
Directory.CreateDirectory(storageRoot);

AppStoragePaths storagePaths = AppStoragePaths.FromRoot(storageRoot);
InProcessBackendDescriptor descriptor = new(
    storagePaths,
    new LocalSqliteStoreDescriptor(storagePaths),
    LocalJobQueueDescriptor.Default,
    new OllamaEndpointOptions());

await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
    descriptor,
    new InProcessBackendOptions
    {
        Address = IPAddress.Loopback,
        Port = port,
        SessionToken = sessionToken,
        EnableDevelopmentCorsOrigins = true
    });

var hardwareMonitor = backend.Services.GetRequiredService<IHardwareMonitorService>();
await hardwareMonitor.SetEnergyProfileAsync(HardwareEnergyProfile.Performance);

Console.WriteLine($"OnlyRag Playwright backend listening on {backend.BaseUri}");
Console.Out.Flush();

TaskCompletionSource stopSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopSignal.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stopSignal.TrySetResult();

await stopSignal.Task;

static int ReadIntArgument(string[] args, string name, int fallback)
{
    string? raw = ReadOptionalArgument(args, name);
    return int.TryParse(raw, out int value) ? value : fallback;
}

static string ReadStringArgument(string[] args, string name, string fallback)
{
    return ReadOptionalArgument(args, name) is { Length: > 0 } value ? value : fallback;
}

static string? ReadOptionalArgument(string[] args, string name)
{
    for (int index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static void ResetStorageRoot(string storageRoot)
{
    string fullStorageRoot = Path.GetFullPath(storageRoot);
    string expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OnlyRag.PlaywrightBackendHost"));

    if (!fullStorageRoot.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Playwright backend test storage must stay under the test temp directory.");
    }

    if (Directory.Exists(fullStorageRoot))
    {
        Directory.Delete(fullStorageRoot, recursive: true);
    }
}
