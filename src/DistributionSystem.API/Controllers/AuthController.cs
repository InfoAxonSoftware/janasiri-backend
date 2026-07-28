using System.Security.Claims;
using DistributionSystem.Application.DTOs.Auth;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributionSystem.API.Controllers;

/// <summary>
/// Authentication and authorization endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly INotificationService _notificationService;

    public AuthController(IAuthService authService, INotificationService notificationService)
    {
        _authService = authService;
        _notificationService = notificationService;
    }

    /// <summary>Login with username/email and password</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, ct);
        return Ok(ApiResponse<AuthResponse>.SuccessResponse(result, "Login successful"));
    }

    /// <summary>Register a new user</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request, ct);

        // Notify coordinators and admins about new customer registration
        try
        {
            await _notificationService.SendToRoleAsync(new BroadcastNotificationRequest
            {
                Role = "SalesCoordinator",
                Title = "New Customer Registration",
                Message = $"New customer '{request.ShopName ?? request.Username}' has registered and needs approval.",
                Type = "CustomerRegistration"
            }, ct);
        }
        catch { /* best-effort notification */ }

        return Ok(ApiResponse<AuthResponse>.SuccessResponse(result, "Registration successful"));
    }

    /// <summary>Create a new admin account (SuperAdmin only)</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("create-admin")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), 200)]
    public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminAccountRequest request, CancellationToken ct)
    {
        var result = await _authService.CreateAdminAccountAsync(request, ct);
        return Ok(ApiResponse<UserDto>.SuccessResponse(result, "Admin account created successfully"));
    }

    /// <summary>Get all admin accounts (SuperAdmin only)</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpGet("admins")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AdminAccountDto>>), 200)]
    public async Task<IActionResult> GetAdmins(CancellationToken ct)
    {
        var result = await _authService.GetAdminAccountsAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<AdminAccountDto>>.SuccessResponse(result));
    }

    /// <summary>Get current admin profile details (Admin or SuperAdmin)</summary>
    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet("admin-profile")]
    [ProducesResponseType(typeof(ApiResponse<AdminAccountDto>), 200)]
    public async Task<IActionResult> GetAdminProfile(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.GetCurrentAdminProfileAsync(userId, ct);
        return Ok(ApiResponse<AdminAccountDto>.SuccessResponse(result));
    }

    /// <summary>Update an admin account (SuperAdmin only)</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpPut("admins/{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminAccountDto>), 200)]
    public async Task<IActionResult> UpdateAdmin(Guid userId, [FromBody] UpdateAdminAccountRequest request, CancellationToken ct)
    {
        var result = await _authService.UpdateAdminAccountAsync(userId, request, ct);
        return Ok(ApiResponse<AdminAccountDto>.SuccessResponse(result, "Admin account updated successfully"));
    }

    /// <summary>Deactivate an admin account (SuperAdmin only)</summary>
    [Authorize(Roles = "SuperAdmin")]
    [HttpDelete("admins/{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<string>), 200)]
    public async Task<IActionResult> DeleteAdmin(Guid userId, CancellationToken ct)
    {
        await _authService.DeleteAdminAccountAsync(userId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Admin account deactivated successfully"));
    }

    /// <summary>Refresh JWT access token</summary>
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshTokenAsync(request, ct);
        return Ok(ApiResponse<AuthResponse>.SuccessResponse(result));
    }

    /// <summary>Change password</summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _authService.ChangePasswordAsync(userId, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Password changed successfully"));
    }

    /// <summary>Set a specific password for a user (Admin)</summary>
    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPut("admin/users/{userId:guid}/password")]
    [ProducesResponseType(typeof(ApiResponse<string>), 200)]
    public async Task<IActionResult> AdminSetUserPassword(Guid userId, [FromBody] AdminSetUserPasswordRequest request, CancellationToken ct)
    {
        await _authService.AdminSetUserPasswordAsync(userId, request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Password updated successfully"));
    }

    /// <summary>Generate a new temporary password for a user (Admin)</summary>
    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPost("admin/users/{userId:guid}/temp-password")]
    [ProducesResponseType(typeof(ApiResponse<AdminResetPasswordResultDto>), 200)]
    public async Task<IActionResult> AdminResetUserPassword(Guid userId, CancellationToken ct)
    {
        var result = await _authService.AdminResetUserPasswordAsync(userId, ct);
        return Ok(ApiResponse<AdminResetPasswordResultDto>.SuccessResponse(result, "Temporary password generated"));
    }

    /// <summary>Get currently locked-out user accounts (Admin)</summary>
    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet("admin/locked-users")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<LockedUserDto>>), 200)]
    public async Task<IActionResult> GetLockedUsers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] string? role = null,
        [FromQuery] string? sortField = null, [FromQuery] string? sortDirection = null,
        CancellationToken ct = default)
    {
        var result = await _authService.GetLockedUsersAsync(page, pageSize, search, role, sortField, sortDirection, ct);
        return Ok(ApiResponse<PagedResult<LockedUserDto>>.SuccessResponse(result));
    }

    /// <summary>Unlock a locked user account (Admin)</summary>
    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPost("admin/users/{userId:guid}/unlock")]
    [ProducesResponseType(typeof(ApiResponse<UnlockUserResultDto>), 200)]
    public async Task<IActionResult> UnlockUser(Guid userId, CancellationToken ct)
    {
        var adminUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _authService.AdminUnlockUserAsync(userId, adminUserId, ct);
        return Ok(ApiResponse<UnlockUserResultDto>.SuccessResponse(result, result.Message));
    }

    /// <summary>Request password reset</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await _authService.ForgotPasswordAsync(request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Password reset instructions sent"));
    }

    /// <summary>Reset password with token</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await _authService.ResetPasswordAsync(request, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Password reset successful"));
    }

    /// <summary>Logout (invalidate refresh token)</summary>
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _authService.LogoutAsync(userId, ct);
        return Ok(ApiResponse<string>.SuccessResponse("Logged out successfully"));
    }
}
