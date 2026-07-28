using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Customer;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.ValueObjects;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using DistributionSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public class CustomerRegistrationService : ICustomerRegistrationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly IFileStorageService _fileStorage;

    private static readonly string[] AllowedDocExtensions = [".pdf", ".jpg", ".jpeg", ".png"];
    private static readonly string[] AllowedDocContentTypes = ["application/pdf", "image/jpeg", "image/png"];
    private const long MaxDocSizeBytes = 10 * 1024 * 1024;

    public CustomerRegistrationService(IUnitOfWork unitOfWork, IEmailService emailService, IFileStorageService fileStorage)
    {
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _fileStorage = fileStorage;
    }

    public async Task<CustomerRegistrationRequestDto> SubmitAsync(
        SubmitRegistrationFormRequest data,
        IFormFile? businessRegDoc,
        IFormFile? businessAddressDoc,
        IFormFile? vatDoc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(data.BusinessRegistrationNumber))
            throw new BusinessException("Business Registration Number is required");

        var requestId = Guid.NewGuid();
        var module = $"customer-registrations/{requestId}";

        string? businessRegPath = await SaveDocAsync(businessRegDoc, module, ct);
        string? businessAddressPath = await SaveDocAsync(businessAddressDoc, module, ct);
        string? vatPath = await SaveDocAsync(vatDoc, module, ct);

        if (data.RegionId.HasValue)
        {
            var hasRegion = await _unitOfWork.Repository<Region>().AnyAsync(r => r.Id == data.RegionId.Value, ct);
            if (!hasRegion)
                throw new NotFoundException("Region", data.RegionId.Value);
        }

        if (data.SubRegionId.HasValue)
        {
            var hasSubRegion = await _unitOfWork.Repository<SubRegion>().AnyAsync(s => s.Id == data.SubRegionId.Value, ct);
            if (!hasSubRegion)
                throw new NotFoundException("SubRegion", data.SubRegionId.Value);
        }

        var registrationEmail = string.IsNullOrWhiteSpace(data.Email)
            ? string.Empty
            : data.Email.Trim().ToLowerInvariant();

        var entity = new CustomerRegistrationRequest
        {
            Id = requestId,
            CustomerType = data.CustomerType,
            CustomerName = data.CustomerName,
            BusinessRegistrationNumber = data.BusinessRegistrationNumber.Trim(),
            RegisteredAddress = data.RegisteredAddress,
            IncorporateDate = data.IncorporateDate,
            BusinessName = data.BusinessName,
            BusinessLocation = data.BusinessLocation,
            Telephone = data.Telephone,
            Email = registrationEmail,
            BankBranch = data.BankBranch,
            ProprietorName = data.ProprietorName,
            ProprietorTp = data.ProprietorTp,
            ProprietorEmail = data.ProprietorEmail,
            ManagerName = data.ManagerName,
            ManagerTp = data.ManagerTp,
            ManagerEmail = data.ManagerEmail,
            ChefName = data.ChefName,
            ChefTp = data.ChefTp,
            ChefEmail = data.ChefEmail,
            PurchasingName = data.PurchasingName,
            PurchasingTp = data.PurchasingTp,
            PurchasingEmail = data.PurchasingEmail,
            AccountantName = data.AccountantName,
            AccountantTp = data.AccountantTp,
            AccountantEmail = data.AccountantEmail,
            RegionId = data.RegionId,
            SubRegionId = data.SubRegionId,
            Province = data.Province,
            Town = data.Town,
            BusinessRegDocPath = businessRegPath,
            BusinessAddressDocPath = businessAddressPath,
            VatDocPath = vatPath,
            PreferredUsername = data.PreferredUsername?.Trim(),
            PreferredPassword = data.PreferredPassword,
            Status = "Pending",
        };

        await _unitOfWork.Repository<CustomerRegistrationRequest>().AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(entity, "");
    }

    public async Task<PagedResult<CustomerRegistrationRequestDto>> GetAllAsync(
        int page, int pageSize, string? status, string baseUrl, CancellationToken ct = default)
    {
        var query = _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
            .Include(r => r.AssignedCoordinator).ThenInclude(c => c!.User)
            .Include(r => r.AssignedRep)
            .Include(r => r.Region)
            .Include(r => r.SubRegion)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<CustomerRegistrationRequestDto>
        {
            Items = items.Select(r => MapToDto(r, baseUrl)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<PagedResult<CustomerRegistrationRequestDto>> GetForCoordinatorAsync(
        Guid coordinatorUserId, int page, int pageSize, string? status, string baseUrl, CancellationToken ct = default)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        if (!coordinator.RegionId.HasValue)
        {
            return new PagedResult<CustomerRegistrationRequestDto>
            {
                Items = [],
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
            };
        }

        var query = _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
            .Include(r => r.AssignedCoordinator).ThenInclude(c => c!.User)
            .Include(r => r.AssignedRep)
            .Include(r => r.Region)
            .Include(r => r.SubRegion)
            .Where(r => r.RegionId == coordinator.RegionId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<CustomerRegistrationRequestDto>
        {
            Items = items.Select(r => MapToDto(r, baseUrl)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<CustomerRegistrationRequestDto> GetByIdAsync(
        Guid id, string baseUrl, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
            .Include(r => r.AssignedCoordinator).ThenInclude(c => c!.User)
            .Include(r => r.AssignedRep)
            .Include(r => r.Region)
            .Include(r => r.SubRegion)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("CustomerRegistrationRequest", id);

        return MapToDto(entity, baseUrl);
    }

    public async Task<CustomerRegistrationRequestDto> ReviewAsync(
        Guid id, ReviewRegistrationRequest request, Guid adminId, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
            .Include(r => r.AssignedCoordinator)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("CustomerRegistrationRequest", id);

        if (entity.Status != "Pending")
            throw new InvalidOperationException("Registration request has already been reviewed.");

        if (request.Action == "Approve")
        {
            if (string.IsNullOrWhiteSpace(request.Password))
                throw new BusinessException("Password is required when approving registration");

            entity.Status = "Approved";
            entity.RegionId = request.RegionId;
            entity.SubRegionId = request.SubRegionId;
            entity.AssignedCoordinatorId = request.AssignedCoordinatorId;
            entity.AssignedRepId = null;

            // Auto-create a CustomerProfile (and User) so the coordinator can see this customer
            await CreateCustomerProfileFromRegistrationAsync(entity, request, request.Password!, ct);
        }
        else if (request.Action == "Reject")
        {
            entity.Status = "Rejected";
            entity.RejectionReason = request.RejectionReason;
        }
        else
        {
            throw new ArgumentException("Action must be 'Approve' or 'Reject'.");
        }

        entity.ReviewNotes = request.ReviewNotes;
        entity.ReviewedByAdminId = adminId;
        entity.ReviewedAt = DateTime.UtcNow;

        // Explicitly mark as modified in case EF Core change tracker didn't detect the status change
        _unitOfWork.Repository<CustomerRegistrationRequest>().Update(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        // Reload coordinator navigation
        if (entity.AssignedCoordinatorId.HasValue)
        {
            entity.AssignedCoordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == entity.AssignedCoordinatorId.Value, ct);
        }

        return MapToDto(entity, "");
    }

    public async Task<CustomerRegistrationRequestDto> ReviewByCoordinatorAsync(
        Guid id, ReviewRegistrationRequest request, Guid coordinatorUserId, string baseUrl, CancellationToken ct = default)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var entity = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
            .Include(r => r.AssignedCoordinator)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("CustomerRegistrationRequest", id);

        if (entity.Status != "Pending")
            throw new InvalidOperationException("Registration request has already been reviewed.");

        if (!coordinator.RegionId.HasValue || !entity.RegionId.HasValue || entity.RegionId.Value != coordinator.RegionId.Value)
            throw new ForbiddenException("You can only review registration requests from your region.");

        if (request.Action == "Approve")
        {
            if (string.IsNullOrWhiteSpace(request.Password))
                throw new BusinessException("Password is required when approving registration");

            request.RegionId = entity.RegionId;
            request.AssignedCoordinatorId = coordinator.Id;

            entity.Status = "Approved";
            entity.RegionId = entity.RegionId ?? request.RegionId;
            entity.SubRegionId = request.SubRegionId;
            entity.AssignedCoordinatorId = coordinator.Id;
            entity.AssignedRepId = null;

            await CreateCustomerProfileFromRegistrationAsync(entity, request, request.Password!, ct);
        }
        else if (request.Action == "Reject")
        {
            entity.Status = "Rejected";
            entity.RejectionReason = request.RejectionReason;
        }
        else
        {
            throw new ArgumentException("Action must be 'Approve' or 'Reject'.");
        }

        entity.ReviewNotes = request.ReviewNotes;
        entity.ReviewedByAdminId = coordinatorUserId;
        entity.ReviewedAt = DateTime.UtcNow;

        _unitOfWork.Repository<CustomerRegistrationRequest>().Update(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        if (entity.AssignedCoordinatorId.HasValue)
        {
            entity.AssignedCoordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == entity.AssignedCoordinatorId.Value, ct);
        }

        return MapToDto(entity, baseUrl);
    }

    public async Task<CustomerRegistrationRequestDto> AdminCreateAsync(
        AdminCreateRegistrationRequest data,
        IFormFile? businessRegDoc,
        IFormFile? businessAddressDoc,
        IFormFile? vatDoc,
        Guid adminId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(data.BusinessRegistrationNumber))
            throw new BusinessException("Business Registration Number is required");
        if (string.IsNullOrWhiteSpace(data.Password))
            throw new BusinessException("Password is required");

        var registrationEmail = string.IsNullOrWhiteSpace(data.Email)
            ? string.Empty
            : data.Email.Trim().ToLowerInvariant();

        var requestId = Guid.NewGuid();
        var module = $"customer-registrations/{requestId}";

        string? businessRegPath = await SaveDocAsync(businessRegDoc, module, ct);
        string? businessAddressPath = await SaveDocAsync(businessAddressDoc, module, ct);
        string? vatPath = await SaveDocAsync(vatDoc, module, ct);

        var entity = new CustomerRegistrationRequest
        {
            Id = requestId,
            CustomerType = data.CustomerType,
            CustomerName = data.CustomerName,
            BusinessRegistrationNumber = data.BusinessRegistrationNumber.Trim(),
            RegisteredAddress = data.RegisteredAddress,
            IncorporateDate = data.IncorporateDate,
            BusinessName = data.BusinessName,
            BusinessLocation = data.BusinessLocation,
            Telephone = data.Telephone,
            Email = registrationEmail,
            BankBranch = data.BankBranch,
            ProprietorName = data.ProprietorName,
            ProprietorTp = data.ProprietorTp,
            ProprietorEmail = data.ProprietorEmail,
            ManagerName = data.ManagerName,
            ManagerTp = data.ManagerTp,
            ManagerEmail = data.ManagerEmail,
            ChefName = data.ChefName,
            ChefTp = data.ChefTp,
            ChefEmail = data.ChefEmail,
            PurchasingName = data.PurchasingName,
            PurchasingTp = data.PurchasingTp,
            PurchasingEmail = data.PurchasingEmail,
            AccountantName = data.AccountantName,
            AccountantTp = data.AccountantTp,
            AccountantEmail = data.AccountantEmail,
            Province = data.Province,
            Town = data.Town,
            BusinessRegDocPath = businessRegPath,
            BusinessAddressDocPath = businessAddressPath,
            VatDocPath = vatPath,
            RegionId = data.RegionId,
            SubRegionId = data.SubRegionId,
            AssignedCoordinatorId = data.AssignedCoordinatorId,
            AssignedRepId = null,
            Status = "Approved",
            ReviewedByAdminId = adminId,
            ReviewedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<CustomerRegistrationRequest>().AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(ct);

        // Auto-create user + customer profile and send credentials email
        var reviewRequest = new ReviewRegistrationRequest
        {
            Action = "Approve",
            Password = data.Password,
            RegionId = data.RegionId,
            SubRegionId = data.SubRegionId,
            AssignedCoordinatorId = data.AssignedCoordinatorId,
            AssignedRepId = null,
        };
        await CreateCustomerProfileFromRegistrationAsync(entity, reviewRequest, data.Password, ct);

        return MapToDto(entity, "");
    }

    public async Task<List<CoordinatorOptionDto>> GetCoordinatorOptionsAsync(CancellationToken ct = default)    {
        return await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .Where(c => c.User.IsActive)
            .Select(c => new CoordinatorOptionDto
            {
                Id = c.Id,
                Name = c.User.Username,
            })
            .ToListAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task SendCredentialsEmailAsync(string toEmail, string username, string tempPassword, string customerName, CancellationToken ct)
    {
        var subject = "Welcome to Janasiri Distributors – Your Account is Ready";
        var body = $"""
            <div style="font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;">
                <div style="background: linear-gradient(135deg, #4f46e5, #7c3aed); padding: 30px; border-radius: 12px 12px 0 0; text-align: center;">
                    <h1 style="color: white; margin: 0; font-size: 24px;">Welcome to Janasiri Distributors</h1>
                </div>
                <div style="background: #ffffff; padding: 30px; border: 1px solid #e5e7eb; border-top: none; border-radius: 0 0 12px 12px;">
                    <p style="color: #374151; font-size: 16px;">Dear <strong>{customerName}</strong>,</p>
                    <p style="color: #6b7280;">Your registration has been <strong style="color: #059669;">approved</strong>! You can now log in to the system using the credentials below:</p>
                    <div style="background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 20px; margin: 20px 0;">
                        <table style="width: 100%;">
                            <tr>
                                <td style="padding: 8px 0; color: #64748b; font-size: 14px;">Username (BR Number)</td>
                                <td style="padding: 8px 0; font-weight: bold; color: #1e293b; font-size: 14px;">{username}</td>
                            </tr>
                            <tr>
                                <td style="padding: 8px 0; color: #64748b; font-size: 14px;">Password</td>
                                <td style="padding: 8px 0; font-weight: bold; color: #4f46e5; font-size: 14px; font-family: monospace;">{tempPassword}</td>
                            </tr>
                        </table>
                    </div>
                    <p style="color: #6b7280; font-size: 14px;">If you have any questions, please contact our support team.</p>
                </div>
            </div>
            """;

        await _emailService.SendEmailAsync(toEmail, subject, body, ct);
    }

    private async Task CreateCustomerProfileFromRegistrationAsync(
        CustomerRegistrationRequest entity, ReviewRegistrationRequest request, string permanentPassword, CancellationToken ct)
    {
        // Use admin-specified username → preferred username → BR number (fallback)
        var username = !string.IsNullOrWhiteSpace(request.Username)
            ? request.Username.Trim()
            : !string.IsNullOrWhiteSpace(entity.PreferredUsername)
                ? entity.PreferredUsername.Trim()
                : (entity.BusinessRegistrationNumber ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username))
            throw new BusinessException("Username is required to create customer account");
        if (string.IsNullOrWhiteSpace(permanentPassword))
            throw new BusinessException("Password is required to approve customer account");
        if (permanentPassword.Length < 6)
            throw new BusinessException("Password must be at least 6 characters");

        // Reuse by email only when it's already a customer account.
        var existingUser = await _unitOfWork.Repository<User>().Query()
            .FirstOrDefaultAsync(u => u.Email == entity.Email, ct);

        var usernameTaken = await _unitOfWork.Repository<User>().AnyAsync(
            u => u.Username == username && (existingUser == null || u.Id != existingUser.Id), ct);
        if (usernameTaken)
            throw new BusinessException($"Username '{username}' is already taken. Please choose a different username.");

        User user;
        if (existingUser != null)
        {
            if (existingUser.Role != UserRole.Customer)
                throw new BusinessException("Email is already used by another user role");

            user = existingUser;
            user.Username = username;
            user.PasswordHash = PasswordHasher.Hash(permanentPassword);
            user.CurrentPassword = permanentPassword;
            user.PhoneNumber = entity.Telephone ?? user.PhoneNumber;
            user.Role = UserRole.Customer;
            user.IsActive = true;
            user.MustChangePassword = false;
            user.TokenVersion += 1;
            user.PasswordChangedAt = DateTime.UtcNow;
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;

            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        else
        {
            var normalizedEmail = (entity.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                normalizedEmail = $"{username.ToLowerInvariant()}@customer.local";

            user = new User
            {
                Username = username,
                Email = normalizedEmail,
                PasswordHash = PasswordHasher.Hash(permanentPassword),
                CurrentPassword = permanentPassword,
                PhoneNumber = entity.Telephone ?? string.Empty,
                Role = UserRole.Customer,
                IsActive = true,
                MustChangePassword = false,
                TokenVersion = 0,
                PasswordChangedAt = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<User>().AddAsync(user, ct);
            // Save first so User.Id is generated before creating CustomerProfile
            await _unitOfWork.SaveChangesAsync(ct);

            if (!string.IsNullOrWhiteSpace(entity.Email))
                await SendCredentialsEmailAsync(entity.Email, username, permanentPassword, entity.CustomerName, ct);
        }

        // Skip if this user already has a CustomerProfile
        var existingProfile = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == user.Id, ct);
        if (existingProfile != null)
        {
            if (request.AssignedCoordinatorId.HasValue)
                existingProfile.AssignedCoordinatorId = request.AssignedCoordinatorId;
            existingProfile.BusinessRegistrationNumber = entity.BusinessRegistrationNumber ?? username;
            _unitOfWork.Repository<CustomerProfile>().Update(existingProfile);
            await _unitOfWork.SaveChangesAsync(ct);
            return;
        }

        var profile = new CustomerProfile
        {
            UserId = user.Id,
            ShopName = entity.BusinessName ?? entity.CustomerName,
            BusinessRegistrationNumber = entity.BusinessRegistrationNumber ?? username,
            RegionId = request.RegionId,
            SubRegionId = request.SubRegionId,
            AssignedCoordinatorId = request.AssignedCoordinatorId,
            AssignedRepId = null,
            ApprovalStatus = CustomerApprovalStatus.Approved,
            Address = new Address
            {
                Street = entity.BusinessLocation ?? entity.RegisteredAddress ?? "",
                City = entity.Town ?? "",
                State = entity.Province ?? "",
                PostalCode = "",
            },
        };
        await _unitOfWork.Repository<CustomerProfile>().AddAsync(profile, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Saves a KYC document as a Private storage-service file and returns the logical storage key
    /// to persist on the entity — never a physical path.
    /// </summary>
    private async Task<string?> SaveDocAsync(IFormFile? file, string module, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return null;

        var result = await _fileStorage.SaveAsync(
            file, module, FileAccessCategory.Private,
            AllowedDocExtensions, AllowedDocContentTypes, MaxDocSizeBytes, ct);

        return result.StorageKey;
    }

    /// <summary>Resolves which stored document (if any) corresponds to a docType route value, for the authenticated download endpoint.</summary>
    public async Task<(string StorageKey, string DownloadName)?> GetDocumentReferenceAsync(Guid id, string docType, CancellationToken ct = default)
    {
        var entity = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("CustomerRegistrationRequest", id);

        var (path, label) = docType.Trim().ToLowerInvariant() switch
        {
            "business-reg" => (entity.BusinessRegDocPath, "business-registration"),
            "business-address" => (entity.BusinessAddressDocPath, "business-address-proof"),
            "vat" => (entity.VatDocPath, "vat-document"),
            _ => throw new BusinessException("Unknown document type."),
        };

        return string.IsNullOrEmpty(path) ? null : (path, $"{label}-{id}");
    }

    /// <summary>Coordinator-scoped variant of <see cref="GetByIdAsync"/>, used to authorize document access — throws NotFoundException if the request is outside the coordinator's assigned region.</summary>
    public async Task<CustomerRegistrationRequestDto> GetByIdForCoordinatorAsync(Guid id, Guid coordinatorUserId, string baseUrl, CancellationToken ct = default)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == coordinatorUserId, ct)
            ?? throw new NotFoundException("Coordinator profile", coordinatorUserId);

        var entity = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
            .Include(r => r.AssignedCoordinator).ThenInclude(c => c!.User)
            .Include(r => r.AssignedRep)
            .Include(r => r.Region)
            .Include(r => r.SubRegion)
            .FirstOrDefaultAsync(r => r.Id == id && r.RegionId == coordinator.RegionId, ct)
            ?? throw new NotFoundException("CustomerRegistrationRequest", id);

        return MapToDto(entity, baseUrl);
    }

    private static CustomerRegistrationRequestDto MapToDto(CustomerRegistrationRequest r, string baseUrl)
    {
        // Document fields expose the authenticated download endpoint (not a direct file URL) —
        // private KYC documents must never be reachable as a static/anonymous link.
        static string? ToUrl(string? storageKey, Guid requestId, string docType) =>
            string.IsNullOrEmpty(storageKey) ? null : $"/api/customer-registrations/{requestId}/documents/{docType}";

        return new CustomerRegistrationRequestDto
        {
            Id = r.Id,
            CustomerType = r.CustomerType,
            CustomerName = r.CustomerName,
            BusinessRegistrationNumber = r.BusinessRegistrationNumber,
            RegisteredAddress = r.RegisteredAddress,
            IncorporateDate = r.IncorporateDate,
            BusinessName = r.BusinessName,
            BusinessLocation = r.BusinessLocation,
            Telephone = r.Telephone,
            Email = r.Email,
            BankBranch = r.BankBranch,
            ProprietorName = r.ProprietorName,
            ProprietorTp = r.ProprietorTp,
            ProprietorEmail = r.ProprietorEmail,
            ManagerName = r.ManagerName,
            ManagerTp = r.ManagerTp,
            ManagerEmail = r.ManagerEmail,
            ChefName = r.ChefName,
            ChefTp = r.ChefTp,
            ChefEmail = r.ChefEmail,
            PurchasingName = r.PurchasingName,
            PurchasingTp = r.PurchasingTp,
            PurchasingEmail = r.PurchasingEmail,
            AccountantName = r.AccountantName,
            AccountantTp = r.AccountantTp,
            AccountantEmail = r.AccountantEmail,
            Province = r.Province,
            Town = r.Town,
            BusinessRegDocUrl = ToUrl(r.BusinessRegDocPath, r.Id, "business-reg"),
            BusinessAddressDocUrl = ToUrl(r.BusinessAddressDocPath, r.Id, "business-address"),
            VatDocUrl = ToUrl(r.VatDocPath, r.Id, "vat"),
            PreferredUsername = r.PreferredUsername,
            PreferredPassword = r.PreferredPassword,
            Status = r.Status,
            RejectionReason = r.RejectionReason,
            ReviewNotes = r.ReviewNotes,
            RegionId = r.RegionId,
            RegionName = r.Region?.Name,
            SubRegionId = r.SubRegionId,
            SubRegionName = r.SubRegion?.Name,
            AssignedCoordinatorId = r.AssignedCoordinatorId,
            AssignedCoordinatorName = r.AssignedCoordinator?.User?.Username,
            AssignedRepId = r.AssignedRepId,
            AssignedRepName = r.AssignedRep?.FullName,
            ReviewedAt = r.ReviewedAt,
            CreatedAt = r.CreatedAt,
        };
    }
}
