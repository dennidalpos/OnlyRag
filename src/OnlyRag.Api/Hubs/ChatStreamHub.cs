using Microsoft.AspNetCore.SignalR;

namespace OnlyRag.Api.Hubs;

public interface IChatStreamClient
{
    Task ReceiveToken(string token);
    Task StreamCompleted(string messageId);
    Task StreamError(string error);
}

public sealed class ChatStreamHub : Hub<IChatStreamClient>
{
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
    }
}
