using Microsoft.AspNetCore.SignalR;

namespace ErfxWebServer.Hubs;

/// <summary>
/// 실시간 검사 결과 전달용 SignalR Hub
/// </summary>
public class InspectionHub : Hub
{
    private readonly ILogger<InspectionHub> _logger;

    public InspectionHub(ILogger<InspectionHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
