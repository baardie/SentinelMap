using Microsoft.AspNetCore.SignalR;

namespace SentinelMap.Api.Hubs;

/// <summary>
/// Real-time track data hub. Clients subscribe to receive entity position updates.
/// M2: broadcasts to all connected clients. M3+: bbox-scoped group filtering.
/// </summary>
public class TrackHub : Hub
{
    public Task SubscribeArea(double west, double south, double east, double north)
    {
        Context.Items["bbox"] = $"{west},{south},{east},{north}";
        return Task.CompletedTask;
    }

    public Task UnsubscribeArea()
    {
        Context.Items.Remove("bbox");
        return Task.CompletedTask;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
