using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.Interfaces;

namespace DistributionSystem.Domain.Entities;

public class User : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? CurrentPassword { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsEmailConfirmed { get; set; }
    public bool IsPhoneConfirmed { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public bool MustChangePassword { get; set; }
    public int TokenVersion { get; set; } = 0;
    public DateTime? PasswordChangedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation properties
    public AdminProfile? AdminProfile { get; set; }
    public SalesRepProfile? SalesRepProfile { get; set; }
    public CustomerProfile? CustomerProfile { get; set; }
    public CoordinatorProfile? CoordinatorProfile { get; set; }
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
