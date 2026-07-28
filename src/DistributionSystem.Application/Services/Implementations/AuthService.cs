using System;
using System.Security.Claims;
using System.Security.Cryptography;
using DistributionSystem.Application.DTOs.Auth;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DistributionSystem.Application.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtService _jwtService;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthService> _logger;
    private readonly int _maxFailedLoginAttempts;
    private readonly int _lockoutDurationMinutes;
    private readonly int _refreshTokenExpirationDays;
    private readonly int _accessTokenExpirationMinutes;
    private readonly decimal _defaultCustomerCreditLimit;

    public AuthService(IUnitOfWork unitOfWork, JwtService jwtService, IConfiguration configuration, IEmailService emailService, ILogger<AuthService> logger)
    {
        _unitOfWork = unitOfWork;
        _jwtService = jwtService;
        _emailService = emailService;
        _logger = logger;
        _maxFailedLoginAttempts = configuration.GetValue("SecuritySettings:MaxFailedLoginAttempts", 5);
        _lockoutDurationMinutes = configuration.GetValue("SecuritySettings:LockoutDurationMinutes", 15);
        _refreshTokenExpirationDays = configuration.GetValue("JwtSettings:RefreshTokenExpirationDays", 7);
        _accessTokenExpirationMinutes = configuration.GetValue("JwtSettings:AccessTokenExpirationMinutes", 60);
        _defaultCustomerCreditLimit = configuration.GetValue("DefaultSettings:CustomerCreditLimit", 50000m);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Repository<User>().Query()
            .Include(u => u.SalesRepProfile)
            .Include(u => u.CoordinatorProfile)
            .FirstOrDefaultAsync(u =>
                EF.Functions.ILike(u.Username, request.Username)
                || EF.Functions.ILike(u.Email, request.Username)
                || (u.SalesRepProfile != null && EF.Functions.ILike(u.SalesRepProfile.EmployeeCode, request.Username))
                || (u.CoordinatorProfile != null && EF.Functions.ILike(u.CoordinatorProfile.EmployeeCode, request.Username)),
                cancellationToken)
            ?? throw new BusinessException("Invalid username or password", "AUTH_INVALID_CREDENTIALS");

        if (!user.IsActive)
            throw new BusinessException("Account is deactivated", "AUTH_ACCOUNT_INACTIVE");

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            throw new BusinessException("Account is locked. Please try again later.", "AUTH_ACCOUNT_LOCKED");

        if (!PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            // Fallback: check against stored plaintext temp password (set by admin reset)
            var tempMatch = !string.IsNullOrWhiteSpace(user.CurrentPassword) && user.CurrentPassword == request.Password;
            if (!tempMatch)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= _maxFailedLoginAttempts)
                {
                    user.LockoutEnd = DateTime.UtcNow.AddMinutes(_lockoutDurationMinutes);
                }
                _unitOfWork.Repository<User>().Update(user);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw new BusinessException("Invalid username or password", "AUTH_INVALID_CREDENTIALS");
            }

            // Temp password matched — re-hash it so future logins use the proper hash
            user.PasswordHash = PasswordHasher.Hash(request.Password);
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTime.UtcNow;

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshToken = _jwtService.GenerateRefreshToken();
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_refreshTokenExpirationDays);

        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_accessTokenExpirationMinutes),
            User = MapToUserDto(user)
        };
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Role == UserRole.Customer && string.IsNullOrWhiteSpace(request.Username) && !string.IsNullOrWhiteSpace(request.ShopName))
            request.Username = request.ShopName;

        var normalizedUsername = request.Username.Trim();
        var roleKey = request.Role.ToString().ToLowerInvariant();
        var normalizedEmail = string.IsNullOrWhiteSpace(request.Email)
            ? $"{normalizedUsername.ToLowerInvariant()}@{roleKey}.local"
            : request.Email.Trim().ToLowerInvariant();

        if (await _unitOfWork.Repository<User>().AnyAsync(u => u.Username == normalizedUsername, cancellationToken))
            throw new BusinessException("Username already exists", "AUTH_USERNAME_EXISTS");

        if (await _unitOfWork.Repository<User>().AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
            throw new BusinessException("Email already exists", "AUTH_EMAIL_EXISTS");

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && await _unitOfWork.Repository<User>().AnyAsync(u => u.PhoneNumber == request.PhoneNumber, cancellationToken))
            throw new BusinessException("Phone number already exists", "AUTH_PHONE_EXISTS");

        if (!string.IsNullOrWhiteSpace(request.ShopName) && await _unitOfWork.Repository<CustomerProfile>().AnyAsync(c => c.ShopName == request.ShopName, cancellationToken))
            throw new BusinessException("Shop name already exists", "AUTH_SHOP_NAME_EXISTS");

        var user = new User
        {
            Username = normalizedUsername,
            Email = normalizedEmail,
            PasswordHash = PasswordHasher.Hash(request.Password),
            CurrentPassword = request.Password,
            PhoneNumber = request.PhoneNumber,
            Role = request.Role,
            IsActive = true,
            IsEmailConfirmed = false,
            TokenVersion = 0,
            PasswordChangedAt = DateTime.UtcNow
        };

        await _unitOfWork.Repository<User>().AddAsync(user, cancellationToken);

        // Create role-specific profile
        switch (request.Role)
        {
            case UserRole.SuperAdmin:
            case UserRole.Admin:
                await _unitOfWork.Repository<AdminProfile>().AddAsync(new AdminProfile
                {
                    UserId = user.Id,
                    FullName = request.FullName ?? normalizedUsername
                }, cancellationToken);
                break;
            case UserRole.SalesRep:
                await _unitOfWork.Repository<SalesRepProfile>().AddAsync(new SalesRepProfile
                {
                    UserId = user.Id,
                    FullName = request.FullName ?? normalizedUsername,
                    EmployeeCode = request.EmployeeCode ?? $"REP-{DateTime.UtcNow.Ticks % 10000}",
                    HireDate = DateTime.UtcNow
                }, cancellationToken);
                break;
            case UserRole.Customer:
                var customerProfile = new CustomerProfile
                {
                    UserId = user.Id,
                    ShopName = request.ShopName ?? normalizedUsername
                };

                // Parse location string and populate address
                if (!string.IsNullOrEmpty(request.Location))
                {
                    var locationParts = request.Location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (locationParts.Length >= 1)
                    {
                        customerProfile.Address.City = locationParts[0]; // First part is city
                    }
                    if (locationParts.Length >= 2)
                    {
                        customerProfile.Address.State = locationParts[1]; // Second part is state/country
                    }
                }

                await _unitOfWork.Repository<CustomerProfile>().AddAsync(customerProfile, cancellationToken);
                break;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshToken = _jwtService.GenerateRefreshToken();
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_refreshTokenExpirationDays);
        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_accessTokenExpirationMinutes),
            User = MapToUserDto(user)
        };
    }

    public async Task<UserDto> CreateAdminAccountAsync(CreateAdminAccountRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            throw new BusinessException("Username is required", "AUTH_USERNAME_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new BusinessException("Password is required", "AUTH_PASSWORD_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.FullName))
            throw new BusinessException("Full name is required", "AUTH_FULLNAME_REQUIRED");
        if (request.Password.Length < 6)
            throw new BusinessException("Password must be at least 6 characters", "AUTH_PASSWORD_WEAK");

        var normalizedUsername = request.Username.Trim();
        var normalizedEmail = string.IsNullOrWhiteSpace(request.Email)
            ? $"{normalizedUsername.ToLowerInvariant()}@admin.local"
            : request.Email.Trim().ToLowerInvariant();
        var normalizedFullName = request.FullName.Trim();
        var normalizedPhone = (request.PhoneNumber ?? string.Empty).Trim();
        var permanentPassword = request.Password;

        if (await _unitOfWork.Repository<User>().AnyAsync(u => u.Username == normalizedUsername, cancellationToken))
            throw new BusinessException("Username already exists", "AUTH_USERNAME_EXISTS");

        if (await _unitOfWork.Repository<User>().AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
            throw new BusinessException("Email already exists", "AUTH_EMAIL_EXISTS");

        if (!string.IsNullOrWhiteSpace(normalizedPhone)
            && await _unitOfWork.Repository<User>().AnyAsync(u => u.PhoneNumber == normalizedPhone, cancellationToken))
            throw new BusinessException("Phone number already exists", "AUTH_PHONE_EXISTS");

        var user = new User
        {
            Username = normalizedUsername,
            Email = normalizedEmail,
            PasswordHash = PasswordHasher.Hash(permanentPassword),
            CurrentPassword = permanentPassword,
            PhoneNumber = normalizedPhone,
            Role = UserRole.Admin,
            IsActive = true,
            IsEmailConfirmed = false,
            MustChangePassword = false,
            TokenVersion = 0,
            PasswordChangedAt = DateTime.UtcNow
        };

        await _unitOfWork.Repository<User>().AddAsync(user, cancellationToken);

        await _unitOfWork.Repository<AdminProfile>().AddAsync(new AdminProfile
        {
            UserId = user.Id,
            FullName = normalizedFullName,
            Department = request.Department,
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Email))
            _ = TrySendAdminCredentialsEmailAsync(normalizedEmail, normalizedUsername, permanentPassword, normalizedFullName, cancellationToken);

        return MapToUserDto(user);
    }

    public async Task<IReadOnlyList<AdminAccountDto>> GetAdminAccountsAsync(CancellationToken cancellationToken = default)
    {
        var admins = await _unitOfWork.Repository<AdminProfile>().Query()
            .Include(a => a.User)
            .Where(a => a.User.Role == UserRole.Admin)
            .OrderBy(a => a.FullName)
            .ToListAsync(cancellationToken);

        return admins.Select(MapToAdminAccountDto).ToList();
    }

    public async Task<AdminAccountDto> UpdateAdminAccountAsync(Guid userId, UpdateAdminAccountRequest request, CancellationToken cancellationToken = default)
    {
        var adminProfile = await _unitOfWork.Repository<AdminProfile>().Query()
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId && a.User.Role == UserRole.Admin, cancellationToken)
            ?? throw new NotFoundException("Admin user", userId);

        string? normalizedEmail = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var existingByEmail = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.Id != userId, cancellationToken);
            if (existingByEmail is not null)
                throw new BusinessException("Email already exists", "AUTH_EMAIL_EXISTS");
        }

        var user = adminProfile.User;
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
            user.Email = normalizedEmail;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
            user.PhoneNumber = request.PhoneNumber;

        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;

        adminProfile.FullName = string.IsNullOrWhiteSpace(request.FullName) ? adminProfile.FullName : request.FullName;
        adminProfile.Department = request.Department;

        _unitOfWork.Repository<AdminProfile>().Update(adminProfile);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToAdminAccountDto(adminProfile);
    }

    public async Task DeleteAdminAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var adminProfile = await _unitOfWork.Repository<AdminProfile>().Query()
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId && a.User.Role == UserRole.Admin, cancellationToken)
            ?? throw new NotFoundException("Admin user", userId);

        var user = adminProfile.User;
        user.IsActive = false;
        _unitOfWork.Repository<User>().Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminAccountDto> GetCurrentAdminProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.Repository<AdminProfile>().Query()
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.UserId == userId && (a.User.Role == UserRole.Admin || a.User.Role == UserRole.SuperAdmin), cancellationToken)
            ?? throw new NotFoundException("Admin profile", userId);

        return MapToAdminAccountDto(profile);
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var principal = _jwtService.GetPrincipalFromExpiredToken(request.AccessToken)
            ?? throw new BusinessException("Invalid token", "AUTH_INVALID_TOKEN");

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        if (user.RefreshToken != request.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            throw new BusinessException("Invalid refresh token", "AUTH_INVALID_REFRESH_TOKEN");

        var newAccessToken = _jwtService.GenerateAccessToken(user);
        var newRefreshToken = _jwtService.GenerateRefreshToken();
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_refreshTokenExpirationDays);
        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_accessTokenExpirationMinutes),
            User = MapToUserDto(user)
        };
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var currentPasswordMatches = PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash);

        if (!currentPasswordMatches)
        {
            var temporaryPassword = user.CurrentPassword;
            currentPasswordMatches = !string.IsNullOrWhiteSpace(temporaryPassword) && temporaryPassword == request.CurrentPassword;
        }

        if (!currentPasswordMatches)
            throw new BusinessException("Current password is incorrect", "AUTH_WRONG_PASSWORD");

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        user.CurrentPassword = request.NewPassword;
        user.MustChangePassword = false;
        user.TokenVersion += 1;
        user.PasswordChangedAt = DateTime.UtcNow;
        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminResetPasswordResultDto> AdminResetUserPasswordAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var tempPassword = GenerateTemporaryPassword();
        user.PasswordHash = PasswordHasher.Hash(tempPassword);
        user.CurrentPassword = tempPassword;
        user.MustChangePassword = false;
        user.TokenVersion += 1;
        user.PasswordChangedAt = DateTime.UtcNow;
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;

        // Invalidate active refresh sessions so the temp password policy applies on next login.
        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;

        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AdminResetPasswordResultDto
        {
            UserId = user.Id,
            Username = user.Username,
            TemporaryPassword = tempPassword,
            MustChangePassword = false,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    public async Task AdminSetUserPasswordAsync(Guid userId, AdminSetUserPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new BusinessException("Password cannot be empty");

        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        user.PasswordHash = PasswordHasher.Hash(request.Password.Trim());
        user.CurrentPassword = request.Password.Trim();
        user.MustChangePassword = false;
        user.TokenVersion += 1;
        user.PasswordChangedAt = DateTime.UtcNow;
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;

        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Password reset via email is not yet implemented.");
    }

    public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Password reset via email is not yet implemented.");
    }

    public async Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken);
        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<PagedResult<LockedUserDto>> GetLockedUsersAsync(int page, int pageSize, string? search, string? role, string? sortField, string? sortDirection, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var query = _unitOfWork.Repository<User>().Query()
            .Include(u => u.AdminProfile)
            .Include(u => u.SalesRepProfile)
            .Include(u => u.CoordinatorProfile)
            .Include(u => u.CustomerProfile)
            .Where(u => u.LockoutEnd != null && u.LockoutEnd > now);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.Username, pattern) ||
                EF.Functions.ILike(u.Email, pattern) ||
                (u.SalesRepProfile != null && EF.Functions.ILike(u.SalesRepProfile.FullName, pattern)) ||
                (u.SalesRepProfile != null && EF.Functions.ILike(u.SalesRepProfile.EmployeeCode, pattern)) ||
                (u.CoordinatorProfile != null && EF.Functions.ILike(u.CoordinatorProfile.FullName, pattern)) ||
                (u.CoordinatorProfile != null && EF.Functions.ILike(u.CoordinatorProfile.EmployeeCode, pattern)) ||
                (u.AdminProfile != null && EF.Functions.ILike(u.AdminProfile.FullName, pattern)) ||
                (u.CustomerProfile != null && EF.Functions.ILike(u.CustomerProfile.ShopName, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, true, out var parsedRole))
            query = query.Where(u => u.Role == parsedRole);

        var totalCount = await query.CountAsync(cancellationToken);

        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sortField?.ToLowerInvariant()) switch
        {
            "username" => descending ? query.OrderByDescending(u => u.Username) : query.OrderBy(u => u.Username),
            "role" => descending ? query.OrderByDescending(u => u.Role) : query.OrderBy(u => u.Role),
            "accessfailedcount" or "failedattempts" => descending ? query.OrderByDescending(u => u.FailedLoginAttempts) : query.OrderBy(u => u.FailedLoginAttempts),
            _ => descending ? query.OrderByDescending(u => u.LockoutEnd) : query.OrderBy(u => u.LockoutEnd),
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<LockedUserDto>
        {
            Items = items.Select(u => MapToLockedUserDto(u, now)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<UnlockUserResultDto> AdminUnlockUserAsync(Guid userId, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var isCurrentlyLocked = user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow;

        if (!isCurrentlyLocked)
        {
            return new UnlockUserResultDto
            {
                UserId = user.Id,
                Username = user.Username,
                WasLocked = false,
                Message = $"{user.Username} is already unlocked."
            };
        }

        var previousFailedCount = user.FailedLoginAttempts;
        var previousLockoutEnd = user.LockoutEnd;

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "UserAccountUnlocked: Admin {AdminUserId} unlocked user {UnlockedUserId} ({UnlockedUsername}). PreviousFailedAttempts={PreviousFailedCount}, PreviousLockoutEnd={PreviousLockoutEnd:o}, AtUtc={AtUtc:o}",
            adminUserId, user.Id, user.Username, previousFailedCount, previousLockoutEnd, DateTime.UtcNow);

        return new UnlockUserResultDto
        {
            UserId = user.Id,
            Username = user.Username,
            WasLocked = true,
            Message = $"{user.Username} has been unlocked successfully."
        };
    }

    private static LockedUserDto MapToLockedUserDto(User user, DateTime now)
    {
        var displayName = user.AdminProfile?.FullName
            ?? user.SalesRepProfile?.FullName
            ?? user.CoordinatorProfile?.FullName
            ?? user.CustomerProfile?.ShopName
            ?? user.Username;

        var employeeCode = user.SalesRepProfile?.EmployeeCode ?? user.CoordinatorProfile?.EmployeeCode;

        return new LockedUserDto
        {
            UserId = user.Id,
            Username = user.Username,
            DisplayName = displayName,
            EmployeeCode = employeeCode,
            Role = user.Role.ToString(),
            AccessFailedCount = user.FailedLoginAttempts,
            LockoutEnd = user.LockoutEnd!.Value,
            LockedForDisplay = FormatRemaining(user.LockoutEnd.Value - now),
        };
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "Less than a minute";
        if (remaining.TotalMinutes < 1) return "Less than a minute";
        if (remaining.TotalHours < 1) return $"{(int)Math.Ceiling(remaining.TotalMinutes)} min";
        if (remaining.TotalDays < 1) return $"{(int)Math.Floor(remaining.TotalHours)} hr {remaining.Minutes} min";
        return $"{(int)Math.Floor(remaining.TotalDays)} day(s)";
    }

    private static UserDto MapToUserDto(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        Role = user.Role.ToString(),
        IsActive = user.IsActive,
        MustChangePassword = user.MustChangePassword,
        LastLoginAt = user.LastLoginAt
    };

        private static AdminAccountDto MapToAdminAccountDto(AdminProfile profile) => new()
        {
                Id = profile.Id,
                UserId = profile.UserId,
                Username = profile.User.Username,
                Email = profile.User.Email,
                PhoneNumber = profile.User.PhoneNumber,
                FullName = profile.FullName,
                Department = profile.Department,
                IsActive = profile.User.IsActive,
                MustChangePassword = profile.User.MustChangePassword,
                TemporaryPassword = profile.User.CurrentPassword,
                CreatedAt = profile.CreatedAt,
        };

        private static string GenerateTemporaryPassword()
        {
            const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lower = "abcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string special = "!@#$%^&*";
            var all = upper + lower + digits + special;

            var password = new char[8];
            password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
            password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
            password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
            password[3] = special[RandomNumberGenerator.GetInt32(special.Length)];

            for (var i = 4; i < password.Length; i++)
            {
                password[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
            }

            for (var i = password.Length - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(i + 1);
                (password[i], password[j]) = (password[j], password[i]);
            }

            return new string(password);
        }

        private async Task TrySendAdminCredentialsEmailAsync(string toEmail, string username, string tempPassword, string? fullName, CancellationToken ct)
        {
                try
                {
                        var displayName = string.IsNullOrWhiteSpace(fullName) ? "Administrator" : fullName;
                        var subject = "Your Admin Account Credentials";
                        var body = $@"
                                <div style='font-family: system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif;'>
                                    <h2 style='color: #0f172a;'>Welcome, {displayName}!</h2>
                                    <p style='color: #334155;'>Your admin account has been created. Use the credentials below to log in.</p>
                                    <table style='width:100%; border-collapse: collapse; margin-top: 16px;'>
                                        <tr>
                                            <td style='padding: 8px; font-weight: 600; color: #334155;'>Username (ID Number)</td>
                                            <td style='padding: 8px; color: #0f172a; font-weight: 600;'>{username}</td>
                                        </tr>
                                        <tr>
                                            <td style='padding: 8px; font-weight: 600; color: #334155;'>Password</td>
                                            <td style='padding: 8px; color: #0f172a; font-weight: 600; font-family: monospace;'>{tempPassword}</td>
                                        </tr>
                                    </table>
                                </div>
                        ";

                        await _emailService.SendEmailAsync(toEmail, subject, body, ct);
                }
                catch
                {
                        // Best-effort email delivery.
                }
        }
}
