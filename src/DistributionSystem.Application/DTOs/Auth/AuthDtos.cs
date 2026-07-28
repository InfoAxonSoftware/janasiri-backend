using DistributionSystem.Domain.Enums;

namespace DistributionSystem.Application.DTOs.Auth;

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Password { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string? FullName { get; set; }
    public string? ShopName { get; set; }
    public string? Location { get; set; }
    public string? EmployeeCode { get; set; }
}

public class CreateAdminAccountRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? FullName { get; set; }
    public string? Department { get; set; }
}

public class AdminAccountDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Department { get; set; }
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public string? TemporaryPassword { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateAdminAccountRequest
{
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? FullName { get; set; }
    public string? Department { get; set; }
    public bool? IsActive { get; set; }
}

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public UserDto User { get; set; } = null!;
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class RefreshTokenRequest
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    public string? Email { get; set; }
}

public class AdminSetUserPasswordRequest
{
    public string Password { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string? Email { get; set; }
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class AdminResetPasswordResultDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string TemporaryPassword { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}

public class LockedUserDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public string Role { get; set; } = string.Empty;
    public int AccessFailedCount { get; set; }
    public DateTime LockoutEnd { get; set; }
    public string LockedForDisplay { get; set; } = string.Empty;
}

public class UnlockUserResultDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool WasLocked { get; set; }
    public string Message { get; set; } = string.Empty;
}
