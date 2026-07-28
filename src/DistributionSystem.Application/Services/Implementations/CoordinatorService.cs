using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Coordinator;
using DistributionSystem.Application.DTOs.Customer;
using DistributionSystem.Application.DTOs.Rep;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using DistributionSystem.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;

namespace DistributionSystem.Application.Services.Implementations;

public class CoordinatorService : ICoordinatorService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;

    public CoordinatorService(IUnitOfWork unitOfWork, INotificationService notificationService, IEmailService emailService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _emailService = emailService;
    }

    // ===== ADMIN: Coordinator CRUD =====

    public async Task<PagedResult<CoordinatorDto>> GetAllCoordinatorsAsync(int page, int pageSize, string? search, CancellationToken ct)
    {
        var query = _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.Region)
            .Include(c => c.RepCoordinators)
            .Include(c => c.AssignedCustomers)
            .Where(c => !c.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.FullName, searchTerm) ||
                EF.Functions.ILike(c.User.Email, searchTerm) ||
                EF.Functions.ILike(c.EmployeeCode, searchTerm));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(c => c.FullName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<CoordinatorDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<CoordinatorDto> GetCoordinatorByIdAsync(Guid id, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.Region)
            .Include(c => c.RepCoordinators)
            .Include(c => c.AssignedCustomers)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Coordinator", id);

        return MapToDto(coordinator);
    }

    public async Task<CoordinatorDto> CreateCoordinatorAsync(CreateCoordinatorRequest request, CancellationToken ct)
    {
        try
        {
            var username = (string.IsNullOrWhiteSpace(request.Username) ? request.EmployeeCode : request.Username) ?? string.Empty;
            username = username.Trim();
            if (string.IsNullOrWhiteSpace(username))
                throw new BusinessException("ID number (username) is required");

            if (await _unitOfWork.Repository<User>().AnyAsync(u => u.Username == username, ct))
                throw new BusinessException("Username already exists");

            var normalizedEmail = string.IsNullOrWhiteSpace(request.Email)
                ? $"{username.ToLowerInvariant()}@coordinator.local"
                : request.Email.Trim().ToLowerInvariant();

            if (await _unitOfWork.Repository<User>().Query().AnyAsync(u => u.Email != null && EF.Functions.ILike(u.Email, normalizedEmail), ct))
                throw new BusinessException("Email already exists");

            if (string.IsNullOrWhiteSpace(request.Password))
                throw new BusinessException("Password is required");
            if (request.Password.Length < 6)
                throw new BusinessException("Password must be at least 6 characters");

            var permanentPassword = request.Password;

            var user = new User
            {
                Username = username,
                Email = normalizedEmail,
                PasswordHash = PasswordHasher.Hash(permanentPassword),
                CurrentPassword = permanentPassword,
                PhoneNumber = request.PhoneNumber ?? string.Empty,
                Role = UserRole.SalesCoordinator,
                IsActive = true,
                MustChangePassword = false,
                TokenVersion = 0,
                PasswordChangedAt = DateTime.UtcNow,
            };

            await _unitOfWork.Repository<User>().AddAsync(user, ct);

            var coordinator = new CoordinatorProfile
            {
                UserId = user.Id,
                FullName = request.FullName,
                EmployeeCode = request.EmployeeCode,
                RegionId = request.RegionId,
                HireDate = request.HireDate == default ? DateTime.UtcNow : request.HireDate
            };

            await _unitOfWork.Repository<CoordinatorProfile>().AddAsync(coordinator, ct);

            try
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                if (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
                {
                    if (pgEx.ConstraintName?.Contains("IX_Users_Email") == true)
                        throw new BusinessException("Email already exists");
                    if (pgEx.ConstraintName?.Contains("IX_Users_Username") == true)
                        throw new BusinessException("Username already exists");
                    if (pgEx.ConstraintName?.Contains("EmployeeCode") == true)
                        throw new BusinessException("Employee code already exists");
                }
                throw;
            }

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                _ = TrySendCredentialsEmailAsync(user.Email, user.Username, permanentPassword, request.FullName, ct);
            }

            return MapToDto(coordinator);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BusinessException($"Failed to create coordinator: {ex.Message}");
        }
    }

    public async Task DeleteCoordinatorAsync(Guid id, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Coordinator", id);

        if (coordinator.User is null)
            throw new NotFoundException("Coordinator user", coordinator.UserId);

        // Soft-delete behavior: disable account while keeping historical relations.
        coordinator.User.IsActive = false;
        _unitOfWork.Repository<CoordinatorProfile>().Update(coordinator);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<CoordinatorDto> UpdateCoordinatorAsync(Guid id, UpdateCoordinatorRequest request, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Coordinator", id);

        if (request.FullName != null) coordinator.FullName = request.FullName;
        if (request.EmployeeCode != null) coordinator.EmployeeCode = request.EmployeeCode;
        if (request.PhoneNumber != null) coordinator.User.PhoneNumber = request.PhoneNumber;
        if (request.Email != null) coordinator.User.Email = request.Email;
        if (request.HireDate.HasValue) coordinator.HireDate = request.HireDate.Value;
        if (request.RegionId.HasValue) coordinator.RegionId = request.RegionId;
        if (request.IsActive.HasValue) coordinator.User.IsActive = request.IsActive.Value;

        _unitOfWork.Repository<CoordinatorProfile>().Update(coordinator);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToDto(coordinator);
    }

    public async Task AssignRepToCoordinatorAsync(Guid coordinatorId, Guid repId, CancellationToken ct)
    {
        var coordinatorExists = await _unitOfWork.Repository<CoordinatorProfile>()
            .AnyAsync(c => c.Id == coordinatorId, ct);
        if (!coordinatorExists)
            throw new NotFoundException("Coordinator", coordinatorId);

        var repExists = await _unitOfWork.Repository<SalesRepProfile>()
            .AnyAsync(r => r.Id == repId, ct);
        if (!repExists)
            throw new NotFoundException("Sales Rep", repId);

        var alreadyAssigned = await _unitOfWork.Repository<RepCoordinator>()
            .AnyAsync(rc => rc.RepId == repId && rc.CoordinatorId == coordinatorId, ct);

        if (!alreadyAssigned)
        {
            await _unitOfWork.Repository<RepCoordinator>()
                .AddAsync(new RepCoordinator { RepId = repId, CoordinatorId = coordinatorId }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
    }

    // ===== COORDINATOR: Own Profile =====

    public async Task<CoordinatorDto> GetMyProfileAsync(Guid userId, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.Region)
            .Include(c => c.RepCoordinators)
            .Include(c => c.AssignedCustomers)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        return MapToDto(coordinator);
    }

    public async Task<CoordinatorDto> UpdateMyProfileAsync(Guid userId, UpdateCoordinatorRequest request, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.Region)
            .Include(c => c.RepCoordinators)
            .Include(c => c.AssignedCustomers)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        // Self-service edit: allow only personal profile fields.
        if (request.FullName != null) coordinator.FullName = request.FullName;
        if (request.PhoneNumber != null) coordinator.User.PhoneNumber = request.PhoneNumber;

        _unitOfWork.Repository<CoordinatorProfile>().Update(coordinator);
        await _unitOfWork.SaveChangesAsync(ct);
        return MapToDto(coordinator);
    }

    public async Task<CoordinatorDashboardDto> GetDashboardAsync(Guid userId, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.RepCoordinators)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var repIds = coordinator.RepCoordinators.Select(rc => rc.RepId).ToList();

        var pendingCustomers = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .Where(c => c.AssignedCoordinatorId == coordinator.Id && c.ApprovalStatus == CustomerApprovalStatus.PendingApproval)
            .ToListAsync(ct);

        var pendingQuotations = await _unitOfWork.Repository<Quotation>().Query()
            .Include(q => q.Customer).ThenInclude(c => c.User)
            .Include(q => q.Rep)
            .Where(q => q.CoordinatorId == coordinator.Id && q.Status == QuotationStatus.Submitted)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var monthOrders = await _unitOfWork.Repository<Order>().Query()
            .Where(o => repIds.Contains(o.RepId!.Value) && o.OrderDate >= monthStart)
            .ToListAsync(ct);

        var regionCustomersCount = await _unitOfWork.Repository<CustomerProfile>().Query()
            .CountAsync(c => c.AssignedCoordinatorId == coordinator.Id, ct);

        return new CoordinatorDashboardDto
        {
            TotalReps = repIds.Count,
            TotalCustomers = regionCustomersCount,
            PendingCustomerApprovals = pendingCustomers.Count,
            PendingQuotations = pendingQuotations.Count,
            TotalSalesThisMonth = monthOrders.Sum(o => o.TotalAmount),
            TotalOrdersThisMonth = monthOrders.Count,
            RecentPendingApprovals = pendingCustomers.Take(5).Select(c => new PendingCustomerApprovalDto
            {
                CustomerId = c.Id,
                ShopName = c.ShopName,
                Email = c.User?.Email,
                PhoneNumber = c.User?.PhoneNumber,
                RepName = c.AssignedRep?.FullName,
                City = c.Address?.City,
                RequestedAt = c.CreatedAt
            }).ToList(),
            RecentPendingQuotations = pendingQuotations.Take(5).Select(q => new PendingQuotationDto
            {
                QuotationId = q.Id,
                QuotationNumber = q.QuotationNumber,
                CustomerName = q.Customer?.User?.Username ?? q.Customer?.ShopName ?? "",
                RepName = q.Rep?.FullName,
                TotalAmount = q.TotalAmount,
                SubmittedAt = q.CreatedAt
            }).ToList()
        };
    }

    // ===== Customer Approval Workflow =====

    public async Task<PagedResult<CustomerDto>> GetPendingCustomerApprovalsAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var query = _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .Where(c => c.AssignedCoordinatorId == coordinator.Id && c.ApprovalStatus == CustomerApprovalStatus.PendingApproval);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<CustomerDto>
        {
            Items = items.Select(MapCustomerToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<CustomerDto> ApproveCustomerAsync(Guid userId, Guid customerId, ApproveCustomerRequest request, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException("Customer", customerId);

        if (customer.ApprovalStatus != CustomerApprovalStatus.PendingApproval)
            throw new BusinessException("Customer is not in pending approval status");

        customer.ApprovalStatus = CustomerApprovalStatus.Approved;
        customer.ApprovedByCoordinatorId = coordinator.Id;
        customer.ApprovedAt = DateTime.UtcNow;
        customer.User.IsActive = true;

        // Direct rep assignment is disabled. Rep visibility is determined by route assignment.
        customer.AssignedRepId = null;

        _unitOfWork.Repository<CustomerProfile>().Update(customer);
        await _unitOfWork.SaveChangesAsync(ct);

        // Notify the customer
        await _notificationService.SendNotificationAsync(
            customer.UserId,
            NotificationType.CustomerApproval,
            "Account Approved",
            "Your account has been approved. You can now log in and start ordering.",
            ct);

        return MapCustomerToDto(customer);
    }

    public async Task RejectCustomerAsync(Guid userId, Guid customerId, RejectCustomerRequest request, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException("Customer", customerId);

        if (customer.ApprovalStatus != CustomerApprovalStatus.PendingApproval)
            throw new BusinessException("Customer is not in pending approval status");

        customer.ApprovalStatus = CustomerApprovalStatus.Rejected;
        customer.ApprovalRejectionReason = request.Reason;
        customer.User.IsActive = false;

        _unitOfWork.Repository<CustomerProfile>().Update(customer);
        await _unitOfWork.SaveChangesAsync(ct);

        // Notify the rep
        if (customer.AssignedRep != null)
        {
            await _notificationService.SendNotificationAsync(
                customer.AssignedRep.UserId,
                NotificationType.CustomerRejection,
                "Customer Rejected",
                $"Customer '{customer.ShopName}' registration was rejected. Reason: {request.Reason}",
                ct);
        }

        // Notify the customer about rejection
        await _notificationService.SendNotificationAsync(
            customer.User.Id,
            NotificationType.CustomerRejection,
            "Registration Rejected",
            $"Your registration for '{customer.ShopName}' was rejected. Reason: {request.Reason}",
            ct);
    }

    // ===== Region & Team =====

    public async Task<List<RepDto>> GetAssignedRepsAsync(Guid userId, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.RepCoordinators).ThenInclude(rc => rc.Rep).ThenInclude(r => r.User)
            .Include(c => c.RepCoordinators).ThenInclude(rc => rc.Rep).ThenInclude(r => r.Regions).ThenInclude(rr => rr.Region)
            .Include(c => c.RepCoordinators).ThenInclude(rc => rc.Rep).ThenInclude(r => r.SubRegions).ThenInclude(rs => rs.SubRegion)
            .Include(c => c.RepCoordinators).ThenInclude(rc => rc.Rep).ThenInclude(r => r.Coordinators)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        return coordinator.RepCoordinators.Select(rc => rc.Rep).Select(r => new RepDto
        {
            Id = r.Id,
            UserId = r.UserId,
            Username = r.User?.Username ?? string.Empty,
            FullName = r.FullName,
            EmployeeCode = r.EmployeeCode,
            HireDate = r.HireDate,
            RegionIds = r.Regions.Select(rr => rr.RegionId).ToList(),
            RegionNames = r.Regions.Select(rr => rr.Region?.Name ?? string.Empty).ToList(),
            SubRegionIds = r.SubRegions.Select(rs => rs.SubRegionId).ToList(),
            SubRegionNames = r.SubRegions.Select(rs => rs.SubRegion?.Name ?? string.Empty).ToList(),
            CoordinatorIds = r.Coordinators.Select(rc2 => rc2.CoordinatorId).ToList(),
            CoordinatorNames = r.Coordinators.Select(rc2 => rc2.Coordinator?.FullName ?? string.Empty).ToList(),
            Email = r.User?.Email,
            PhoneNumber = r.User?.PhoneNumber,
            IsActive = r.User?.IsActive ?? false,
            MustChangePassword = r.User?.MustChangePassword ?? false,
            TemporaryPassword = r.User?.CurrentPassword,
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    public async Task<RepDto> GetAssignedRepByIdAsync(Guid userId, Guid repId, CancellationToken ct)
    {
        var (coordinator, rep) = await ResolveCoordinatorAndOwnedRepAsync(userId, repId, ct);
        _ = coordinator;

        var assignedCustomersCount = await _unitOfWork.Repository<RouteCustomer>().Query()
            .Where(rc => rc.Route.AssignedReps.Any(rr => rr.RepId == rep.Id) && rc.Route.IsActive)
            .Select(rc => rc.CustomerId)
            .Distinct()
            .CountAsync(ct);

        var dto = new RepDto
        {
            Id = rep.Id,
            UserId = rep.UserId,
            Username = rep.User?.Username ?? string.Empty,
            FullName = rep.FullName,
            EmployeeCode = rep.EmployeeCode,
            HireDate = rep.HireDate,
            RegionIds = rep.Regions.Select(rr => rr.RegionId).ToList(),
            RegionNames = rep.Regions.Select(rr => rr.Region?.Name ?? string.Empty).ToList(),
            SubRegionIds = rep.SubRegions.Select(rs => rs.SubRegionId).ToList(),
            SubRegionNames = rep.SubRegions.Select(rs => rs.SubRegion?.Name ?? string.Empty).ToList(),
            CoordinatorIds = rep.Coordinators.Select(rc => rc.CoordinatorId).ToList(),
            CoordinatorNames = rep.Coordinators.Select(rc => rc.Coordinator?.FullName ?? string.Empty).ToList(),
            Email = rep.User?.Email,
            PhoneNumber = rep.User?.PhoneNumber,
            IsActive = rep.User?.IsActive ?? false,
            MustChangePassword = rep.User?.MustChangePassword ?? false,
            TemporaryPassword = rep.User?.CurrentPassword,
            CreatedAt = rep.CreatedAt,
            AssignedCustomersCount = assignedCustomersCount,
        };

        return dto;
    }

    public async Task<RepPerformanceDto> GetRepPerformanceAsync(Guid userId, Guid repId, DateTime from, DateTime to, CancellationToken ct)
    {
        var (_, rep) = await ResolveCoordinatorAndOwnedRepAsync(userId, repId, ct);

        var orders = await _unitOfWork.Repository<Order>().Query()
            .Where(o => o.RepId == rep.Id && o.OrderDate >= from && o.OrderDate <= to && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);

        var payments = await _unitOfWork.Repository<Payment>().Query()
            .Where(p => p.CollectedByRepId == rep.Id && p.PaymentDate >= from && p.PaymentDate <= to)
            .ToListAsync(ct);

        var visits = await _unitOfWork.Repository<Visit>().Query()
            .Where(v => v.RepId == rep.Id && v.PlannedDate >= from && v.PlannedDate <= to)
            .ToListAsync(ct);

        var target = await _unitOfWork.Repository<SalesTarget>().Query()
            .FirstOrDefaultAsync(t => t.RepId == rep.Id && t.StartDate <= DateTime.UtcNow && t.EndDate >= DateTime.UtcNow, ct);

        var totalSales = orders.Sum(o => o.TotalAmount);
        var totalCustomers = await _unitOfWork.Repository<RouteCustomer>().Query()
            .Where(rc => rc.Route.AssignedReps.Any(rr => rr.RepId == rep.Id) && rc.Route.IsActive)
            .Select(rc => rc.CustomerId)
            .Distinct()
            .CountAsync(ct);

        return new RepPerformanceDto
        {
            RepId = rep.Id,
            RepName = rep.FullName,
            TotalSales = totalSales,
            TotalOrders = orders.Count,
            TotalCustomers = totalCustomers,
            CustomersVisited = visits.Count(v => v.Status == VisitStatus.Completed),
            CollectedPayments = payments.Sum(p => p.Amount),
            TargetAmount = target?.TargetAmount ?? 0,
            AchievedAmount = totalSales,
            AchievementPercentage = target != null && target.TargetAmount > 0 ? (totalSales / target.TargetAmount) * 100 : 0,
        };
    }

    public async Task<PagedResult<CustomerDto>> GetRepCustomersAsync(Guid userId, Guid repId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var (coordinator, rep) = await ResolveCoordinatorAndOwnedRepAsync(userId, repId, ct);

        var routeCustomerIds = await _unitOfWork.Repository<RouteCustomer>().Query()
            .Where(rc => rc.Route.AssignedReps.Any(rr => rr.RepId == rep.Id) && rc.Route.IsActive)
            .Select(rc => rc.CustomerId)
            .Distinct()
            .ToListAsync(ct);

        var query = _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .Include(c => c.Region)
            .Include(c => c.SubRegion)
            .Where(c => c.AssignedCoordinatorId == coordinator.Id && routeCustomerIds.Contains(c.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.ShopName, searchTerm) ||
                EF.Functions.ILike(c.User.Email, searchTerm));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(c => c.ShopName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<CustomerDto>
        {
            Items = items.Select(MapCustomerToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<List<RouteDto>> GetRepRoutesAsync(Guid userId, Guid repId, CancellationToken ct)
    {
        var (_, rep) = await ResolveCoordinatorAndOwnedRepAsync(userId, repId, ct);

        var routes = await _unitOfWork.Repository<Route>().Query()
            .Include(r => r.AssignedReps).ThenInclude(rr => rr.Rep)
            .Include(r => r.RouteCustomers).ThenInclude(rc => rc.Customer).ThenInclude(c => c.User)
            .Where(r => r.AssignedReps.Any(rr => rr.RepId == rep.Id) && r.IsActive)
            .ToListAsync(ct);

        return routes.Select(MapRouteToDto).ToList();
    }

    public async Task<List<RouteDto>> GetRoutesAsync(Guid userId, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.RepCoordinators)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var repIds = coordinator.RepCoordinators.Select(rc => rc.RepId).ToList();
        var coordinatorIdText = coordinator.Id.ToString();

        var routes = await _unitOfWork.Repository<Route>().Query()
            .Include(r => r.AssignedReps).ThenInclude(rr => rr.Rep)
            .Include(r => r.RouteCustomers).ThenInclude(rc => rc.Customer).ThenInclude(c => c.User)
            .Where(r => r.IsActive && (
                (r.AssignedReps.Any(rr => repIds.Contains(rr.RepId))) ||
                (!r.AssignedReps.Any() && r.CreatedBy == coordinatorIdText)
            ))
            .ToListAsync(ct);

        return routes.Select(MapRouteToDto).OrderBy(r => r.Name).ToList();
    }

    public async Task<RouteDto> CreateRouteAsync(Guid userId, CreateRouteRequest request, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.RepCoordinators)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        if (request.RepId.HasValue && !coordinator.RepCoordinators.Any(rc => rc.RepId == request.RepId.Value))
            throw new ForbiddenException("You can only assign routes to reps assigned to you.");

        var route = new Route
        {
            Name = request.Name,
            Description = request.Description,
            DaysOfWeek = string.IsNullOrWhiteSpace(request.DaysOfWeek) ? "[]" : request.DaysOfWeek,
            EstimatedDurationMinutes = request.EstimatedDurationMinutes,
            IsActive = true,
            CreatedBy = coordinator.Id.ToString(),
        };

        await _unitOfWork.Repository<Route>().AddAsync(route, ct);

        if (request.RepId.HasValue)
        {
            await _unitOfWork.Repository<RepRoute>().AddAsync(new RepRoute
            {
                RouteId = route.Id,
                RepId = request.RepId.Value,
                CreatedBy = coordinator.Id.ToString(),
            }, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        if (request.RepId.HasValue)
        {
            await NotifyRepRouteAssignmentAsync(
                request.RepId.Value,
                route.Name,
                NotificationType.RouteCreated,
                $"You have been assigned to route '{route.Name}' by coordinator {coordinator.FullName}.",
                ct);
        }

        return await GetRouteByIdAsync(route.Id, ct);
    }

    public async Task AssignRouteAsync(Guid userId, Guid routeId, Guid repId, CancellationToken ct)
    {
        var (_, rep) = await ResolveCoordinatorAndOwnedRepAsync(userId, repId, ct);
        var (coordinator, route) = await ResolveCoordinatorAndOwnedRouteAsync(userId, routeId, ct);

        var existing = await _unitOfWork.Repository<RepRoute>().AnyAsync(rr => rr.RouteId == route.Id && rr.RepId == rep.Id, ct);
        if (!existing)
        {
            await _unitOfWork.Repository<RepRoute>().AddAsync(new RepRoute
            {
                RouteId = route.Id,
                RepId = rep.Id,
                CreatedBy = coordinator.Id.ToString(),
            }, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        await NotifyRepRouteAssignmentAsync(
            rep.Id,
            route.Name,
            NotificationType.RouteAssigned,
            $"You have been assigned to route '{route.Name}' by coordinator {coordinator.FullName}.",
            ct);
    }

    public async Task<RouteDto> CreateRouteForRepAsync(Guid userId, Guid repId, CreateRouteRequest request, CancellationToken ct)
    {
        var (_, rep) = await ResolveCoordinatorAndOwnedRepAsync(userId, repId, ct);

        var route = new Route
        {
            Name = request.Name,
            Description = request.Description,
            DaysOfWeek = string.IsNullOrWhiteSpace(request.DaysOfWeek) ? "[]" : request.DaysOfWeek,
            EstimatedDurationMinutes = request.EstimatedDurationMinutes,
            IsActive = true,
        };

        await _unitOfWork.Repository<Route>().AddAsync(route, ct);
        await _unitOfWork.Repository<RepRoute>().AddAsync(new RepRoute
        {
            RouteId = route.Id,
            RepId = rep.Id,
            CreatedBy = userId.ToString(),
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await GetRouteByIdAsync(route.Id, ct);
    }

    public async Task AddCustomerToRouteAsync(Guid userId, Guid routeId, AddRouteCustomerRequest request, CancellationToken ct)
    {
        var (coordinator, route) = await ResolveCoordinatorAndOwnedRouteAsync(userId, routeId, ct);

        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var isOwnedByCoordinator = customer.AssignedCoordinatorId == coordinator.Id;
        var isLegacyRegionCustomer = !customer.AssignedCoordinatorId.HasValue
            && coordinator.RegionId.HasValue
            && customer.RegionId.HasValue
            && customer.RegionId.Value == coordinator.RegionId.Value;

        if (!isOwnedByCoordinator && !isLegacyRegionCustomer)
            throw new BusinessException("You can only add customers assigned to your team or your own region.");

        if (customer.ApprovalStatus != CustomerApprovalStatus.Approved)
            throw new BusinessException("Only approved customers can be added to routes.");

        var exists = await _unitOfWork.Repository<RouteCustomer>().AnyAsync(
            rc => rc.RouteId == routeId && rc.CustomerId == request.CustomerId, ct);
        if (exists)
            throw new BusinessException("Customer is already in this route.");

        await _unitOfWork.Repository<RouteCustomer>().AddAsync(new RouteCustomer
        {
            RouteId = routeId,
            CustomerId = request.CustomerId,
            VisitOrder = request.VisitOrder,
            VisitFrequency = string.IsNullOrWhiteSpace(request.VisitFrequency) ? "Weekly" : request.VisitFrequency,
        }, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        var repIds = route.AssignedReps.Select(rr => rr.RepId).Distinct().ToList();
        await NotifyRouteRepsAsync(
            repIds,
            "Route Updated",
            NotificationType.RouteUpdated,
            $"Customer '{customer.ShopName}' was added to route '{route.Name}' by coordinator {coordinator.FullName}.",
            ct);
        await NotifyCustomerRouteMembershipAsync(
            customer.Id,
            "Route Assignment Updated",
            NotificationType.RouteUpdated,
            $"Your shop '{customer.ShopName}' was added to route '{route.Name}'.",
            ct);
    }

    public async Task RemoveCustomerFromRouteAsync(Guid userId, Guid routeId, Guid customerId, CancellationToken ct)
    {
        var (coordinator, route) = await ResolveCoordinatorAndOwnedRouteAsync(userId, routeId, ct);

        var routeCustomer = await _unitOfWork.Repository<RouteCustomer>().Query()
            .Include(rc => rc.Customer).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(rc => rc.RouteId == routeId && rc.CustomerId == customerId, ct)
            ?? throw new NotFoundException("RouteCustomer", customerId);

        _unitOfWork.Repository<RouteCustomer>().Remove(routeCustomer);
        await _unitOfWork.SaveChangesAsync(ct);

        var customerName = routeCustomer.Customer?.ShopName ?? "Customer";
        var repIds = route.AssignedReps.Select(rr => rr.RepId).Distinct().ToList();
        await NotifyRouteRepsAsync(
            repIds,
            "Route Updated",
            NotificationType.RouteUpdated,
            $"Customer '{customerName}' was removed from route '{route.Name}' by coordinator {coordinator.FullName}.",
            ct);
        await NotifyCustomerRouteMembershipAsync(
            routeCustomer.CustomerId,
            "Route Assignment Updated",
            NotificationType.RouteUpdated,
            $"Your shop '{customerName}' was removed from route '{route.Name}'.",
            ct);
    }

    public async Task DeleteRouteAsync(Guid userId, Guid routeId, CancellationToken ct)
    {
        var (_, route) = await ResolveCoordinatorAndOwnedRouteAsync(userId, routeId, ct);

        route.IsActive = false;
        _unitOfWork.Repository<Route>().Update(route);
        await _unitOfWork.SaveChangesAsync(ct);

        var repIds = route.AssignedReps.Select(rr => rr.RepId).Distinct().ToList();
        await NotifyRouteRepsAsync(
            repIds,
            "Route Deactivated",
            NotificationType.RouteDeleted,
            $"Route '{route.Name}' has been deactivated and is no longer available for visits.",
            ct);
    }

    public async Task<PagedResult<CustomerDto>> GetRegionCustomersAsync(Guid userId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var query = _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.AssignedRep)
            .Where(c => c.AssignedCoordinatorId == coordinator.Id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.ShopName, searchTerm) ||
                EF.Functions.ILike(c.User.Email, searchTerm));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(c => c.ShopName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<CustomerDto>
        {
            Items = items.Select(MapCustomerToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    // ===== Mappers =====

    public async Task SoftDeleteAsync(Guid id, string deletedBy, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct)
            ?? throw new NotFoundException("Coordinator", id);

        coordinator.IsDeleted = true;
        coordinator.DeletedAt = DateTime.UtcNow;
        coordinator.DeletedBy = deletedBy;
        coordinator.User.IsActive = false;
        _unitOfWork.Repository<CoordinatorProfile>().Update(coordinator);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted, ct)
            ?? throw new NotFoundException("Coordinator", id);

        coordinator.IsDeleted = false;
        coordinator.DeletedAt = null;
        coordinator.DeletedBy = null;
        coordinator.User.IsActive = true;
        _unitOfWork.Repository<CoordinatorProfile>().Update(coordinator);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<CoordinatorDto>> GetTrashedAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .Include(c => c.Region)
            .Where(c => c.IsDeleted)
            .OrderByDescending(c => c.DeletedAt);

        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<CoordinatorDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task PurgeOldTrashedAsync(int olderThanDays, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        var toDelete = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.User)
            .Where(c => c.IsDeleted && c.DeletedAt < cutoff)
            .ToListAsync(ct);

        foreach (var coordinator in toDelete)
            _unitOfWork.Repository<CoordinatorProfile>().Remove(coordinator);

        if (toDelete.Any())
            await _unitOfWork.SaveChangesAsync(ct);
    }

    private static CoordinatorDto MapToDto(CoordinatorProfile c) => new()
    {
        Id = c.Id,
        UserId = c.UserId,
        Username = c.User?.Username ?? string.Empty,
        FullName = c.FullName,
        EmployeeCode = c.EmployeeCode,
        RegionId = c.RegionId,
        RegionName = c.Region?.Name,
        HireDate = c.HireDate,
        Email = c.User?.Email,
        PhoneNumber = c.User?.PhoneNumber,
        IsActive = c.User?.IsActive ?? false,
        MustChangePassword = c.User?.MustChangePassword ?? false,
        TemporaryPassword = c.User?.CurrentPassword,
        CreatedAt = c.CreatedAt,
        AssignedRepsCount = c.RepCoordinators?.Count ?? 0,
        AssignedCustomersCount = c.AssignedCustomers?.Count ?? 0
    };

    private async Task SendCredentialsEmailAsync(string toEmail, string username, string password, string fullName, CancellationToken ct)
    {
        var subject = "Your Coordinator Login Details";
        var displayName = string.IsNullOrWhiteSpace(fullName) ? "Coordinator" : fullName;
        var body = $@"
                        <div style='font-family: system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif;'>
                            <h2 style='color: #0f172a;'>Welcome, {displayName}!</h2>
                            <p style='color: #334155;'>Your coordinator account has been created. Use the credentials below to log in.</p>
                            <table style='width:100%; border-collapse: collapse; margin-top: 16px;'>
                                <tr>
                                    <td style='padding: 8px; font-weight: 600; color: #334155;'>Username (ID Number)</td>
                                    <td style='padding: 8px; color: #0f172a; font-weight: 600;'>{username}</td>
                                </tr>
                                <tr>
                                    <td style='padding: 8px; font-weight: 600; color: #334155;'>Password</td>
                                    <td style='padding: 8px; color: #0f172a; font-weight: 600; font-family: monospace;'>{password}</td>
                                </tr>
                            </table>
                        </div>
                ";

        await _emailService.SendEmailAsync(toEmail, subject, body, ct);
    }

    private async Task TrySendCredentialsEmailAsync(string toEmail, string username, string password, string fullName, CancellationToken ct)
    {
        try
        {
            await SendCredentialsEmailAsync(toEmail, username, password, fullName, ct);
        }
        catch
        {
            // Do not fail coordinator creation if email delivery fails.
        }
    }

    private static CustomerDto MapCustomerToDto(CustomerProfile c) => new()
    {
        Id = c.Id,
        UserId = c.UserId,
        Username = c.User?.Username ?? string.Empty,
        ShopName = c.ShopName,
        BusinessRegistrationNumber = c.BusinessRegistrationNumber,
        IsActive = c.User?.IsActive ?? false,
        Email = c.User?.Email,
        MustChangePassword = c.User?.MustChangePassword ?? false,
        TemporaryPassword = c.User?.CurrentPassword,
        PhoneNumber = c.User?.PhoneNumber,
        Street = c.Address?.Street,
        City = c.Address?.City,
        State = c.Address?.State,
        Latitude = c.Location?.Latitude,
        Longitude = c.Location?.Longitude,
        RegionId = c.RegionId,
        RegionName = c.Region?.Name,
        SubRegionId = c.SubRegionId,
        SubRegionName = c.SubRegion?.Name,
        AssignedRepId = c.AssignedRepId,
        AssignedRepName = c.AssignedRep?.FullName,
        CreatedAt = c.CreatedAt
    };

    private async Task<(CoordinatorProfile Coordinator, SalesRepProfile Rep)> ResolveCoordinatorAndOwnedRepAsync(Guid userId, Guid repId, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Include(r => r.Regions).ThenInclude(rr => rr.Region)
            .Include(r => r.SubRegions).ThenInclude(rs => rs.SubRegion)
            .Include(r => r.Coordinators)
            .FirstOrDefaultAsync(r => r.Id == repId, ct)
            ?? throw new NotFoundException("SalesRep", repId);

        if (!rep.Coordinators.Any(rc => rc.CoordinatorId == coordinator.Id))
            throw new ForbiddenException("You can only access reps assigned to you.");

        return (coordinator, rep);
    }

    private async Task<(CoordinatorProfile Coordinator, Route Route)> ResolveCoordinatorAndOwnedRouteAsync(Guid userId, Guid routeId, CancellationToken ct)
    {
        var coordinator = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Include(c => c.RepCoordinators)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new NotFoundException("Coordinator profile", userId);

        var route = await _unitOfWork.Repository<Route>().Query()
            .Include(r => r.AssignedReps).ThenInclude(rr => rr.Rep).ThenInclude(rep => rep.User)
            .Include(r => r.AssignedReps).ThenInclude(rr => rr.Rep).ThenInclude(rep => rep.Coordinators)
            .Include(r => r.RouteCustomers).ThenInclude(rc => rc.Customer).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(r => r.Id == routeId, ct)
            ?? throw new NotFoundException("Route", routeId);

        var coordinatorRepIds = coordinator.RepCoordinators.Select(rc => rc.RepId).ToHashSet();
        var isOwnedByAssignedRep = route.AssignedReps.Any(rr => coordinatorRepIds.Contains(rr.RepId));
        var isOwnedUnassignedRoute = !route.AssignedReps.Any() && route.CreatedBy == coordinator.Id.ToString();
        if (!isOwnedByAssignedRep && !isOwnedUnassignedRoute)
            throw new ForbiddenException("You can only manage routes for reps assigned to you.");

        return (coordinator, route);
    }

    private async Task NotifyRepRouteAssignmentAsync(Guid repId, string routeName, NotificationType type, string message, CancellationToken ct)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == repId, ct);

        if (rep?.User == null)
            return;

        await _notificationService.SendNotificationAsync(rep.User.Id, type, "Route Assignment", message, ct);

        if (!string.IsNullOrWhiteSpace(rep.User.Email))
        {
            var emailBody = $"<h3>Route Assignment</h3><p>Hi {rep.FullName},</p><p>{message}</p>";
            _ = _emailService.SendEmailAsync(rep.User.Email, $"Route Assignment: {routeName}", emailBody, ct);
        }
    }

    private async Task NotifyRouteRepsAsync(List<Guid> repIds, string title, NotificationType type, string message, CancellationToken ct)
    {
        if (repIds.Count == 0)
            return;

        var reps = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Where(r => repIds.Contains(r.Id))
            .ToListAsync(ct);

        foreach (var rep in reps)
        {
            if (rep.User == null)
                continue;

            await _notificationService.SendNotificationAsync(rep.User.Id, type, title, message, ct);

            if (!string.IsNullOrWhiteSpace(rep.User.Email))
            {
                var emailBody = $"<h3>{title}</h3><p>Hi {rep.FullName},</p><p>{message}</p>";
                _ = _emailService.SendEmailAsync(rep.User.Email, title, emailBody, ct);
            }
        }
    }

    private async Task NotifyCustomerRouteMembershipAsync(Guid customerId, string title, NotificationType type, string message, CancellationToken ct)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

        if (customer?.User == null)
            return;

        await _notificationService.SendNotificationAsync(customer.User.Id, type, title, message, ct);

        if (!string.IsNullOrWhiteSpace(customer.User.Email))
        {
            var emailBody = $"<h3>{title}</h3><p>Hi {customer.ShopName},</p><p>{message}</p>";
            _ = _emailService.SendEmailAsync(customer.User.Email, title, emailBody, ct);
        }
    }

    private async Task<RouteDto> GetRouteByIdAsync(Guid routeId, CancellationToken ct)
    {
        var route = await _unitOfWork.Repository<Route>().Query()
            .Include(r => r.AssignedReps).ThenInclude(rr => rr.Rep)
            .Include(r => r.RouteCustomers).ThenInclude(rc => rc.Customer).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(r => r.Id == routeId, ct)
            ?? throw new NotFoundException("Route", routeId);

        return MapRouteToDto(route);
    }

    private static RouteDto MapRouteToDto(Route r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description,
        RepId = r.AssignedReps.OrderBy(rr => rr.CreatedAt).Select(rr => (Guid?)rr.RepId).FirstOrDefault(),
        RepName = r.AssignedReps.OrderBy(rr => rr.CreatedAt).Select(rr => rr.Rep.FullName).FirstOrDefault(),
        AssignedReps = r.AssignedReps.OrderBy(rr => rr.CreatedAt).Select(rr => new RouteRepDto
        {
            RepId = rr.RepId,
            RepName = rr.Rep.FullName,
        }).ToList(),
        DaysOfWeek = ParseDaysOfWeek(r.DaysOfWeek),
        EstimatedDurationMinutes = r.EstimatedDurationMinutes,
        IsActive = r.IsActive,
        Customers = r.RouteCustomers?.Select(rc => new RouteCustomerDto
        {
            CustomerId = rc.CustomerId,
            CustomerName = rc.Customer?.User?.Username ?? "",
            ShopName = rc.Customer?.ShopName,
            VisitOrder = rc.VisitOrder,
            VisitFrequency = rc.VisitFrequency,
            Latitude = rc.Customer?.Location?.Latitude,
            Longitude = rc.Customer?.Location?.Longitude,
        }).OrderBy(c => c.VisitOrder).ToList() ?? [],
    };

    private static List<string> ParseDaysOfWeek(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]")
            return [];

        if (raw.TrimStart().StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
            }
            catch
            {
                // Fallback below.
            }
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
