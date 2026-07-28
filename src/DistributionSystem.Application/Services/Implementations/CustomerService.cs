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

public class CustomerService : ICustomerService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly IFileStorageService _fileStorage;

    private static readonly string[] AllowedDocExtensions = [".pdf", ".jpg", ".jpeg", ".png"];
    private static readonly string[] AllowedDocContentTypes = ["application/pdf", "image/jpeg", "image/png"];
    private const long MaxDocSizeBytes = 10 * 1024 * 1024;

    public CustomerService(IUnitOfWork unitOfWork, INotificationService notificationService, IEmailService emailService, IFileStorageService fileStorage)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _emailService = emailService;
        _fileStorage = fileStorage;
    }

    public async Task<CustomerDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Customer", id);

        var dto = MapToDto(customer);
        dto.CustomerType = await ResolveCustomerTypeAsync(customer.User?.Email, customer.BusinessRegistrationNumber, cancellationToken);
        return dto;
    }

    public async Task<CustomerDto> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Customer", userId);

        var dto = MapToDto(customer);
        dto.CustomerType = await ResolveCustomerTypeAsync(customer.User?.Email, customer.BusinessRegistrationNumber, cancellationToken);
        return dto;
    }

    public async Task<PagedResult<CustomerDto>> GetAllAsync(int page, int pageSize, string? search = null, string? sortBy = null, string? sortOrder = "asc",
        bool? isActive = null, Guid? assignedRepId = null, Guid? assignedCoordinatorId = null, Guid? regionId = null, Guid? subRegionId = null, string? customerSegment = null,
        decimal? minCreditLimit = null, decimal? maxCreditLimit = null,
        DateTime? createdFrom = null, DateTime? createdTo = null, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .Include(c => c.AssignedCoordinator)
            .Include(c => c.Region)
            .Include(c => c.SubRegion)
            .AsQueryable();

        // Exclude soft-deleted customers from all normal listings
        query = query.Where(c => !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.ShopName, searchTerm) ||
                EF.Functions.ILike(c.User.Email, searchTerm));
        }

        if (isActive.HasValue)
            query = query.Where(c => c.User.IsActive == isActive.Value);

        if (assignedRepId.HasValue)
            query = query.Where(c => c.AssignedRepId == assignedRepId.Value);
        if (assignedCoordinatorId.HasValue)
            query = query.Where(c => c.AssignedCoordinatorId == assignedCoordinatorId.Value);
        if (regionId.HasValue)
            query = query.Where(c => c.RegionId == regionId.Value);
        if (subRegionId.HasValue)
            query = query.Where(c => c.SubRegionId == subRegionId.Value);

        if (createdFrom.HasValue)
            query = query.Where(c => c.CreatedAt >= createdFrom.Value);
        if (createdTo.HasValue)
            query = query.Where(c => c.CreatedAt <= createdTo.Value);

        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        // Get order counts and totals for all customer IDs in one go
        var customerIds = items.Select(c => c.Id).ToList();
        var orderStats = await _unitOfWork.Repository<Order>().Query()
            .Where(o => customerIds.Contains(o.CustomerId))
            .GroupBy(o => o.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count(), Total = g.Sum(o => o.TotalAmount) })
            .ToListAsync(cancellationToken);
        var statsDict = orderStats.ToDictionary(x => x.CustomerId);

        var customerEmails = items
            .Select(c => c.User?.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!)
            .Distinct()
            .ToList();

        var customerBRNs = items
            .Select(c => c.BusinessRegistrationNumber)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b!.Trim().ToLower())
            .Distinct()
            .ToList();

        var approvedRegistrations = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
            .Where(r => r.Status == "Approved" && (customerEmails.Contains(r.Email) || (r.BusinessRegistrationNumber != null && customerBRNs.Contains(r.BusinessRegistrationNumber.ToLower()))))
            .Select(r => new { r.Email, r.BusinessRegistrationNumber, r.CustomerType, r.ReviewedAt, r.CreatedAt })
            .ToListAsync(cancellationToken);

        var customerTypeByBRN = approvedRegistrations
            .Where(r => !string.IsNullOrWhiteSpace(r.BusinessRegistrationNumber))
            .GroupBy(r => r.BusinessRegistrationNumber!.Trim().ToLower())
            .ToDictionary(
                g => g.Key,
                g => NormalizeCustomerType(g.OrderByDescending(x => x.ReviewedAt ?? x.CreatedAt).First().CustomerType)
            );

        var customerTypeByEmail = approvedRegistrations
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .GroupBy(r => r.Email.Trim().ToLower())
            .ToDictionary(
                g => g.Key,
                g => NormalizeCustomerType(g.OrderByDescending(x => x.ReviewedAt ?? x.CreatedAt).First().CustomerType)
            );

        return new PagedResult<CustomerDto>
        {
            Items = items.Select(c =>
            {
                var dto = MapToDto(c);

                // Prefer BRN-based lookup (most reliable for self-registered customers)
                var brnKey = c.BusinessRegistrationNumber?.Trim().ToLower();
                if (!string.IsNullOrWhiteSpace(brnKey) && customerTypeByBRN.TryGetValue(brnKey, out var customerType))
                {
                    dto.CustomerType = customerType;
                }
                else
                {
                    var emailKey = c.User?.Email?.Trim().ToLower();
                    if (!string.IsNullOrWhiteSpace(emailKey) && customerTypeByEmail.TryGetValue(emailKey, out var emailCustomerType))
                    {
                        dto.CustomerType = emailCustomerType;
                    }
                }

                if (statsDict.TryGetValue(c.Id, out var s))
                {
                    dto.TotalOrders = s.Count;
                    dto.TotalOrderValue = s.Total;
                }

                dto.CustomerType ??= "NonTax";
                return dto;
            }),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<CustomerFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        var reps = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.AssignedRep)
            .Where(c => c.AssignedRepId.HasValue && c.AssignedRep != null)
            .Select(c => new { c.AssignedRepId, c.AssignedRep!.FullName })
            .Distinct()
            .ToListAsync(cancellationToken);

        var coordinators = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .Where(c => c.User.IsActive)
            .Select(c => new { c.Id, c.User.Username })
            .ToListAsync(cancellationToken);

        var regions = await _unitOfWork.Repository<Region>().Query()
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(cancellationToken);

        var subregions = await _unitOfWork.Repository<SubRegion>().Query()
            .Select(s => new { s.Id, s.Name, s.RegionId })
            .ToListAsync(cancellationToken);

        return new CustomerFilterOptionsDto
        {
            AssignedReps = reps.Select(r => new RepOptionDto { Id = r.AssignedRepId!.Value, Name = r.FullName }).ToList(),
            Coordinators = coordinators.Select(c => new CoordinatorOptionDto { Id = c.Id, Name = c.Username }).ToList(),
            Regions = regions.Select(r => new RegionOptionDto { Id = r.Id, Name = r.Name }).ToList(),
            SubRegions = subregions.Select(s => new SubRegionOptionDto { Id = s.Id, Name = s.Name, RegionId = s.RegionId }).ToList(),
        };
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = request.Username.Trim();
        if (await _unitOfWork.Repository<User>().AnyAsync(u => u.Username == normalizedUsername, cancellationToken))
            throw new BusinessException("Username already exists");

        var normalizedEmail = string.IsNullOrWhiteSpace(request.Email)
            ? $"{normalizedUsername.ToLowerInvariant()}@customer.local"
            : request.Email.Trim().ToLowerInvariant();

        if (await _unitOfWork.Repository<User>().Query().AnyAsync(u => u.Email != null && EF.Functions.ILike(u.Email, normalizedEmail), cancellationToken))
            throw new BusinessException("Email already exists");

        // Determine if customer needs approval (only when created by a rep, NOT by admin)
        var needsApproval = request.AssignedRepId.HasValue && !request.IsAdminCreated;

        var user = new User
        {
            Username = normalizedUsername,
            Email = normalizedEmail,
            PasswordHash = PasswordHasher.Hash(request.Password),
            CurrentPassword = request.Password,
            PhoneNumber = request.PhoneNumber,
            Role = UserRole.Customer,
            IsActive = !needsApproval // inactive until approved if created by rep
        };
        await _unitOfWork.Repository<User>().AddAsync(user, cancellationToken);

        // If rep created this customer, try to find the rep's coordinator
        Guid? coordinatorId = request.AssignedCoordinatorId;
        if (coordinatorId == null && request.AssignedRepId.HasValue)
        {
            var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
                .Include(r => r.Coordinators)
                .FirstOrDefaultAsync(r => r.Id == request.AssignedRepId.Value, cancellationToken);
            coordinatorId = rep?.Coordinators.FirstOrDefault()?.CoordinatorId;
        }

        var customer = new CustomerProfile
        {
            UserId = user.Id,
            ShopName = request.ShopName,
            BusinessRegistrationNumber = request.BusinessRegistrationNumber,
            AssignedRepId = request.AssignedRepId,
            AssignedCoordinatorId = coordinatorId,
            RegionId = request.RegionId,
            SubRegionId = request.SubRegionId,
            ApprovalStatus = needsApproval ? CustomerApprovalStatus.PendingApproval : CustomerApprovalStatus.Approved,
            Address = new Address
            {
                Street = request.Street ?? "",
                City = request.City ?? "",
                State = request.State ?? "",
                PostalCode = request.PostalCode ?? ""
            },
            Location = request.Latitude.HasValue ? new Location { Latitude = request.Latitude.Value, Longitude = request.Longitude ?? 0 } : null
        };
        await _unitOfWork.Repository<CustomerProfile>().AddAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Create a skeleton registration record so customerType is stored and admin can edit details later
        var normalizedCustomerType = NormalizeCustomerType(request.CustomerType ?? "NonTax");
        var skeletonReg = new CustomerRegistrationRequest
        {
            CustomerType = normalizedCustomerType,
            CustomerName = request.ShopName,
            BusinessRegistrationNumber = request.BusinessRegistrationNumber,
            Telephone = request.PhoneNumber ?? string.Empty,
            Email = string.IsNullOrWhiteSpace(request.Email)
                ? $"{normalizedUsername.ToLowerInvariant()}@customer.local"
                : request.Email.Trim().ToLowerInvariant(),
            Status = "Approved",
            RegionId = request.RegionId,
            SubRegionId = request.SubRegionId,
            AssignedCoordinatorId = coordinatorId,
            AssignedRepId = request.AssignedRepId,
            ReviewedAt = DateTime.UtcNow,
        };
        await _unitOfWork.Repository<CustomerRegistrationRequest>().AddAsync(skeletonReg, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        customer.User = user;
        return MapToDto(customer);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep).ThenInclude(r => r!.User)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Customer", id);

        var oldAssignedRepId = customer.AssignedRepId;
        var oldAssignedRepUserId = customer.AssignedRep?.UserId;
        var oldAssignedRepEmail = customer.AssignedRep?.User?.Email;
        var oldAssignedRepName = customer.AssignedRep?.FullName;

        if (request.ShopName != null) customer.ShopName = request.ShopName;
        if (request.ClearAssignedRep == true)
            customer.AssignedRepId = null;
        else if (request.AssignedRepId.HasValue)
            customer.AssignedRepId = request.AssignedRepId;

        if (request.ClearAssignedCoordinator == true)
            customer.AssignedCoordinatorId = null;
        else if (request.AssignedCoordinatorId.HasValue)
            customer.AssignedCoordinatorId = request.AssignedCoordinatorId;

        if (request.RegionId.HasValue) customer.RegionId = request.RegionId;
        if (request.SubRegionId.HasValue) customer.SubRegionId = request.SubRegionId;
        customer.Address ??= new Address();
        if (request.Street != null) customer.Address.Street = request.Street;
        if (request.City != null) customer.Address.City = request.City;
        if (request.Latitude.HasValue)
            customer.Location = new Location { Latitude = request.Latitude.Value, Longitude = request.Longitude ?? 0 };

        _unitOfWork.Repository<CustomerProfile>().Update(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Notify the rep(s) if assignment changed
        var newAssignedRepId = request.ClearAssignedRep == true ? (Guid?)null : request.AssignedRepId;
        var assignmentChanged = newAssignedRepId != oldAssignedRepId;

        if (assignmentChanged)
        {
            // Notify previous rep (if any) that the customer was unassigned
            if (oldAssignedRepUserId.HasValue)
            {
                await _notificationService.SendNotificationAsync(
                    oldAssignedRepUserId.Value,
                    NotificationType.CustomerAssignment,
                    "Customer Unassigned",
                    $"Customer '{customer.ShopName}' has been unassigned from your territory.",
                    cancellationToken);

                if (!string.IsNullOrEmpty(oldAssignedRepEmail) && !string.IsNullOrEmpty(oldAssignedRepName))
                {
                    var emailBody = $"<p>Hi {oldAssignedRepName},</p><p>Customer '<strong>{customer.ShopName}</strong>' has been removed from your assigned customers.</p>";
                    _ = _emailService.SendEmailAsync(oldAssignedRepEmail, "Customer Unassigned", emailBody, cancellationToken);
                }
            }

            // Notify new rep about their new assignment
            if (newAssignedRepId.HasValue)
            {
                var newRep = await _unitOfWork.Repository<SalesRepProfile>().Query()
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == newAssignedRepId.Value, cancellationToken);

                if (newRep?.UserId != null)
                {
                    await _notificationService.SendNotificationAsync(
                        newRep.UserId,
                        NotificationType.CustomerAssignment,
                        "New Customer Assigned",
                        $"Customer '{customer.ShopName}' has been assigned to you.",
                        cancellationToken);

                    if (!string.IsNullOrEmpty(newRep.User.Email))
                    {
                        var emailBody = $"<p>Hi {newRep.FullName},</p><p>Customer '<strong>{customer.ShopName}</strong>' has been assigned to you.</p>";
                        _ = _emailService.SendEmailAsync(newRep.User.Email, "New Customer Assigned", emailBody, cancellationToken);
                    }
                }
            }
        }

        return MapToDto(customer);
    }

    public async Task<CustomerSummaryDto> GetSummaryAsync(Guid id, string baseUrl, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.Region)
            .Include(c => c.SubRegion)
            .Include(c => c.AssignedRep)
            .Include(c => c.AssignedCoordinator).ThenInclude(coord => coord!.User)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Customer", id);

        var orders = await _unitOfWork.Repository<Order>().Query()
            .Where(o => o.CustomerId == id)
            .ToListAsync(cancellationToken);

        var frequentProducts = await _unitOfWork.Repository<OrderItem>().Query()
            .Include(oi => oi.Product).Include(oi => oi.Order)
            .Where(oi => oi.Order.CustomerId == id)
            .GroupBy(oi => oi.Product.Name)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);

        // Fetch original registration form details (prefer BR match, fallback to email when real email exists).
        var regRequest = await FindRegistrationRequestForCustomerAsync(customer, cancellationToken);

        return new CustomerSummaryDto
        {
            Customer = MapToDto(customer),
            TotalPurchases = orders.Sum(o => o.TotalAmount),
            TotalOrders = orders.Count,
            LastOrderDate = orders.MaxBy(o => o.OrderDate)?.OrderDate,
            FrequentProducts = frequentProducts,
            RegistrationRequest = regRequest == null ? null : new RegistrationSummaryDto
            {
                CustomerType = regRequest.CustomerType,
                CustomerName = regRequest.CustomerName,
                BusinessRegistrationNumber = regRequest.BusinessRegistrationNumber,
                RegisteredAddress = regRequest.RegisteredAddress,
                IncorporateDate = regRequest.IncorporateDate,
                BusinessName = regRequest.BusinessName,
                BusinessLocation = regRequest.BusinessLocation,
                Telephone = regRequest.Telephone,
                Email = regRequest.Email,
                BankBranch = regRequest.BankBranch,
                Province = regRequest.Province,
                Town = regRequest.Town,
                ProprietorName = regRequest.ProprietorName,
                ProprietorTp = regRequest.ProprietorTp,
                ProprietorEmail = regRequest.ProprietorEmail,
                ManagerName = regRequest.ManagerName,
                ManagerTp = regRequest.ManagerTp,
                ManagerEmail = regRequest.ManagerEmail,
                ChefName = regRequest.ChefName,
                ChefTp = regRequest.ChefTp,
                ChefEmail = regRequest.ChefEmail,
                PurchasingName = regRequest.PurchasingName,
                PurchasingTp = regRequest.PurchasingTp,
                PurchasingEmail = regRequest.PurchasingEmail,
                AccountantName = regRequest.AccountantName,
                AccountantTp = regRequest.AccountantTp,
                AccountantEmail = regRequest.AccountantEmail,
                BusinessRegDocPath = PathToUrl(regRequest.BusinessRegDocPath, regRequest.Id, "business-reg"),
                BusinessAddressDocPath = PathToUrl(regRequest.BusinessAddressDocPath, regRequest.Id, "business-address"),
                VatDocPath = PathToUrl(regRequest.VatDocPath, regRequest.Id, "vat"),
            }
        };
    }

    public async Task<CustomerSummaryDto> UpdateRegistrationDetailsAsync(
        Guid id,
        UpdateCustomerRegistrationDetailsRequest request,
        IFormFile? businessRegDoc,
        IFormFile? businessAddressDoc,
        IFormFile? vatDoc,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Customer", id);

        var regRequest = await FindRegistrationRequestForCustomerAsync(customer, cancellationToken)
            ?? throw new NotFoundException("CustomerRegistrationRequest", id);

        if (request.BusinessRegistrationNumber != null)
        {
            var normalizedBr = request.BusinessRegistrationNumber.Trim();
            if (string.IsNullOrWhiteSpace(normalizedBr))
                throw new BusinessException("Business Registration Number is required");

            if (await _unitOfWork.Repository<User>().AnyAsync(u => u.Id != customer.UserId && u.Username == normalizedBr, cancellationToken))
                throw new BusinessException("Business Registration Number is already used as a username");

            customer.BusinessRegistrationNumber = normalizedBr;
            customer.User.Username = normalizedBr;
            regRequest.BusinessRegistrationNumber = normalizedBr;
        }

        if (request.CustomerType != null)
        {
            var normalizedType = NormalizeCustomerType(request.CustomerType);
            if (normalizedType != "Tax" && normalizedType != "NonTax")
                throw new BusinessException("Customer type must be Tax or NonTax");
            regRequest.CustomerType = normalizedType;
        }

        if (request.CustomerName != null)
        {
            var normalizedCustomerName = request.CustomerName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedCustomerName))
                throw new BusinessException("Customer name is required");
            regRequest.CustomerName = normalizedCustomerName;
        }

        if (request.RegisteredAddress != null)
            regRequest.RegisteredAddress = NullIfWhiteSpace(request.RegisteredAddress);

        regRequest.IncorporateDate = request.IncorporateDate;

        if (request.BusinessName != null)
        {
            regRequest.BusinessName = NullIfWhiteSpace(request.BusinessName);
            if (!string.IsNullOrWhiteSpace(regRequest.BusinessName))
                customer.ShopName = regRequest.BusinessName;
            else if (!string.IsNullOrWhiteSpace(regRequest.CustomerName))
                customer.ShopName = regRequest.CustomerName;
        }

        if (request.BusinessLocation != null)
            regRequest.BusinessLocation = NullIfWhiteSpace(request.BusinessLocation);

        if (request.Telephone != null)
        {
            var normalizedPhone = request.Telephone.Trim();
            if (string.IsNullOrWhiteSpace(normalizedPhone))
                throw new BusinessException("Telephone is required");

            regRequest.Telephone = normalizedPhone;
            customer.User.PhoneNumber = normalizedPhone;
        }

        if (request.Email != null)
        {
            var requestedEmail = request.Email.Trim();
            var normalizedStoredEmail = NormalizeStoredCustomerEmail(requestedEmail, customer.UserId);

            if (!string.Equals(customer.User.Email, normalizedStoredEmail, StringComparison.OrdinalIgnoreCase)
                && await _unitOfWork.Repository<User>().AnyAsync(u => u.Id != customer.UserId && u.Email == normalizedStoredEmail, cancellationToken))
            {
                throw new BusinessException("Email already exists");
            }

            customer.User.Email = normalizedStoredEmail;
            regRequest.Email = string.IsNullOrWhiteSpace(requestedEmail) ? string.Empty : requestedEmail.ToLowerInvariant();
        }

        if (request.BankBranch != null)
            regRequest.BankBranch = NullIfWhiteSpace(request.BankBranch);

        if (request.Province != null)
            regRequest.Province = NullIfWhiteSpace(request.Province);

        if (request.Town != null)
            regRequest.Town = NullIfWhiteSpace(request.Town);

        if (request.ProprietorName != null)
            regRequest.ProprietorName = NullIfWhiteSpace(request.ProprietorName);
        if (request.ProprietorTp != null)
            regRequest.ProprietorTp = NullIfWhiteSpace(request.ProprietorTp);
        if (request.ProprietorEmail != null)
            regRequest.ProprietorEmail = NullIfWhiteSpace(request.ProprietorEmail);

        if (request.ManagerName != null)
            regRequest.ManagerName = NullIfWhiteSpace(request.ManagerName);
        if (request.ManagerTp != null)
            regRequest.ManagerTp = NullIfWhiteSpace(request.ManagerTp);
        if (request.ManagerEmail != null)
            regRequest.ManagerEmail = NullIfWhiteSpace(request.ManagerEmail);

        if (request.ChefName != null)
            regRequest.ChefName = NullIfWhiteSpace(request.ChefName);
        if (request.ChefTp != null)
            regRequest.ChefTp = NullIfWhiteSpace(request.ChefTp);
        if (request.ChefEmail != null)
            regRequest.ChefEmail = NullIfWhiteSpace(request.ChefEmail);

        if (request.PurchasingName != null)
            regRequest.PurchasingName = NullIfWhiteSpace(request.PurchasingName);
        if (request.PurchasingTp != null)
            regRequest.PurchasingTp = NullIfWhiteSpace(request.PurchasingTp);
        if (request.PurchasingEmail != null)
            regRequest.PurchasingEmail = NullIfWhiteSpace(request.PurchasingEmail);

        if (request.AccountantName != null)
            regRequest.AccountantName = NullIfWhiteSpace(request.AccountantName);
        if (request.AccountantTp != null)
            regRequest.AccountantTp = NullIfWhiteSpace(request.AccountantTp);
        if (request.AccountantEmail != null)
            regRequest.AccountantEmail = NullIfWhiteSpace(request.AccountantEmail);

        if (request.RegionId.HasValue)
        {
            regRequest.RegionId = request.RegionId;
            customer.RegionId = request.RegionId;
        }

        if (request.SubRegionId.HasValue)
        {
            regRequest.SubRegionId = request.SubRegionId;
            customer.SubRegionId = request.SubRegionId;
        }

        if (request.AssignedCoordinatorId.HasValue)
        {
            regRequest.AssignedCoordinatorId = request.AssignedCoordinatorId;
            customer.AssignedCoordinatorId = request.AssignedCoordinatorId;
        }

        customer.Address ??= new Address();
        if (request.BusinessLocation != null || request.RegisteredAddress != null)
        {
            var normalizedStreet = NullIfWhiteSpace(request.BusinessLocation) ?? NullIfWhiteSpace(request.RegisteredAddress);
            customer.Address.Street = normalizedStreet ?? string.Empty;
        }

        if (request.Town != null)
            customer.Address.City = NullIfWhiteSpace(request.Town) ?? string.Empty;

        if (request.Province != null)
            customer.Address.State = NullIfWhiteSpace(request.Province) ?? string.Empty;

        var module = $"customer-registrations/{regRequest.Id}";
        var oldBusinessRegPath = regRequest.BusinessRegDocPath;
        var oldBusinessAddressPath = regRequest.BusinessAddressDocPath;
        var oldVatPath = regRequest.VatDocPath;

        var businessRegPath = await SaveDocAsync(businessRegDoc, module, cancellationToken);
        if (businessRegPath != null)
            regRequest.BusinessRegDocPath = businessRegPath;

        var businessAddressPath = await SaveDocAsync(businessAddressDoc, module, cancellationToken);
        if (businessAddressPath != null)
            regRequest.BusinessAddressDocPath = businessAddressPath;

        var vatPath = await SaveDocAsync(vatDoc, module, cancellationToken);
        if (vatPath != null)
            regRequest.VatDocPath = vatPath;

        _unitOfWork.Repository<CustomerProfile>().Update(customer);
        _unitOfWork.Repository<CustomerRegistrationRequest>().Update(regRequest);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Safe replacement: only remove the old file once the new one is saved and committed.
        if (businessRegPath != null) _fileStorage.Delete(oldBusinessRegPath);
        if (businessAddressPath != null) _fileStorage.Delete(oldBusinessAddressPath);
        if (vatPath != null) _fileStorage.Delete(oldVatPath);

        return await GetSummaryAsync(id, baseUrl, cancellationToken);
    }

    public async Task<List<PriceDetailDto>> GetSpecialPricesAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var segment = customerId.ToString();

        // Keep only one active/latest override per product to avoid duplicate-key issues downstream.
        var entries = await _unitOfWork.Repository<PriceList>().Query()
            .Where(p => p.CustomerSegment == segment && p.IsActive)
            .Select(p => new
            {
                ProductId = p.ProductId,
                ProductName = p.Product.Name,
                SpecialPrice = p.SpecialPrice,
                DiscountPercent = p.DiscountPercent,
                StartDate = p.StartDate
            })
            .ToListAsync(cancellationToken);

        return entries
            .GroupBy(p => p.ProductId)
            .Select(g => g.OrderByDescending(x => x.StartDate).First())
            .Select(p => new PriceDetailDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                SpecialPrice = p.SpecialPrice,
                DiscountPercent = p.DiscountPercent
            })
            .ToList();
    }

    public async Task SaveSpecialPricesAsync(Guid customerId, IEnumerable<SpecialPriceUpdateRequest> prices, CancellationToken cancellationToken = default)
    {
        var segment = customerId.ToString();
        var existing = await _unitOfWork.Repository<PriceList>().Query()
            .Where(p => p.CustomerSegment == segment)
            .ToListAsync(cancellationToken);

        foreach (var item in prices)
        {
            var productEntries = existing
                .Where(p => p.ProductId == item.ProductId)
                .OrderByDescending(p => p.StartDate)
                .ToList();

            var entry = productEntries.FirstOrDefault(p => p.IsActive) ?? productEntries.FirstOrDefault();

            var hasSpecialPrice = item.SpecialPrice.HasValue;
            var hasDiscount = item.DiscountPercent.HasValue;

            // If neither special price nor discount is provided, delete existing entry.
            if (!hasSpecialPrice && !hasDiscount)
            {
                foreach (var duplicate in productEntries)
                    _unitOfWork.Repository<PriceList>().Remove(duplicate);
                continue;
            }

            if (entry != null)
            {
                entry.SpecialPrice = item.SpecialPrice;
                entry.DiscountPercent = item.DiscountPercent;
                entry.IsActive = true;
                _unitOfWork.Repository<PriceList>().Update(entry);

                // Remove stale duplicates for this same customer/product pair.
                foreach (var duplicate in productEntries.Where(p => p.Id != entry.Id))
                    _unitOfWork.Repository<PriceList>().Remove(duplicate);
            }
            else
            {
                await _unitOfWork.Repository<PriceList>().AddAsync(new PriceList
                {
                    CustomerSegment = segment,
                    ProductId = item.ProductId,
                    SpecialPrice = item.SpecialPrice,
                    DiscountPercent = item.DiscountPercent,
                    StartDate = DateTime.UtcNow,
                    IsActive = true
                }, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Exposes the authenticated download endpoint (never a direct file URL) — private KYC documents must never be reachable anonymously.</summary>
    private static string? PathToUrl(string? storageKey, Guid requestId, string docType) =>
        string.IsNullOrEmpty(storageKey) ? null : $"/api/customer-registrations/{requestId}/documents/{docType}";

    public async Task<PagedResult<CustomerDto>> GetByRepAsync(Guid repUserId, int page, int pageSize, string? search = null, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        var routeCustomerIds = await _unitOfWork.Repository<RouteCustomer>().Query()
            .Include(rc => rc.Route)
            .Where(rc => rc.Route.IsActive && rc.Route.AssignedReps.Any(rr => rr.RepId == rep.Id))
            .Select(rc => rc.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var query = _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Where(c => routeCustomerIds.Contains(c.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.ShopName, searchTerm) ||
                EF.Functions.ILike(c.User.Email, searchTerm));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(c => c.ShopName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        // --- resolve CustomerType for each item (same pattern as GetAllAsync) ---
        var customerEmails = items
            .Select(c => c.User?.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!)
            .Distinct()
            .ToList();

        var customerBRNs = items
            .Select(c => c.BusinessRegistrationNumber)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b!.Trim().ToLower())
            .Distinct()
            .ToList();

        var approvedRegistrations = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
            .Where(r => r.Status == "Approved" && (customerEmails.Contains(r.Email) || (r.BusinessRegistrationNumber != null && customerBRNs.Contains(r.BusinessRegistrationNumber.ToLower()))))
            .Select(r => new { r.Email, r.BusinessRegistrationNumber, r.CustomerType, r.ReviewedAt, r.CreatedAt })
            .ToListAsync(cancellationToken);

        var customerTypeByBRN = approvedRegistrations
            .Where(r => !string.IsNullOrWhiteSpace(r.BusinessRegistrationNumber))
            .GroupBy(r => r.BusinessRegistrationNumber!.Trim().ToLower())
            .ToDictionary(
                g => g.Key,
                g => NormalizeCustomerType(g.OrderByDescending(x => x.ReviewedAt ?? x.CreatedAt).First().CustomerType)
            );

        var customerTypeByEmail = approvedRegistrations
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .GroupBy(r => r.Email.Trim().ToLower())
            .ToDictionary(
                g => g.Key,
                g => NormalizeCustomerType(g.OrderByDescending(x => x.ReviewedAt ?? x.CreatedAt).First().CustomerType)
            );

        return new PagedResult<CustomerDto>
        {
            Items = items.Select(c =>
            {
                var dto = MapToDto(c);
                var brnKey = c.BusinessRegistrationNumber?.Trim().ToLower();
                if (!string.IsNullOrWhiteSpace(brnKey) && customerTypeByBRN.TryGetValue(brnKey, out var customerType))
                    dto.CustomerType = customerType;
                else
                {
                    var emailKey = c.User?.Email?.Trim().ToLower();
                    if (!string.IsNullOrWhiteSpace(emailKey) && customerTypeByEmail.TryGetValue(emailKey, out var emailCustomerType))
                        dto.CustomerType = emailCustomerType;
                }
                dto.CustomerType ??= "NonTax";
                return dto;
            }),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<CustomerDto> UpdateByUserIdAsync(Guid userId, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Customer", userId);

        if (request.ShopName != null) customer.ShopName = request.ShopName;
        if (request.PhoneNumber != null) customer.User.PhoneNumber = request.PhoneNumber;
        if (request.Street != null) customer.Address.Street = request.Street;
        if (request.City != null) customer.Address.City = request.City;
        if (request.State != null) customer.Address.State = request.State;
        if (request.Latitude.HasValue)
            customer.Location = new Location { Latitude = request.Latitude.Value, Longitude = request.Longitude ?? 0 };

        _unitOfWork.Repository<CustomerProfile>().Update(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(customer);
    }

    public async Task SoftDeleteAsync(Guid id, string deletedBy, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Customer", id);

        customer.IsDeleted = true;
        customer.DeletedAt = DateTime.UtcNow;
        customer.DeletedBy = deletedBy;
        customer.User.IsActive = false;
        _unitOfWork.Repository<CustomerProfile>().Update(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Customer", id);

        customer.IsDeleted = false;
        customer.DeletedAt = null;
        customer.DeletedBy = null;
        customer.User.IsActive = true;
        _unitOfWork.Repository<CustomerProfile>().Update(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<CustomerDto>> GetTrashedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .Include(c => c.AssignedCoordinator)
            .Include(c => c.Region)
            .Include(c => c.SubRegion)
            .Where(c => c.IsDeleted)
            .OrderByDescending(c => c.DeletedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<CustomerDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Customer", id);

        customer.User.IsActive = true;
        _unitOfWork.Repository<CustomerProfile>().Update(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Customer", id);

        customer.User.IsActive = false;
        _unitOfWork.Repository<CustomerProfile>().Update(customer);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeStoredCustomerEmail(string? requestedEmail, Guid userId) =>
        string.IsNullOrWhiteSpace(requestedEmail)
            ? $"no-email-{userId:N}@customer.local"
            : requestedEmail.Trim().ToLowerInvariant();

    /// <summary>Saves a KYC document as a Private storage-service file and returns the logical storage key — never a physical path. Same validation as the initial-submission path (unified; the old admin-update path previously had no validation at all).</summary>
    private async Task<string?> SaveDocAsync(IFormFile? file, string module, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return null;

        var result = await _fileStorage.SaveAsync(
            file, module, FileAccessCategory.Private,
            AllowedDocExtensions, AllowedDocContentTypes, MaxDocSizeBytes, ct);

        return result.StorageKey;
    }

    private static CustomerDto MapToDto(CustomerProfile c) => new()
    {
        Id = c.Id,
        UserId = c.UserId,
        Username = c.User?.Username ?? string.Empty,
        ShopName = c.ShopName,
        BusinessRegistrationNumber = c.BusinessRegistrationNumber,
        IsActive = c.User?.IsActive ?? false,
        Email = ToDisplayEmail(c.User?.Email),
        PhoneNumber = c.User?.PhoneNumber,
        Street = c.Address?.Street,
        City = c.Address?.City,
        State = c.Address?.State,
        Latitude = c.Location?.Latitude,
        Longitude = c.Location?.Longitude,
        MustChangePassword = c.User?.MustChangePassword ?? false,
        TemporaryPassword = c.User?.CurrentPassword,
        RegionId = c.RegionId,
        RegionName = c.Region?.Name,
        SubRegionId = c.SubRegionId,
        SubRegionName = c.SubRegion?.Name,
        AssignedRepId = c.AssignedRepId,
        AssignedRepName = c.AssignedRep?.FullName,
        ApprovalStatus = c.ApprovalStatus.ToString(),
        ApprovalRejectionReason = c.ApprovalRejectionReason,
        AssignedCoordinatorId = c.AssignedCoordinatorId,
        AssignedCoordinatorName = c.AssignedCoordinator?.FullName,
        CreatedAt = c.CreatedAt,
        IsDeleted = c.IsDeleted,
        DeletedAt = c.DeletedAt,
    };

    private static string? ToDisplayEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        if (email.EndsWith("@customer.local", StringComparison.OrdinalIgnoreCase)) return null;
        if (email.StartsWith("no-email-", StringComparison.OrdinalIgnoreCase)) return null;
        return email;
    }

    private static IQueryable<CustomerProfile> ApplySorting(IQueryable<CustomerProfile> query, string? sortBy, string? sortOrder)
    {
        var desc = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        return sortBy?.ToLowerInvariant() switch
        {
            "shopname" => desc ? query.OrderByDescending(c => c.ShopName) : query.OrderBy(c => c.ShopName),
            "createdat" => desc ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            "totalordervalue" => desc ? query.OrderByDescending(c => c.Orders.Sum(o => o.TotalAmount)) : query.OrderBy(c => c.Orders.Sum(o => o.TotalAmount)),
            _ => query.OrderByDescending(c => c.CreatedAt)
        };
    }

    private static string NormalizeCustomerType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "NonTax";
        var n = raw.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
        return n == "tax" ? "Tax" : "NonTax";
    }

    private async Task<string> ResolveCustomerTypeAsync(string? email, string? brn, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(brn))
        {
            var brnLower = brn.Trim().ToLower();
            var regByBrn = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
                .Where(r => r.Status == "Approved" && r.BusinessRegistrationNumber != null && r.BusinessRegistrationNumber.ToLower() == brnLower)
                .OrderByDescending(r => r.ReviewedAt ?? r.CreatedAt)
                .Select(r => r.CustomerType)
                .FirstOrDefaultAsync(cancellationToken);
            if (regByBrn != null) return NormalizeCustomerType(regByBrn);
        }
        if (!string.IsNullOrWhiteSpace(email))
        {
            var emailLower = email.Trim().ToLower();
            var regByEmail = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
                .Where(r => r.Status == "Approved" && r.Email != null && r.Email.ToLower() == emailLower)
                .OrderByDescending(r => r.ReviewedAt ?? r.CreatedAt)
                .Select(r => r.CustomerType)
                .FirstOrDefaultAsync(cancellationToken);
            if (regByEmail != null) return NormalizeCustomerType(regByEmail);
        }
        return "NonTax";
    }

    private async Task<CustomerRegistrationRequest?> FindRegistrationRequestForCustomerAsync(CustomerProfile customer, CancellationToken cancellationToken)
    {
        // Try by BusinessRegistrationNumber first, then by email
        if (!string.IsNullOrWhiteSpace(customer.BusinessRegistrationNumber))
        {
            var brnLower = customer.BusinessRegistrationNumber.Trim().ToLower();
            var reg = await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
                .Where(r => r.BusinessRegistrationNumber != null && r.BusinessRegistrationNumber.ToLower() == brnLower)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (reg != null) return reg;
        }
        var userEmail = customer.User?.Email;
        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            var emailLower = userEmail.Trim().ToLower();
            return await _unitOfWork.Repository<CustomerRegistrationRequest>().Query()
                .Where(r => r.Email != null && r.Email.ToLower() == emailLower)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        return null;
    }

}
