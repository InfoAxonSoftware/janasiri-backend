using DistributionSystem.Application.DTOs.Auth;
using DistributionSystem.Application.DTOs.Common;

namespace DistributionSystem.Application.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<UserDto> CreateAdminAccountAsync(CreateAdminAccountRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdminAccountDto>> GetAdminAccountsAsync(CancellationToken cancellationToken = default);
    Task<AdminAccountDto> UpdateAdminAccountAsync(Guid userId, UpdateAdminAccountRequest request, CancellationToken cancellationToken = default);
    Task DeleteAdminAccountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AdminAccountDto> GetCurrentAdminProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task<AdminResetPasswordResultDto> AdminResetUserPasswordAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AdminSetUserPasswordAsync(Guid userId, AdminSetUserPasswordRequest request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PagedResult<LockedUserDto>> GetLockedUsersAsync(int page, int pageSize, string? search, string? role, string? sortField, string? sortDirection, CancellationToken cancellationToken = default);
    Task<UnlockUserResultDto> AdminUnlockUserAsync(Guid userId, Guid adminUserId, CancellationToken cancellationToken = default);
}
