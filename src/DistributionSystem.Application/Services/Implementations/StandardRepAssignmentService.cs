using DistributionSystem.Application.DTOs.QuickRequests;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DistributionSystem.Application.Services.Implementations;

public sealed class StandardRepAssignmentService(IUnitOfWork uow, INotificationPublisher publisher, ILogger<StandardRepAssignmentService> logger)
{
    private async Task EnsureAdmin(Guid actorId, CancellationToken ct)
    {
        if (!await uow.Repository<User>().Query().AnyAsync(u => u.Id == actorId && u.IsActive &&
            (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin), ct))
            throw new ForbiddenException("Only administrators can manage these assignments.");
    }
    private IQueryable<SalesRepProfile> Eligible() => uow.Repository<SalesRepProfile>().Query()
        .Where(r => !r.IsDeleted && r.User.IsActive && r.User.Role == UserRole.SalesRep);
    public async Task<StandardRepAssignmentDto> GetAsync(string kind, Guid id, Guid actorId, CancellationToken ct)
    {
        await EnsureAdmin(actorId, ct);
        var record = await Load(kind, id, ct);
        return new StandardRepAssignmentDto
        {
            AssignedReps = record.Read(), AssignmentVersion = record.Version,
            EligibleReps = await Eligible().OrderBy(r => r.FullName)
                .Select(r => new GuestOrderRepOptionDto { Id = r.Id, FullName = r.FullName, EmployeeCode = r.EmployeeCode }).ToListAsync(ct)
        };
    }
    public async Task<StandardRepAssignmentDto> SetAsync(string kind, Guid id, Guid actorId,
        AssignGuestOrderRepsDto dto, string updatedBy, CancellationToken ct)
    {
        await EnsureAdmin(actorId, ct);
        var record = await Load(kind, id, ct);
        if (dto.RepIds == null || dto.ExpectedRepIds == null || dto.RepIds.Contains(Guid.Empty) ||
            dto.RepIds.Distinct().Count() != dto.RepIds.Count)
            throw new ArgumentException("Invalid sales rep selection.");
        var current = record.Read().Select(r => r.Id).ToHashSet();
        if (record.Version != dto.ExpectedVersion || !current.SetEquals(dto.ExpectedRepIds))
            throw new DbUpdateConcurrencyException("The sales rep assignment has changed. Refresh the record.");
        var desired = dto.RepIds.ToHashSet();
        var added = desired.Except(current).ToArray();
        var removed = current.Except(desired).ToArray();
        var addedProfiles = await Eligible().Where(r => added.Contains(r.Id)).ToListAsync(ct);
        if (addedProfiles.Count != added.Length)
            throw new ForbiddenException("A selected sales rep is inactive or unavailable.");
        if (added.Length != 0 || removed.Length != 0)
        {
            var profiles = await uow.Repository<SalesRepProfile>().Query().Where(r => desired.Contains(r.Id)).ToListAsync(ct);
            if (profiles.Count != desired.Count) throw new ForbiddenException("A selected sales rep is unavailable.");
            var primary = record.Primary.HasValue && desired.Contains(record.Primary.Value)
                ? record.Primary : desired.OrderBy(x => x).Select(x => (Guid?)x).FirstOrDefault();
            record.Apply(primary, profiles, Guid.NewGuid(), updatedBy);
            var formerUsers = await uow.Repository<SalesRepProfile>().Query()
                .Where(r => removed.Contains(r.Id) && r.User.IsActive && r.User.Role == UserRole.SalesRep)
                .Select(r => r.UserId).Distinct().ToListAsync(ct);
            var label = kind == "orders" ? "Order" : "Quotation";
            var notices = formerUsers.Select(userId => new Notification
            {
                UserId = userId, NotificationType = NotificationType.General,
                Title = label + " Assignment Removed", Message = $"#{record.Number} is no longer assigned to you."
            }).Concat(addedProfiles.Select(rep => new Notification
            {
                UserId = rep.UserId, NotificationType = kind == "orders" ? NotificationType.NewOrder : NotificationType.QuotationSubmitted,
                Title = "New " + label + " Assigned", Message = $"{label} #{record.Number} has been assigned to you.",
                Metadata = JsonSerializer.Serialize(new { kind = kind == "orders" ? "StandardOrderAssigned" : "StandardQuotationAssigned", recordId = id })
            })).ToList();
            await uow.Repository<Notification>().AddRangeAsync(notices, ct);
            await uow.SaveChangesAsync(ct);
            foreach (var notice in notices)
                try { await publisher.PublishToUserAsync(notice.UserId, NotificationService.MapToDto(notice), CancellationToken.None); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not publish assignment notification {NotificationId}", notice.Id); }
        }
        return await GetAsync(kind, id, actorId, ct);
    }
    private sealed record AssignmentRecord(Guid? Primary, Guid? Version, string Number,
        Func<List<GuestOrderRepOptionDto>> Read, Action<Guid?, List<SalesRepProfile>, Guid, string> Apply);
    private async Task<AssignmentRecord> Load(string kind, Guid id, CancellationToken ct)
    {
        if (kind == "orders")
        {
            var x = await uow.Repository<Order>().Query().Include(x => x.Rep).Include(x => x.AdditionalReps).ThenInclude(a => a.Rep)
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted && !x.IsPurgedByAdmin, ct)
                ?? throw new NotFoundException("Order", id);
            return new(x.RepId, x.AssignmentVersion, x.OrderNumber,
                () => Map(x.RepId, x.Rep, x.AdditionalReps.Select(a => (a.RepId, a.Rep))),
                (primary, profiles, version, actor) =>
                {
                    var extras = profiles.Where(r => r.Id != primary).ToDictionary(r => r.Id);
                    foreach (var a in x.AdditionalReps.Where(a => !extras.ContainsKey(a.RepId)).ToList())
                    { x.AdditionalReps.Remove(a); uow.Repository<OrderAdditionalRep>().Remove(a); }
                    foreach (var r in extras.Values.Where(r => !x.AdditionalReps.Any(a => a.RepId == r.Id)))
                        x.AdditionalReps.Add(new OrderAdditionalRep { OrderId = x.Id, Order = x, RepId = r.Id, Rep = r });
                    x.RepId = primary; x.Rep = profiles.FirstOrDefault(r => r.Id == primary);
                    x.AssignmentVersion = version; x.UpdatedBy = actor;
                });
        }
        if (kind == "quotations")
        {
            var x = await uow.Repository<Quotation>().Query().Include(x => x.Rep).Include(x => x.AdditionalReps).ThenInclude(a => a.Rep)
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted && !x.IsPurgedByAdmin, ct)
                ?? throw new NotFoundException("Quotation", id);
            return new(x.RepId, x.AssignmentVersion, x.QuotationNumber,
                () => Map(x.RepId, x.Rep, x.AdditionalReps.Select(a => (a.RepId, a.Rep))),
                (primary, profiles, version, actor) =>
                {
                    var extras = profiles.Where(r => r.Id != primary).ToDictionary(r => r.Id);
                    foreach (var a in x.AdditionalReps.Where(a => !extras.ContainsKey(a.RepId)).ToList())
                    { x.AdditionalReps.Remove(a); uow.Repository<QuotationAdditionalRep>().Remove(a); }
                    foreach (var r in extras.Values.Where(r => !x.AdditionalReps.Any(a => a.RepId == r.Id)))
                        x.AdditionalReps.Add(new QuotationAdditionalRep { QuotationId = x.Id, Quotation = x, RepId = r.Id, Rep = r });
                    x.RepId = primary; x.Rep = profiles.FirstOrDefault(r => r.Id == primary);
                    x.AssignmentVersion = version; x.UpdatedBy = actor;
                });
        }
        throw new ArgumentException("Unsupported record kind.");
    }
    internal static List<GuestOrderRepOptionDto> Map(Guid? primary, SalesRepProfile? rep, IEnumerable<(Guid Id, SalesRepProfile Rep)> extras)
    {
        var result = extras.Select(a => new GuestOrderRepOptionDto { Id = a.Id, FullName = a.Rep?.FullName ?? "", EmployeeCode = a.Rep?.EmployeeCode ?? "" }).ToList();
        if (primary.HasValue) result.Add(new GuestOrderRepOptionDto { Id = primary.Value, FullName = rep?.FullName ?? "", EmployeeCode = rep?.EmployeeCode ?? "" });
        return result.OrderBy(a => a.FullName).ThenBy(a => a.Id).ToList();
    }
}