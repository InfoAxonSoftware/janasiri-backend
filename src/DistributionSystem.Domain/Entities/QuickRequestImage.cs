namespace DistributionSystem.Domain.Entities;

public class QuickRequestImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuickRequestId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public QuickRequest QuickRequest { get; set; } = null!;
}
