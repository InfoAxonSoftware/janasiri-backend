using DistributionSystem.Application.DTOs.Support;

namespace DistributionSystem.Application.Services.Interfaces;

public interface ISupportChatPublisher
{
    Task PublishMessageAsync(Guid complaintId, ComplaintMessageDto message, CancellationToken cancellationToken = default);
    Task PublishStatusChangedAsync(Guid complaintId, object payload, CancellationToken cancellationToken = default);
}
