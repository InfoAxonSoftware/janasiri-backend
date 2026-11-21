namespace DistributionSystem.Domain.Entities;

/// <summary>
/// A user's private trash state for a business record. A row means the record is
/// hidden from that user; PurgedAt removes it from their trash without deleting
/// the shared business record. Admin global deletion is tracked on the record.
/// </summary>
public class UserRecordTrash
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string RecordKind { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PurgedAt { get; set; }
}
