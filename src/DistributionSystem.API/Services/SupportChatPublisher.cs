using DistributionSystem.API.Hubs;
using DistributionSystem.Application.DTOs.Support;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace DistributionSystem.API.Services;

public class SupportChatPublisher : ISupportChatPublisher
{
    private readonly IHubContext<SupportHub> _hubContext;

    public SupportChatPublisher(IHubContext<SupportHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishMessageAsync(Guid complaintId, ComplaintMessageDto message, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.Group($"complaint_{complaintId}")
            .SendAsync("SupportMessageReceived", message, cancellationToken);
    }

    public Task PublishStatusChangedAsync(Guid complaintId, object payload, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.Group($"complaint_{complaintId}")
            .SendAsync("SupportStatusChanged", payload, cancellationToken);
    }
}
