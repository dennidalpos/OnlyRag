using System.Net;
using System.Net.Sockets;
using OnlyRag.Core.Mcp;

namespace OnlyRag.Api.Mcp;

internal static class McpSecurityValidator
{
    private static readonly HashSet<string> BlockedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "cmd.exe", "powershell", "powershell.exe", "pwsh", "pwsh.exe",
        "wscript", "wscript.exe", "cscript", "cscript.exe", "mshta", "mshta.exe"
    };

    public static async Task ValidateAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (server.Transport == McpTransportType.Stdio)
        {
            ValidateStdio(server);
            return;
        }

        if (server.Transport == McpTransportType.HttpSse)
        {
            if (string.IsNullOrWhiteSpace(server.ServerUrl))
            {
                throw new ArgumentException("ServerUrl MCP SSE obbligatorio.");
            }

            await ValidateUriAsync(new Uri(server.ServerUrl, UriKind.Absolute), cancellationToken);
            return;
        }

        throw new ArgumentException("Trasporto MCP non supportato.");
    }

    public static async Task<Uri> ValidateEndpointAsync(
        Uri configuredEndpoint,
        Uri candidateEndpoint,
        CancellationToken cancellationToken)
    {
        await ValidateUriAsync(candidateEndpoint, cancellationToken);
        if (!string.Equals(configuredEndpoint.Scheme, candidateEndpoint.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(configuredEndpoint.Host, candidateEndpoint.Host, StringComparison.OrdinalIgnoreCase)
            || configuredEndpoint.Port != candidateEndpoint.Port)
        {
            throw new InvalidOperationException("L'endpoint MCP SSE deve rimanere sulla stessa origine configurata.");
        }

        return candidateEndpoint;
    }

    private static void ValidateStdio(McpServerConfig server)
    {
        string command = server.Command?.Trim() ?? string.Empty;
        string executableName = Path.GetFileName(command);
        if (string.IsNullOrWhiteSpace(command)
            || Path.IsPathRooted(command)
            || BlockedExecutables.Contains(executableName))
        {
            throw new UnauthorizedAccessException("Comando MCP STDIO non autorizzato.");
        }

        if (server.Arguments?.Any(ContainsShellMetacharacters) == true)
        {
            throw new UnauthorizedAccessException("Gli argomenti MCP STDIO non possono contenere metacaratteri di shell.");
        }

        if (server.EnvironmentVariables?.Keys.Any(key =>
                key.Equals("PATH", StringComparison.OrdinalIgnoreCase)
                || key.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase)
                || key.Equals("COMSPEC", StringComparison.OrdinalIgnoreCase)
                || key.Equals("PSMODULEPATH", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new UnauthorizedAccessException("Le variabili d'ambiente di ricerca eseguibili non sono consentite per MCP.");
        }
    }

    private static async Task ValidateUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Gli endpoint MCP remoti devono usare HTTPS.");
        }

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsPrivateNonLoopbackAddress))
        {
            throw new InvalidOperationException("L'endpoint MCP risolve verso una rete privata o non raggiungibile.");
        }
    }

    private static bool IsPrivateNonLoopbackAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || bytes[0] == 169 && bytes[1] == 254;
        }

        byte[] ipv6 = address.GetAddressBytes();
        return (ipv6[0] & 0xfe) == 0xfc || (ipv6[0] == 0xfe && (ipv6[1] & 0xc0) == 0x80);
    }

    private static bool ContainsShellMetacharacters(string value) =>
        value.IndexOfAny([';', '&', '|', '>', '<', '`', '"', '\'']) >= 0;
}
