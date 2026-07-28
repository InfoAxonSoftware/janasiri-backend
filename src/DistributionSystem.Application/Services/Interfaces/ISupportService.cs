using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Support;

namespace DistributionSystem.Application.Services.Interfaces;

public interface ISupportService
{
    Task<ComplaintDto> CustomerCreateComplaintAsync(Guid userId, CreateComplaintRequest request, CancellationToken ct);
    Task<ComplaintDto> RepCreateComplaintAsync(Guid userId, CreateComplaintRequest request, CancellationToken ct);
    Task<ComplaintDto> CoordinatorCreateComplaintAsync(Guid userId, CreateComplaintRequest request, CancellationToken ct);

    Task<List<ComplaintDto>> CustomerGetComplaintsAsync(Guid userId, CancellationToken ct);
    Task<List<ComplaintDto>> RepGetComplaintsAsync(Guid userId, CancellationToken ct);
    Task<List<ComplaintDto>> CoordinatorGetComplaintsAsync(Guid userId, CancellationToken ct);

    Task<PagedResult<ComplaintDto>> AdminGetComplaintsAsync(int page, int pageSize, string? status, CancellationToken ct);
    Task<ComplaintDto> AdminUpdateStatusAsync(Guid adminUserId, Guid complaintId, string status, CancellationToken ct);

    Task EnsureComplaintAccessAsync(Guid userId, string role, Guid complaintId, CancellationToken ct);
    Task<List<ComplaintMessageDto>> GetMessagesAsync(Guid userId, string role, Guid complaintId, CancellationToken ct);
    Task<ComplaintMessageDto> SendMessageAsync(Guid userId, string role, Guid complaintId, SendComplaintMessageRequest request, CancellationToken ct);
}
