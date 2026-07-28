using DistributionSystem.Application.DTOs.Common;
using DistributionSystem.Application.DTOs.Rep;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Domain.ValueObjects;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using DistributionSystem.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DistributionSystem.Application.Services.Implementations;

public class RepService : IRepService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;

    public RepService(IUnitOfWork unitOfWork, IEmailService emailService, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _notificationService = notificationService;
    }

    public async Task<RepDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Include(r => r.Regions).ThenInclude(rr => rr.Region)
            .Include(r => r.SubRegions).ThenInclude(rs => rs.SubRegion)
            .Include(r => r.Coordinators).ThenInclude(rc => rc.Coordinator)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("SalesRep", id);

        var dto = MapToDto(rep);
        dto.AssignedCustomersCount = await _unitOfWork.Repository<CustomerProfile>().Query()
            .CountAsync(c => c.AssignedRepId == id, cancellationToken);
        return dto;
    }

    public async Task<RepDto> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Include(r => r.Regions).ThenInclude(rr => rr.Region)
            .Include(r => r.SubRegions).ThenInclude(rs => rs.SubRegion)
            .Include(r => r.Coordinators).ThenInclude(rc => rc.Coordinator)
            .FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", userId);
        return MapToDto(rep);
    }

    public async Task<RepDto> UpdateByUserIdAsync(Guid userId, UpdateRepRequest request, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Include(r => r.Regions).ThenInclude(rr => rr.Region)
            .Include(r => r.SubRegions).ThenInclude(rs => rs.SubRegion)
            .Include(r => r.Coordinators).ThenInclude(rc => rc.Coordinator)
            .FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", userId);

        // Self-service edit: allow only personal profile fields.
        if (request.FullName != null) rep.FullName = request.FullName;
        if (request.PhoneNumber != null) rep.User.PhoneNumber = request.PhoneNumber;
        if (request.HireDate.HasValue) rep.HireDate = request.HireDate.Value;

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(rep);
    }

    public async Task<PagedResult<RepDto>> GetAllAsync(int page, int pageSize, string? search = null, Guid? coordinatorId = null, Guid? regionId = null, bool? isActive = null, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Include(r => r.Regions).ThenInclude(rr => rr.Region)
            .Include(r => r.SubRegions).ThenInclude(rs => rs.SubRegion)
            .Include(r => r.Coordinators).ThenInclude(rc => rc.Coordinator)
            .Where(r => !r.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            var pattern = $"%{search}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.FullName, pattern) ||
                EF.Functions.ILike(r.EmployeeCode, pattern) ||
                EF.Functions.ILike(r.User.Username ?? string.Empty, pattern) ||
                EF.Functions.ILike(r.User.Email ?? string.Empty, pattern) ||
                EF.Functions.ILike(r.User.PhoneNumber ?? string.Empty, pattern) ||
                r.Regions.Any(rr => EF.Functions.ILike(rr.Region!.Name, pattern)) ||
                r.SubRegions.Any(rs => EF.Functions.ILike(rs.SubRegion!.Name, pattern)) ||
                r.Coordinators.Any(rc => EF.Functions.ILike(rc.Coordinator!.FullName, pattern)));
        }
        if (coordinatorId.HasValue)
            query = query.Where(r => r.Coordinators.Any(rc => rc.CoordinatorId == coordinatorId.Value));
        if (regionId.HasValue)
            query = query.Where(r => r.Regions.Any(rr => rr.RegionId == regionId.Value));
        if (isActive.HasValue)
            query = query.Where(r => r.User.IsActive == isActive.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(r => r.FullName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var repIds = items.Select(r => r.Id).ToList();
        var customerCounts = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Where(c => c.AssignedRepId.HasValue && repIds.Contains(c.AssignedRepId.Value))
            .GroupBy(c => c.AssignedRepId!.Value)
            .Select(g => new { RepId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var countMap = customerCounts.ToDictionary(x => x.RepId, x => x.Count);

        var dtos = items.Select(r =>
        {
            var dto = MapToDto(r);
            dto.AssignedCustomersCount = countMap.TryGetValue(r.Id, out var c) ? c : 0;
            return dto;
        });

        return new PagedResult<RepDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<RepDto> CreateAsync(CreateRepRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var username = (string.IsNullOrWhiteSpace(request.Username) ? request.EmployeeCode : request.Username) ?? string.Empty;
            username = username.Trim();
            if (string.IsNullOrWhiteSpace(username))
                throw new BusinessException("Employee code (username) is required");

            if (await _unitOfWork.Repository<User>().AnyAsync(u => u.Username == username, cancellationToken))
                throw new BusinessException("Username already exists");

            var normalizedEmail = string.IsNullOrWhiteSpace(request.Email)
                ? $"{username.ToLowerInvariant()}@rep.local"
                : request.Email.Trim().ToLowerInvariant();

            if (await _unitOfWork.Repository<User>().Query().AnyAsync(u => u.Email != null && EF.Functions.ILike(u.Email, normalizedEmail), cancellationToken))
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
                Role = UserRole.SalesRep,
                IsActive = true,
                MustChangePassword = false,
                TokenVersion = 0,
                PasswordChangedAt = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<User>().AddAsync(user, cancellationToken);

            var rep = new SalesRepProfile
            {
                UserId = user.Id,
                FullName = request.FullName,
                EmployeeCode = request.EmployeeCode,
                HireDate = request.HireDate,
            };

            foreach (var rid in request.RegionIds ?? [])
                rep.Regions.Add(new RepRegion { RepId = rep.Id, RegionId = rid });
            foreach (var sid in request.SubRegionIds ?? [])
                rep.SubRegions.Add(new RepSubRegion { RepId = rep.Id, SubRegionId = sid });
            foreach (var cid in request.CoordinatorIds ?? [])
                rep.Coordinators.Add(new RepCoordinator { RepId = rep.Id, CoordinatorId = cid });

            await _unitOfWork.Repository<SalesRepProfile>().AddAsync(rep, cancellationToken);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                if (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
                {
                    if (pgEx.ConstraintName?.Contains("IX_Users_Email") == true)
                        throw new BusinessException("Email already exists");
                    if (pgEx.ConstraintName?.Contains("IX_Users_Username") == true)
                        throw new BusinessException("Username already exists");
                }
                throw;
            }

            // Send credentials email only when email is explicitly provided.
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                _ = SendCredentialsEmailAsync(user.Email, username, permanentPassword, request.FullName, cancellationToken);
            }

            rep.User = user;
            return MapToDto(rep);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BusinessException($"Failed to create sales rep: {ex.Message}");
        }
    }

        private async Task SendCredentialsEmailAsync(string toEmail, string username, string password, string fullName, CancellationToken ct)
    {
        var subject = "Your Sales Rep Login Details";
        var displayName = string.IsNullOrWhiteSpace(fullName) ? "Sales Rep" : fullName;
        var body = $@"
            <div style='font-family: system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif;'>
              <h2 style='color: #0f172a;'>Welcome, {displayName}!</h2>
                            <p style='color: #334155;'>Your account has been created. Use the credentials below to log in.</p>
              <table style='width:100%; border-collapse: collapse; margin-top: 16px;'>
                <tr>
                                    <td style='padding: 8px; font-weight: 600; color: #334155;'>Username (Employee Code)</td>
                                    <td style='padding: 8px; color: #0f172a; font-weight: 600;'>{username}</td>
                </tr>
                <tr>
                                    <td style='padding: 8px; font-weight: 600; color: #334155;'>Password</td>
                                    <td style='padding: 8px; color: #0f172a; font-weight: 600; font-family: monospace;'>{password}</td>
                </tr>
              </table>
              <p style='color: #64748b;'>If you have any questions, please contact your administrator.</p>
            </div>
        ";

        await _emailService.SendEmailAsync(toEmail, subject, body, ct);
    }

    // Username is provided as employee code for rep creation flows.

    public async Task<RepDto> UpdateAsync(Guid id, UpdateRepRequest request, CancellationToken cancellationToken = default)
    {
        // Load rep WITHOUT navigation collections to avoid EF tracking conflicts on junction tables
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("SalesRep", id);

        var changes = new List<string>();

        if (request.FullName != null) rep.FullName = request.FullName;
        if (request.EmployeeCode != null) rep.EmployeeCode = request.EmployeeCode;
        if (request.HireDate.HasValue) rep.HireDate = request.HireDate.Value;
        if (request.PhoneNumber != null) rep.User.PhoneNumber = request.PhoneNumber;
        if (request.Email != null) rep.User.Email = request.Email;

        // Explicitly delete + insert junction records to avoid EF collection tracking issues
        if (request.RegionIds != null)
        {
            var existing = await _unitOfWork.Repository<RepRegion>().Query()
                .Where(rr => rr.RepId == id).ToListAsync(cancellationToken);
            _unitOfWork.Repository<RepRegion>().RemoveRange(existing);
            foreach (var rid in request.RegionIds.Distinct())
                await _unitOfWork.Repository<RepRegion>().AddAsync(
                    new RepRegion { RepId = id, RegionId = rid }, cancellationToken);
            changes.Add("Regions updated");
        }

        if (request.SubRegionIds != null)
        {
            var existing = await _unitOfWork.Repository<RepSubRegion>().Query()
                .Where(rs => rs.RepId == id).ToListAsync(cancellationToken);
            _unitOfWork.Repository<RepSubRegion>().RemoveRange(existing);
            foreach (var sid in request.SubRegionIds.Distinct())
                await _unitOfWork.Repository<RepSubRegion>().AddAsync(
                    new RepSubRegion { RepId = id, SubRegionId = sid }, cancellationToken);
            changes.Add("Sub-Regions updated");
        }

        if (request.CoordinatorIds != null)
        {
            var existing = await _unitOfWork.Repository<RepCoordinator>().Query()
                .Where(rc => rc.RepId == id).ToListAsync(cancellationToken);
            _unitOfWork.Repository<RepCoordinator>().RemoveRange(existing);
            foreach (var cid in request.CoordinatorIds.Distinct())
                await _unitOfWork.Repository<RepCoordinator>().AddAsync(
                    new RepCoordinator { RepId = id, CoordinatorId = cid }, cancellationToken);
            changes.Add("Coordinators updated");
        }

        if (request.IsActive.HasValue)
        {
            rep.User.IsActive = request.IsActive.Value;
            changes.Add(request.IsActive.Value ? "Account activated" : "Account deactivated");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Reload with full includes for return DTO (fresh read after save)
        rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Include(r => r.Regions).ThenInclude(rr => rr.Region)
            .Include(r => r.SubRegions).ThenInclude(rs => rs.SubRegion)
            .Include(r => r.Coordinators).ThenInclude(rc => rc.Coordinator)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException("SalesRep", id);

        // Notify rep about changes via notification + email
        if (changes.Count > 0 && rep.User?.Id != null)
        {
            var message = "The following changes were made to your account: " + string.Join("; ", changes);

            await _notificationService.SendNotificationAsync(
                rep.User.Id,
                NotificationType.General,
                "Account Updated",
                message,
                cancellationToken);

            if (!string.IsNullOrEmpty(rep.User.Email))
            {
                var body = $"<h3>Account Update</h3><p>Hi {rep.FullName},</p><p>The following changes were made to your account:</p><ul>{string.Join("", changes.Select(c => $"<li>{c}</li>"))}</ul><p>If you have questions, please contact your coordinator.</p>";
                _ = _emailService.SendEmailAsync(rep.User.Email, "Account Update Notification", body, cancellationToken);
            }
        }

        return MapToDto(rep);
    }

        public async Task<RepPerformanceDto> GetPerformanceAsync(Guid repId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
                .FirstOrDefaultAsync(r => r.Id == repId, cancellationToken)
                ?? throw new NotFoundException("SalesRep", repId);

            var orders = await _unitOfWork.Repository<Order>().Query()
                .Where(o => o.RepId == repId && o.OrderDate >= from && o.OrderDate <= to && o.Status != OrderStatus.Cancelled)
                .ToListAsync(cancellationToken);

            var payments = await _unitOfWork.Repository<Payment>().Query()
                .Where(p => p.CollectedByRepId == repId && p.PaymentDate >= from && p.PaymentDate <= to)
                .ToListAsync(cancellationToken);

            var visits = await _unitOfWork.Repository<Visit>().Query()
                .Where(v => v.RepId == repId && v.PlannedDate >= from && v.PlannedDate <= to)
                .ToListAsync(cancellationToken);

            var targets = await _unitOfWork.Repository<SalesTarget>().Query()
                .Where(t => t.RepId == repId && t.StartDate <= to && t.EndDate >= from)
                .OrderByDescending(t => t.StartDate)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);

            var totalSales = orders.Sum(o => o.TotalAmount);
            var totalTarget = targets.Sum(t => t.TargetAmount);
            var totalAchieved = targets.Sum(t => t.AchievedAmount);

            var totalCustomers = await _unitOfWork.Repository<CustomerProfile>().Query()
                .CountAsync(c => c.AssignedRepId == repId, cancellationToken);

            return new RepPerformanceDto
            {
                RepId = repId,
                RepName = rep.FullName,
                TotalSales = totalSales,
                TotalOrders = orders.Count,
                TotalCustomers = totalCustomers,
                CustomersVisited = visits.Count(v => v.Status == VisitStatus.Completed),
                CollectedPayments = payments.Sum(p => p.Amount),
                TargetAmount = totalTarget,
                AchievedAmount = totalAchieved,
                AchievementPercentage = totalTarget > 0 ? (totalAchieved / totalTarget) * 100 : 0
            };
        }

        public async Task<RepPerformanceDto> GetPerformanceByUserIdAsync(Guid userId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
                .FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("SalesRep", userId);

            return await GetPerformanceAsync(rep.Id, from, to, cancellationToken);
        }

        public async Task UnassignCoordinatorAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException("SalesRep", id);

            var existing = await _unitOfWork.Repository<RepCoordinator>().Query()
                .Where(rc => rc.RepId == id).ToListAsync(cancellationToken);
            if (!existing.Any())
                return;

            _unitOfWork.Repository<RepCoordinator>().RemoveRange(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (rep.User?.Id != null)
            {
                await _notificationService.SendNotificationAsync(
                    rep.User.Id,
                    NotificationType.General,
                    "Coordinator Unassigned",
                    "You have been unassigned from your coordinator(s).",
                    cancellationToken);
            }
        }

    public async Task<List<VisitDto>> GetTodayVisitsAsync(Guid repUserId, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        // if rep has no active routes, nothing to show
        var routeCusts = await _unitOfWork.Repository<RouteCustomer>().Query()
            .Include(rc => rc.Route)
            .Include(rc => rc.Customer).ThenInclude(c => c.User)
            .Where(rc => rc.Route.IsActive && rc.Route.AssignedReps.Any(rr => rr.RepId == rep.Id))
            .ToListAsync(cancellationToken);
        var today = DateTime.UtcNow.Date;
        if (routeCusts.Count == 0)
        {
            // still return any ad-hoc visits even if no route exists
            var adhocVisits = await _unitOfWork.Repository<Visit>().Query()
                .Include(v => v.Customer).ThenInclude(c => c.User)
                .Include(v => v.Customer).ThenInclude(c => c.Location)
                .Include(v => v.Customer).ThenInclude(c => c.Address)
                .Where(v => v.RepId == rep.Id && v.PlannedDate.Date == today)
                .ToListAsync(cancellationToken);
            return adhocVisits.Select(MapVisitToDto).ToList();
        }
        var allVisits = await _unitOfWork.Repository<Visit>().Query()
            .Include(v => v.Customer).ThenInclude(c => c.User)
            .Include(v => v.Customer).ThenInclude(c => c.Location)
            .Include(v => v.Customer).ThenInclude(c => c.Address)
            .Where(v => v.RepId == rep.Id && v.PlannedDate.Date == today)
            .ToListAsync(cancellationToken);

        // group visits by customer and take the latest entry (by ActualStartTime or PlannedDate)
        var latestByCustomer = allVisits
            .GroupBy(v => v.CustomerId)
            .Select(g => g.OrderByDescending(v => v.ActualStartTime ?? v.PlannedDate).First())
            .ToDictionary(v => v.CustomerId, v => v);

        var result = new List<VisitDto>();
        var routeCustomerIds = routeCusts.Select(rc => rc.CustomerId).ToHashSet();

        foreach (var rc in routeCusts)
        {
            if (latestByCustomer.TryGetValue(rc.CustomerId, out var visit))
            {
                result.Add(MapVisitToDto(visit));
            }
            else
            {
                // no visit record yet, use Planned status
                result.Add(new VisitDto
                {
                    CustomerId = rc.CustomerId,
                    CustomerName = rc.Customer?.User?.Username ?? "",
                    ShopName = rc.Customer?.ShopName,
                    PlannedDate = today,
                    Status = VisitStatus.Planned.ToString(),
                    PhoneNumber = rc.Customer?.User?.PhoneNumber,
                    Street = rc.Customer?.Address?.Street,
                    City = rc.Customer?.Address?.City,
                    State = rc.Customer?.Address?.State,
                    PostalCode = rc.Customer?.Address?.PostalCode,
                    Country = rc.Customer?.Address?.Country,
                    Latitude = rc.Customer?.Location?.Latitude,
                    Longitude = rc.Customer?.Location?.Longitude
                });
            }
        }

        // append ad-hoc visits not on the route
        foreach (var kv in latestByCustomer)
        {
            if (!routeCustomerIds.Contains(kv.Key))
            {
                result.Add(MapVisitToDto(kv.Value));
            }
        }

        return result;
    }

    public async Task<VisitDto> CheckInAsync(Guid repUserId, CheckInRequest request, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        var today = DateTime.UtcNow.Date;
        // try to find an existing scheduled visit for this customer today
        var visit = await _unitOfWork.Repository<Visit>().Query()
            .Where(v => v.RepId == rep.Id && v.CustomerId == request.CustomerId && v.PlannedDate.Date == today)
            .FirstOrDefaultAsync(cancellationToken);

        if (visit == null)
        {
            visit = new Visit
            {
                RepId = rep.Id,
                CustomerId = request.CustomerId,
                PlannedDate = DateTime.UtcNow,
                ActualStartTime = DateTime.UtcNow,
                Status = VisitStatus.CheckedIn,
                CheckInLocation = new Location { Latitude = request.Latitude, Longitude = request.Longitude }
            };
            await _unitOfWork.Repository<Visit>().AddAsync(visit, cancellationToken);
        }
        else
        {
            visit.ActualStartTime = DateTime.UtcNow;
            visit.Status = VisitStatus.CheckedIn;
            visit.CheckInLocation = new Location { Latitude = request.Latitude, Longitude = request.Longitude };
            _unitOfWork.Repository<Visit>().Update(visit);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapVisitToDto(visit);
    }

    public async Task CancelVisitAsync(Guid repUserId, Guid visitId, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        var visit = await _unitOfWork.Repository<Visit>().GetByIdAsync(visitId, cancellationToken)
            ?? throw new NotFoundException("Visit", visitId);

        if (visit.RepId != rep.Id)
            throw new ForbiddenException("Cannot cancel visit for a different rep");

        visit.Status = VisitStatus.Cancelled;
        _unitOfWork.Repository<Visit>().Update(visit);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<VisitDto> CheckOutAsync(Guid visitId, CheckOutRequest request, CancellationToken cancellationToken = default)
    {
        var visit = await _unitOfWork.Repository<Visit>().GetByIdAsync(visitId, cancellationToken)
            ?? throw new NotFoundException("Visit", visitId);

        visit.ActualEndTime = DateTime.UtcNow;
        visit.Status = VisitStatus.Completed;
        visit.Notes = request.Notes;
        visit.OutcomeReason = request.OutcomeReason;
        visit.CheckOutLocation = new Location { Latitude = request.Latitude, Longitude = request.Longitude };

        _unitOfWork.Repository<Visit>().Update(visit);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapVisitToDto(visit);
    }

    // create a single visit for today outside any predefined route
    public async Task<VisitDto> AddAdHocVisitAsync(Guid repUserId, Guid customerId, string? notes = null, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        var today = DateTime.UtcNow.Date;
        if (await _unitOfWork.Repository<Visit>().Query()
            .AnyAsync(v => v.RepId == rep.Id && v.CustomerId == customerId && v.PlannedDate.Date == today, cancellationToken))
        {
            throw new BusinessException("Visit already exists for today");
        }

        var visit = new Visit
        {
            RepId = rep.Id,
            CustomerId = customerId,
            PlannedDate = today,
            Status = VisitStatus.Planned,
            Notes = notes
        };
        await _unitOfWork.Repository<Visit>().AddAsync(visit, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapVisitToDto(visit);
    }

    public async Task GenerateTodayVisitsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var dayName = today.DayOfWeek.ToString();

        var routes = await _unitOfWork.Repository<Route>().Query()
            .Include(r => r.AssignedReps)
            .Include(r => r.RouteCustomers)
            .Where(r => r.IsActive && r.DaysOfWeek.Contains(dayName) && r.AssignedReps.Any())
            .ToListAsync(cancellationToken);

        foreach (var route in routes)
        {
            foreach (var assignedRep in route.AssignedReps)
            {
                foreach (var rc in route.RouteCustomers)
                {
                    bool exists = await _unitOfWork.Repository<Visit>().Query()
                        .AnyAsync(v => v.RepId == assignedRep.RepId && v.CustomerId == rc.CustomerId && v.PlannedDate.Date == today, cancellationToken);
                    if (exists) continue;

                    var visit = new Visit
                    {
                        RepId = assignedRep.RepId,
                        CustomerId = rc.CustomerId,
                        PlannedDate = today,
                        Status = VisitStatus.Planned
                    };
                    await _unitOfWork.Repository<Visit>().AddAsync(visit, cancellationToken);
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<RouteProgressDto>> GetTodayRouteProgressAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var visits = await _unitOfWork.Repository<Visit>().Query()
            .Where(v => v.PlannedDate.Date == today)
            .ToListAsync(cancellationToken);

        var repIds = visits.Select(v => v.RepId).Where(id => id != Guid.Empty).Distinct().ToList();
        var reps = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Where(r => repIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        var result = new List<RouteProgressDto>();
        foreach (var rep in reps)
        {
            var repVisits = visits.Where(v => v.RepId == rep.Id).ToList();
            var total = repVisits.Count;
            var completed = repVisits.Count(v => v.Status == VisitStatus.Completed);
            result.Add(new RouteProgressDto
            {
                RepId = rep.Id,
                RepName = rep.FullName,
                TotalPlanned = total,
                Completed = completed,
                StrikeRate = total > 0 ? (double)completed / total : 0
            });
        }

        return result;
    }

    public async Task<RouteDto> CreateRouteAsync(CreateRouteRequest request, CancellationToken cancellationToken = default)
    {
        var route = new Route
        {
            Name = request.Name,
            Description = request.Description,
            DaysOfWeek = request.DaysOfWeek,
            EstimatedDurationMinutes = request.EstimatedDurationMinutes,
            IsActive = true
        };

        await _unitOfWork.Repository<Route>().AddAsync(route, cancellationToken);

        if (request.RepId.HasValue)
        {
            await _unitOfWork.Repository<RepRoute>().AddAsync(new RepRoute
            {
                RouteId = route.Id,
                RepId = request.RepId.Value,
                CreatedBy = "admin"
            }, cancellationToken);
        }

        if (request.Customers != null)
        {
            foreach (var c in request.Customers)
            {
                await _unitOfWork.Repository<RouteCustomer>().AddAsync(new RouteCustomer
                {
                    RouteId = route.Id,
                    CustomerId = c.CustomerId,
                    VisitOrder = c.VisitOrder,
                    VisitFrequency = c.VisitFrequency
                }, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.RepId.HasValue)
        {
            await NotifyRepAboutRouteAsync(
                request.RepId.Value,
                "Route Assignment",
                NotificationType.RouteCreated,
                $"You have been assigned to route '{route.Name}' by admin.",
                cancellationToken);
        }

        return await GetRouteByIdAsync(route.Id, cancellationToken);
    }

    public async Task AssignRouteAsync(Guid routeId, AssignRouteRequest request, CancellationToken cancellationToken = default)
    {
        var route = await _unitOfWork.Repository<Route>().GetByIdAsync(routeId, cancellationToken)
            ?? throw new NotFoundException("Route", routeId);

        var existing = await _unitOfWork.Repository<RepRoute>().AnyAsync(rr => rr.RouteId == route.Id && rr.RepId == request.RepId, cancellationToken);
        if (!existing)
        {
            await _unitOfWork.Repository<RepRoute>().AddAsync(new RepRoute
            {
                RouteId = route.Id,
                RepId = request.RepId,
                CreatedBy = "admin"
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifyRepAboutRouteAsync(
            request.RepId,
            "Route Assignment",
            NotificationType.RouteAssigned,
            $"You have been assigned to route '{route.Name}' by admin.",
            cancellationToken);
    }

    public async Task AddCustomerToRouteAsync(Guid routeId, AddRouteCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var route = await _unitOfWork.Repository<Route>().GetByIdAsync(routeId, cancellationToken)
            ?? throw new NotFoundException("Route", routeId);

        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var exists = await _unitOfWork.Repository<RouteCustomer>().AnyAsync(
            rc => rc.RouteId == routeId && rc.CustomerId == request.CustomerId,
            cancellationToken);
        if (exists)
            throw new BusinessException("Customer is already assigned to this route", "ROUTE_CUSTOMER_EXISTS");

        var rc = new RouteCustomer
        {
            RouteId = routeId,
            CustomerId = request.CustomerId,
            VisitOrder = request.VisitOrder,
            VisitFrequency = request.VisitFrequency ?? "Weekly"
        };

        await _unitOfWork.Repository<RouteCustomer>().AddAsync(rc, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx
                                           && pgEx.SqlState == "23505"
                                           && pgEx.ConstraintName == "IX_RouteCustomers_RouteId_CustomerId")
        {
            throw new BusinessException("Customer is already assigned to this route", "ROUTE_CUSTOMER_EXISTS");
        }

        await NotifyRouteRepsAsync(
            routeId,
            "Route Updated",
            NotificationType.RouteUpdated,
            $"Customer '{customer.ShopName}' was added to route '{route.Name}' by admin.",
            cancellationToken);
        await NotifyCustomerAboutRouteAsync(
            customer.Id,
            "Route Assignment Updated",
            NotificationType.RouteUpdated,
            $"Your shop '{customer.ShopName}' was added to route '{route.Name}'.",
            cancellationToken);
    }

    public async Task RemoveCustomerFromRouteAsync(Guid routeId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var rc = await _unitOfWork.Repository<RouteCustomer>().Query()
            .Include(x => x.Customer).ThenInclude(c => c.User)
            .Include(x => x.Route)
            .FirstOrDefaultAsync(x => x.RouteId == routeId && x.CustomerId == customerId, cancellationToken)
            ?? throw new NotFoundException("RouteCustomer", $"{routeId}/{customerId}");

        _unitOfWork.Repository<RouteCustomer>().Remove(rc);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifyRouteRepsAsync(
            routeId,
            "Route Updated",
            NotificationType.RouteUpdated,
            $"Customer '{rc.Customer?.ShopName}' was removed from route '{rc.Route?.Name}'.",
            cancellationToken);
    }

    public async Task RemoveRepFromRouteAsync(Guid routeId, Guid repId, CancellationToken cancellationToken = default)
    {
        var rr = await _unitOfWork.Repository<RepRoute>().Query()
            .Include(x => x.Rep).ThenInclude(r => r.User)
            .Include(x => x.Route)
            .FirstOrDefaultAsync(x => x.RouteId == routeId && x.RepId == repId, cancellationToken)
            ?? throw new NotFoundException("RepRoute", $"{routeId}/{repId}");

        _unitOfWork.Repository<RepRoute>().Remove(rr);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (rr.Rep?.User != null)
        {
            await _notificationService.SendNotificationAsync(
                rr.Rep.User.Id,
                NotificationType.RouteUpdated,
                "Route Unassigned",
                $"You have been removed from route '{rr.Route?.Name}'.",
                cancellationToken);
        }
    }

    public async Task<List<RouteDto>> GetAllRoutesAsync(CancellationToken cancellationToken = default)
    {
        var routes = await _unitOfWork.Repository<Route>().Query()
            .Include(r => r.AssignedReps).ThenInclude(rr => rr.Rep)
            .Include(r => r.RouteCustomers).ThenInclude(rc => rc.Customer).ThenInclude(c => c.User)
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);

        return routes.Select(MapRouteToDto).ToList();
    }

    public async Task<List<RouteDto>> GetRepRoutesAsync(Guid repUserId, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        var routes = await _unitOfWork.Repository<Route>().Query()
            .Include(r => r.AssignedReps).ThenInclude(rr => rr.Rep)
            .Include(r => r.RouteCustomers).ThenInclude(rc => rc.Customer).ThenInclude(c => c.User)
            .Where(r => r.AssignedReps.Any(rr => rr.RepId == rep.Id) && r.IsActive)
            .ToListAsync(cancellationToken);

        return routes.Select(MapRouteToDto).ToList();
    }

    public async Task DeleteRouteAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        var route = await _unitOfWork.Repository<Route>().GetByIdAsync(routeId, cancellationToken)
            ?? throw new NotFoundException("Route", routeId);

        // soft-delete: mark inactive so lists that filter by IsActive will hide it
        route.IsActive = false;
        _unitOfWork.Repository<Route>().Update(route);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await NotifyRouteRepsAsync(
            routeId,
            "Route Deactivated",
            NotificationType.RouteDeleted,
            $"Route '{route.Name}' has been deactivated and is no longer available for visits.",
            cancellationToken);
    }

    public async Task SetTargetAsync(Guid repId, SalesTargetDto target, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().GetByIdAsync(repId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repId);

        SalesTargetValidator.ValidateTargetPeriod(target.TargetPeriod, target.StartDate, target.EndDate);

        var targetName = target.TargetName?.Trim();
        if (targetName?.Length > 100)
            throw new BusinessException("Target name cannot exceed 100 characters");

        var entity = new SalesTarget
        {
            RepId = repId,
            TargetName = string.IsNullOrWhiteSpace(targetName) ? null : targetName,
            TargetPeriod = target.TargetPeriod,
            StartDate = target.StartDate,
            EndDate = target.EndDate,
            TargetAmount = target.TargetAmount,
            Status = "Active"
        };

        await _unitOfWork.Repository<SalesTarget>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Notify rep about new target (notification + email)
        var repUser = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == repId, cancellationToken);
        if (repUser?.User?.Id != null)
        {
            var targetLabel = string.IsNullOrWhiteSpace(targetName)
                ? $"{target.TargetPeriod} target"
                : $"target '{targetName}'";
            var message = $"A new {targetLabel} of {target.TargetAmount:N2} has been assigned to you ({target.StartDate:d} – {target.EndDate:d}).";
            await _notificationService.SendNotificationAsync(
                repUser.User.Id,
                NotificationType.General,
                "New Sales Target Assigned",
                message,
                cancellationToken);

            if (!string.IsNullOrEmpty(repUser.User.Email))
            {
                var body = $"<h3>New Sales Target</h3><p>Hi {repUser.FullName},</p><p>{message}</p>";
                _ = _emailService.SendEmailAsync(repUser.User.Email, "New Sales Target Assigned", body, cancellationToken);
            }
        }
    }

    public async Task<List<SalesTargetDto>> GetTargetsByRepIdAsync(Guid repId, CancellationToken cancellationToken = default)
    {
        _ = await _unitOfWork.Repository<SalesRepProfile>().GetByIdAsync(repId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repId);

        var targets = await _unitOfWork.Repository<SalesTarget>().Query()
            .Where(t => t.RepId == repId)
            .OrderByDescending(t => t.StartDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return await MapTargetsWithCurrentReportAsync(targets, cancellationToken);
    }

    public async Task DeleteTargetAsync(Guid targetId, CancellationToken cancellationToken = default)
    {
        var target = await _unitOfWork.Repository<SalesTarget>().Query()
            .Include(t => t.Rep).ThenInclude(r => r.User)
            .FirstOrDefaultAsync(t => t.Id == targetId, cancellationToken)
            ?? throw new NotFoundException("SalesTarget", targetId);

        _unitOfWork.Repository<SalesTarget>().Remove(target);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Notify rep about deleted target (notification + email)
        if (target.Rep?.User?.Id != null)
        {
            var targetLabel = string.IsNullOrWhiteSpace(target.TargetName)
                ? $"{target.TargetPeriod} target"
                : $"target '{target.TargetName}'";
            var message = $"Your {targetLabel} ({target.StartDate:d} – {target.EndDate:d}) has been removed by admin.";
            await _notificationService.SendNotificationAsync(
                target.Rep.User.Id,
                NotificationType.General,
                "Sales Target Removed",
                message,
                cancellationToken);

            if (!string.IsNullOrEmpty(target.Rep.User.Email))
            {
                var body = $"<h3>Target Removed</h3><p>Hi {target.Rep.FullName},</p><p>{message}</p>";
                _ = _emailService.SendEmailAsync(target.Rep.User.Email, "Sales Target Removed", body, cancellationToken);
            }
        }
    }

    public async Task<List<SalesTargetDto>> GetTargetsAsync(Guid repUserId, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        var targets = await _unitOfWork.Repository<SalesTarget>().Query()
            .Where(t => t.RepId == rep.Id)
            .OrderByDescending(t => t.StartDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        return await MapTargetsWithCurrentReportAsync(targets, cancellationToken);
    }

    private async Task<List<SalesTargetDto>> MapTargetsWithCurrentReportAsync(List<SalesTarget> targets, CancellationToken cancellationToken)
    {
        var targetIds = targets.Select(t => t.Id).ToList();
        var currentReports = await _unitOfWork.Repository<TargetSalesReport>().Query()
            .Where(r => targetIds.Contains(r.TargetId) && r.IsCurrent)
            .ToListAsync(cancellationToken);
        var reportByTargetId = currentReports.ToDictionary(r => r.TargetId);

        return targets.Select(t =>
        {
            var dto = new SalesTargetDto
            {
                Id = t.Id,
                RepId = t.RepId,
                TargetName = t.TargetName,
                TargetPeriod = t.TargetPeriod,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                CreatedAt = t.CreatedAt,
                TargetAmount = t.TargetAmount,
                AchievedAmount = t.AchievedAmount,
                Status = t.Status
            };

            if (reportByTargetId.TryGetValue(t.Id, out var report))
            {
                dto.HasReport = true;
                dto.CurrentReportId = report.Id;
                dto.ReportFromDate = report.FromDate;
                dto.ReportAsAtDate = report.AsAtDate;
                dto.ReportSourceFileName = report.OriginalFileName;
                dto.ReportUploadedAt = report.UploadedAt;
                dto.DistinctOrderCount = report.DistinctOrderCount;
                dto.DistinctCustomerCount = report.DistinctCustomerCount;
            }

            return dto;
        }).ToList();
    }

    public async Task<List<LeaderboardDto>> GetLeaderboardAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var reps = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.Orders)
            .ToListAsync(cancellationToken);

        var leaderboard = reps.Select(r =>
        {
            var repOrders = r.Orders.Where(o => o.OrderDate >= from && o.OrderDate <= to && o.Status != OrderStatus.Cancelled);
            return new LeaderboardDto
            {
                RepId = r.Id,
                RepName = r.FullName,
                TotalSales = repOrders.Sum(o => o.TotalAmount),
                TotalOrders = repOrders.Count()
            };
        })
        .OrderByDescending(l => l.TotalSales)
        .ToList();

        for (int i = 0; i < leaderboard.Count; i++)
            leaderboard[i].Rank = i + 1;

        return leaderboard;
    }

    private async Task<RouteDto> GetRouteByIdAsync(Guid routeId, CancellationToken cancellationToken)
    {
        var route = await _unitOfWork.Repository<Route>().Query()
            .Include(r => r.AssignedReps).ThenInclude(rr => rr.Rep)
            .Include(r => r.RouteCustomers).ThenInclude(rc => rc.Customer).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(r => r.Id == routeId, cancellationToken)
            ?? throw new NotFoundException("Route", routeId);
        return MapRouteToDto(route);
    }

    private async Task NotifyRepAboutRouteAsync(Guid repId, string title, NotificationType type, string message, CancellationToken cancellationToken)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == repId, cancellationToken);

        if (rep?.User == null)
            return;

        await _notificationService.SendNotificationAsync(rep.User.Id, type, title, message, cancellationToken);

        if (!string.IsNullOrWhiteSpace(rep.User.Email))
        {
            var emailBody = $"<h3>{title}</h3><p>Hi {rep.FullName},</p><p>{message}</p>";
            _ = _emailService.SendEmailAsync(rep.User.Email, title, emailBody, cancellationToken);
        }
    }

    private async Task NotifyRouteRepsAsync(Guid routeId, string title, NotificationType type, string message, CancellationToken cancellationToken)
    {
        var repIds = await _unitOfWork.Repository<RepRoute>().Query()
            .Where(rr => rr.RouteId == routeId)
            .Select(rr => rr.RepId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var reps = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Where(r => repIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        foreach (var rep in reps)
        {
            if (rep.User == null)
                continue;

            await _notificationService.SendNotificationAsync(rep.User.Id, type, title, message, cancellationToken);

            if (!string.IsNullOrWhiteSpace(rep.User.Email))
            {
                var emailBody = $"<h3>{title}</h3><p>Hi {rep.FullName},</p><p>{message}</p>";
                _ = _emailService.SendEmailAsync(rep.User.Email, title, emailBody, cancellationToken);
            }
        }
    }

    private async Task NotifyCustomerAboutRouteAsync(Guid customerId, string title, NotificationType type, string message, CancellationToken cancellationToken)
    {
        var customer = await _unitOfWork.Repository<CustomerProfile>().Query()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        if (customer?.User == null)
            return;

        await _notificationService.SendNotificationAsync(customer.User.Id, type, title, message, cancellationToken);

        if (!string.IsNullOrWhiteSpace(customer.User.Email))
        {
            var emailBody = $"<h3>{title}</h3><p>Hi {customer.ShopName},</p><p>{message}</p>";
            _ = _emailService.SendEmailAsync(customer.User.Email, title, emailBody, cancellationToken);
        }
    }

    public async Task<VisitDto> GetVisitByIdAsync(Guid repUserId, Guid visitId, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        var visit = await _unitOfWork.Repository<Visit>().Query()
            .Include(v => v.Customer).ThenInclude(c => c.User)
            .Include(v => v.Customer).ThenInclude(c => c.Location)
            .Include(v => v.Customer).ThenInclude(c => c.Address)
            .FirstOrDefaultAsync(v => v.Id == visitId && v.RepId == rep.Id, cancellationToken)
            ?? throw new NotFoundException("Visit", visitId);

        return MapVisitToDto(visit);
    }

    public async Task<RouteDto> GetRouteDetailsAsync(Guid repUserId, Guid routeId, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .FirstOrDefaultAsync(r => r.UserId == repUserId, cancellationToken)
            ?? throw new NotFoundException("SalesRep", repUserId);

        var route = await _unitOfWork.Repository<Route>().Query()
            .Include(r => r.AssignedReps).ThenInclude(rr => rr.Rep)
            .Include(r => r.RouteCustomers).ThenInclude(rc => rc.Customer).ThenInclude(c => c.User)
            .FirstOrDefaultAsync(r => r.Id == routeId && r.AssignedReps.Any(rr => rr.RepId == rep.Id), cancellationToken)
            ?? throw new NotFoundException("Route", routeId);

        return MapRouteToDto(route);
    }

    public async Task SoftDeleteAsync(Guid id, string deletedBy, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("SalesRep", id);

        rep.IsDeleted = true;
        rep.DeletedAt = DateTime.UtcNow;
        rep.DeletedBy = deletedBy;
        rep.User.IsActive = false;
        _unitOfWork.Repository<SalesRepProfile>().Update(rep);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rep = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == id && r.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("SalesRep", id);

        rep.IsDeleted = false;
        rep.DeletedAt = null;
        rep.DeletedBy = null;
        rep.User.IsActive = true;
        _unitOfWork.Repository<SalesRepProfile>().Update(rep);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<RepDto>> GetTrashedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Include(r => r.Regions).ThenInclude(rr => rr.Region)
            .Include(r => r.SubRegions).ThenInclude(rs => rs.SubRegion)
            .Include(r => r.Coordinators).ThenInclude(rc => rc.Coordinator)
            .Where(r => r.IsDeleted)
            .OrderByDescending(r => r.DeletedAt);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<RepDto>
        {
            Items = items.Select(MapToDto),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task PurgeOldTrashedAsync(int olderThanDays, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        var toDelete = await _unitOfWork.Repository<SalesRepProfile>().Query()
            .Include(r => r.User)
            .Where(r => r.IsDeleted && r.DeletedAt < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var rep in toDelete)
        {
            rep.User.IsActive = false;
            _unitOfWork.Repository<SalesRepProfile>().Remove(rep);
        }

        if (toDelete.Any())
            await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static RepDto MapToDto(SalesRepProfile r) => new()
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
        CoordinatorIds = r.Coordinators.Select(rc => rc.CoordinatorId).ToList(),
        CoordinatorNames = r.Coordinators.Select(rc => rc.Coordinator?.FullName ?? string.Empty).ToList(),
        Email = r.User?.Email,
        PhoneNumber = r.User?.PhoneNumber,
        IsActive = r.User?.IsActive ?? true,
        MustChangePassword = r.User?.MustChangePassword ?? false,
        TemporaryPassword = r.User?.CurrentPassword,
        CreatedAt = r.CreatedAt
    };

    /// <summary>Parse the comma-separated or JSON-array DaysOfWeek string into a list.</summary>
    private static List<string> ParseDaysOfWeek(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]")
            return [];
        // Try JSON array first, e.g. ["Monday","Wednesday"]
        if (raw.TrimStart().StartsWith('['))
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw) ?? []; }
            catch { /* fall through */ }
        }
        // Comma-separated, e.g. "Monday,Wednesday,Friday"
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
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
            Longitude = rc.Customer?.Location?.Longitude
        }).OrderBy(c => c.VisitOrder).ToList() ?? []
    };

    private static VisitDto MapVisitToDto(Visit v) => new()
    {
        Id = v.Id,
        CustomerId = v.CustomerId,
        CustomerName = v.Customer?.User?.Username ?? "",
        ShopName = v.Customer?.ShopName,
        PlannedDate = v.PlannedDate,
        ActualStartTime = v.ActualStartTime,
        ActualEndTime = v.ActualEndTime,
        Status = v.Status.ToString(),
        Notes = v.Notes,
        OutcomeReason = v.OutcomeReason,
        OrdersPlaced = v.OrdersPlaced,
        PaymentsCollected = v.PaymentsCollected,
        Latitude = v.Customer?.Location?.Latitude,
        Longitude = v.Customer?.Location?.Longitude,
        PhoneNumber = v.Customer?.User?.PhoneNumber,
        Street = v.Customer?.Address?.Street,
        City = v.Customer?.Address?.City,
        State = v.Customer?.Address?.State,
        PostalCode = v.Customer?.Address?.PostalCode,
        Country = v.Customer?.Address?.Country
    };
}