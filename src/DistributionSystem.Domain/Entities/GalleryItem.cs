namespace DistributionSystem.Domain.Entities;

public class GalleryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    /// <summary>JSON array of additional image URLs e.g. ["/uploads/gallery/a.jpg",...]</summary>
    public string? ExtraImageUrlsJson { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
