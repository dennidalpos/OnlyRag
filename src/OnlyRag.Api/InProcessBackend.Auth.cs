using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static string ResolveSessionToken(InProcessBackendOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SessionToken))
        {
            return options.SessionToken;
        }

        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static void UseSessionTokenAuthentication(WebApplication app, string sessionToken)
    {
        app.Use(async (context, next) =>
        {
            if (IsHealthRequest(context.Request) || !context.Request.Path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            if (IsValidSessionToken(context.Request, sessionToken))
            {
                await next();
                return;
            }

            await WriteProblemAsync(
                context,
                "Unauthorized",
                "API session token missing or invalid.",
                StatusCodes.Status401Unauthorized,
                "unauthorized");
        });
    }

    private static bool IsHealthRequest(HttpRequest request)
    {
        return request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidSessionToken(HttpRequest request, string sessionToken)
    {
        if (!request.Headers.TryGetValue(OnlyRagApiHeaders.SessionTokenHeaderName, out var values)
            || values.Count != 1)
        {
            return false;
        }

        string? suppliedToken = values[0];
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return false;
        }

        byte[] suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(sessionToken);
        return suppliedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
