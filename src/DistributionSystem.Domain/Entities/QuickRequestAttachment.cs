namespace DistributionSystem.Domain.Entities;

public class QuickRequestAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid QuickRequestId { get; set; }

    // New file-storage key, e.g. private/quick-requests/xxx/abc.pdf
    public string StorageKey { get; set; } = string.Empty;

    // Original browser-supplied filename for display/download
    public string OriginalFileName { get; set; } = string.Empty;

    // e.g. application/pdf, image/jpeg
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public QuickRequest QuickRequest { get; set; } = null!;
}