using DistributionSystem.Application.DTOs.Order;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

public partial class OrderService
{
    public Task AdminRestoreTrashAsync(IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken ct = default) =>
        MutateTrashAsync(AdminTrashRole, null, null, items, true, ct);

    public Task AdminPurgeTrashAsync(IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken ct = default) =>
        MutateTrashAsync(AdminTrashRole, null, null, items, false, ct);

    public async Task RepRestoreTrashAsync(Guid userId, IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken ct = default) =>
        await MutateTrashAsync(RepTrashRole, await ResolveRepIdAsync(userId, ct), null, items, true, ct);

    public async Task RepPurgeTrashAsync(Guid userId, IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken ct = default) =>
        await MutateTrashAsync(RepTrashRole, await ResolveRepIdAsync(userId, ct), null, items, false, ct);

    public async Task CoordinatorRestoreTrashAsync(Guid userId, IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken ct = default) =>
        await MutateTrashAsync(CoordinatorTrashRole, null, await ResolveCoordinatorIdAsync(userId, ct), items, true, ct);

    public async Task CoordinatorPurgeTrashAsync(Guid userId, IReadOnlyCollection<OrderTrashItemRequest> items, CancellationToken ct = default) =>
        await MutateTrashAsync(CoordinatorTrashRole, null, await ResolveCoordinatorIdAsync(userId, ct), items, false, ct);

    public Task AdminEmptyTrashAsync(CancellationToken ct = default) => EmptyTrashAsync(AdminTrashRole, null, null, ct);

    public async Task RepEmptyTrashAsync(Guid userId, CancellationToken ct = default) =>
        await EmptyTrashAsync(RepTrashRole, await ResolveRepIdAsync(userId, ct), null, ct);

    public async Task CoordinatorEmptyTrashAsync(Guid userId, CancellationToken ct = default) =>
        await EmptyTrashAsync(CoordinatorTrashRole, null, await ResolveCoordinatorIdAsync(userId, ct), ct);
    private async Task<Guid> ResolveRepIdAsync(Guid userId, CancellationToken ct)
    {
        var profile = await _unitOfWork.Repository<SalesRepProfile>().FirstOrDefaultAsync(r => r.UserId == userId, ct);
        return profile!.Id;
    }
    private async Task<Guid> ResolveCoordinatorIdAsync(Guid userId, CancellationToken ct)
    {
        var profile = await _unitOfWork.Repository<CoordinatorProfile>().FirstOrDefaultAsync(c => c.UserId == userId, ct);
        return profile!.Id;
    }
    private static List<OrderTrashItemRequest> ValidateTrashItems(IReadOnlyCollection<OrderTrashItemRequest> items)
    {
        if (items.Count == 0) throw new ArgumentException(nameof(items));
        if (items.Any(i => i.Id == Guid.Empty)) throw new ArgumentException(nameof(items));
        return items.Select(i => new OrderTrashItemRequest { Id = i.Id, Kind = NormalizeKind(i.Kind) })
            .DistinctBy(i => (i.Id, i.Kind)).ToList();
    }

    private static string NormalizeKind(string kind)
    {
        if (kind.Equals(OrderKind, StringComparison.OrdinalIgnoreCase)) return OrderKind;
        if (kind.Equals(QuickOrderKind, StringComparison.OrdinalIgnoreCase)) return QuickOrderKind;
        throw new ArgumentException(nameof(kind));
    }

    private async Task MutateTrashAsync(string role, Guid? repId, Guid? coordinatorId,
        IReadOnlyCollection<OrderTrashItemRequest> rawItems, bool restore, CancellationToken ct)
    {
        if (role == CoordinatorTrashRole)
        {
            await MutateCoordinatorTrashAsync(coordinatorId!.Value, rawItems, restore, ct);
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var item in ValidateTrashItems(rawItems))
        {
            if (item.Kind == OrderKind) await MutateOrderAsync(item.Id, role, repId, coordinatorId, restore, now, ct);
            else await MutateQuickOrderAsync(item.Id, role, repId, coordinatorId, restore, now, ct);
        }
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task MutateCoordinatorTrashAsync(
        Guid coordinatorId, IReadOnlyCollection<OrderTrashItemRequest> rawItems,
        bool restore, CancellationToken ct)
    {
        var coordinatorUserId = await _unitOfWork.Repository<CoordinatorProfile>().Query()
            .Where(c => c.Id == coordinatorId).Select(c => c.UserId).SingleAsync(ct);
        var entries = _unitOfWork.Repository<UserRecordTrash>();
        var now = DateTime.UtcNow;

        // Validate every selected item before mutating any entry.
        var actions = new List<UserRecordTrash>();
        foreach (var item in ValidateTrashItems(rawItems))
        {
            var kind = item.Kind == OrderKind ? PersonalRecordTrash.OrderKind : PersonalRecordTrash.QuickRequestKind;
            var allowed = item.Kind == OrderKind
                ? await _unitOfWork.Repository<Order>().Query().AnyAsync(o => o.Id == item.Id &&
                    !o.IsDeleted && !o.IsPurgedByAdmin &&
                    (o.Customer.AssignedCoordinatorId == coordinatorId ||
                     (o.RepId.HasValue && o.Rep != null &&
                      o.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinatorId))), ct)
                : await _unitOfWork.Repository<QuickRequest>().Query().AnyAsync(r => r.Id == item.Id &&
                    r.Type == QuickRequestType.Order && !r.IsDeletedByAdmin && !r.IsPurgedByAdmin &&
                    r.Rep != null && r.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinatorId), ct);
            if (!allowed) throw new NotFoundException(item.Kind, item.Id);

            var entry = await entries.Query().FirstOrDefaultAsync(x => x.UserId == coordinatorUserId &&
                x.RecordKind == kind && x.RecordId == item.Id && x.PurgedAt == null, ct)
                ?? throw new NotFoundException("Trash entry", item.Id);
            actions.Add(entry);
        }
        foreach (var entry in actions)
        {
            if (restore) entries.Remove(entry);
            else entry.PurgedAt = now;
        }
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task MutateOrderAsync(Guid id, string role, Guid? repId, Guid? coordinatorId,
        bool restore, DateTime now, CancellationToken ct)
    {
        var query = _unitOfWork.Repository<Order>().Query().Where(o => o.Id == id);
        if (role == AdminTrashRole) query = query.Where(o => o.IsDeleted && !o.IsPurgedByAdmin);
        if (role == RepTrashRole) query = query.Where(o => o.RepId == repId && o.IsDeletedByRep && !o.IsPurgedByRep);
        if (role == CoordinatorTrashRole)
            query = query.Where(o => o.IsDeletedByCoordinator && !o.IsPurgedByCoordinator &&
                (o.Customer.AssignedCoordinatorId == coordinatorId ||
                 (o.RepId.HasValue && o.Rep != null && o.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinatorId))));
        var order = await query.FirstOrDefaultAsync(ct);
        if (order == null) throw new NotFoundException(nameof(Order), id);
        MutateOrderTrash(order, role, restore, now);
    }

    private static void MutateOrderTrash(Order order, string role, bool restore, DateTime now)
    {
        if (role == AdminTrashRole)
        {
            if (restore) { order.IsDeleted = false; order.DeletedAt = null; order.DeletedBy = null; }
            else { order.IsPurgedByAdmin = true; order.AdminPurgedAt = now; }
        }
        if (role == RepTrashRole)
        {
            if (restore) { order.IsDeletedByRep = false; order.RepDeletedAt = null; }
            else { order.IsPurgedByRep = true; order.RepPurgedAt = now; }
        }
        if (role == CoordinatorTrashRole)
        {
            if (restore) { order.IsDeletedByCoordinator = false; order.CoordinatorDeletedAt = null; }
            else { order.IsPurgedByCoordinator = true; order.CoordinatorPurgedAt = now; }
        }
    }

    private async Task MutateQuickOrderAsync(Guid id, string role, Guid? repId, Guid? coordinatorId,
        bool restore, DateTime now, CancellationToken ct)
    {
        var query = _unitOfWork.Repository<QuickRequest>().Query()
            .Where(r => r.Id == id && r.Type == QuickRequestType.Order);
        if (role == AdminTrashRole) query = query.Where(r => r.IsDeletedByAdmin && !r.IsPurgedByAdmin);
        if (role == RepTrashRole) query = query.Where(r => r.RepId == repId && r.IsDeletedByRep && !r.IsPurgedByRep);
        if (role == CoordinatorTrashRole)
            query = query.Where(r => r.IsDeletedByCoordinator && !r.IsPurgedByCoordinator &&
                r.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinatorId));
        var request = await query.FirstOrDefaultAsync(ct);
        if (request == null) throw new NotFoundException(nameof(QuickRequest), id);
        MutateQuickTrash(request, role, restore, now);
    }

    private static void MutateQuickTrash(QuickRequest request, string role, bool restore, DateTime now)
    {
        if (role == AdminTrashRole)
        {
            if (restore) { request.IsDeletedByAdmin = false; request.AdminDeletedAt = null; }
            else { request.IsPurgedByAdmin = true; request.AdminPurgedAt = now; }
        }
        if (role == RepTrashRole)
        {
            if (restore) { request.IsDeletedByRep = false; request.RepDeletedAt = null; }
            else { request.IsPurgedByRep = true; request.RepPurgedAt = now; }
        }
        if (role == CoordinatorTrashRole)
        {
            if (restore) { request.IsDeletedByCoordinator = false; request.CoordinatorDeletedAt = null; }
            else { request.IsPurgedByCoordinator = true; request.CoordinatorPurgedAt = now; }
        }
    }

    private async Task EmptyTrashAsync(string role, Guid? repId, Guid? coordinatorId, CancellationToken ct)
    {
        if (role == CoordinatorTrashRole)
        {
            var userId = await _unitOfWork.Repository<CoordinatorProfile>().Query()
                .Where(c => c.Id == coordinatorId).Select(c => c.UserId).SingleAsync(ct);
            var entries = await _unitOfWork.Repository<UserRecordTrash>().Query()
                .Where(x => x.UserId == userId && x.PurgedAt == null &&
                    (x.RecordKind == PersonalRecordTrash.OrderKind ||
                     x.RecordKind == PersonalRecordTrash.QuickRequestKind))
                .ToListAsync(ct);
            foreach (var entry in entries) entry.PurgedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            return;
        }
        var orders = _unitOfWork.Repository<Order>().Query();
        var quick = _unitOfWork.Repository<QuickRequest>().Query().Where(r => r.Type == QuickRequestType.Order);
        if (role == AdminTrashRole)
        {
            orders = orders.Where(o => o.IsDeleted && !o.IsPurgedByAdmin);
            quick = quick.Where(r => r.IsDeletedByAdmin && !r.IsPurgedByAdmin);
        }
        if (role == RepTrashRole)
        {
            orders = orders.Where(o => o.RepId == repId && o.IsDeletedByRep && !o.IsPurgedByRep);
            quick = quick.Where(r => r.RepId == repId && r.IsDeletedByRep && !r.IsPurgedByRep);
        }
        if (role == CoordinatorTrashRole)
        {
            orders = orders.Where(o => o.IsDeletedByCoordinator && !o.IsPurgedByCoordinator &&
                (o.Customer.AssignedCoordinatorId == coordinatorId ||
                 (o.RepId.HasValue && o.Rep != null && o.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinatorId))));
            quick = quick.Where(r => r.IsDeletedByCoordinator && !r.IsPurgedByCoordinator &&
                r.Rep.Coordinators.Any(rc => rc.CoordinatorId == coordinatorId));
        }
        var now = DateTime.UtcNow;
        foreach (var order in await orders.ToListAsync(ct)) MutateOrderTrash(order, role, false, now);
        foreach (var request in await quick.ToListAsync(ct)) MutateQuickTrash(request, role, false, now);
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
