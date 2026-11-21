using DistributionSystem.Application.Exceptions;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.Application.Services.Implementations;

/// <summary>
/// Private per-user trash state; shared domain records are never deleted here.
/// Only the owning user can be used by caller methods after role authorization.
/// </summary>
internal sealed class PersonalRecordTrash
{
    internal const string OrderKind = "Order";
    internal const string QuotationKind = "Quotation";
    internal const string QuickRequestKind = "QuickRequest";

    private readonly IUnitOfWork _uow;

    internal PersonalRecordTrash(IUnitOfWork uow) => _uow = uow;

    internal IQueryable<UserRecordTrash> Query => _uow.Repository<UserRecordTrash>().Query();

    internal async Task HideAsync(Guid userId, string kind, Guid id, CancellationToken ct)
    {
        var existing = await Query.FirstOrDefaultAsync(
            x => x.UserId == userId && x.RecordKind == kind && x.RecordId == id, ct);

        if (existing is null)
            await _uow.Repository<UserRecordTrash>().AddAsync(
                new UserRecordTrash { UserId = userId, RecordKind = kind, RecordId = id }, ct);
        else if (existing.PurgedAt is null)
            existing.DeletedAt = DateTime.UtcNow;

        await _uow.SaveChangesAsync(ct);
    }

    internal async Task RestoreAsync(Guid userId, string kind, Guid id, CancellationToken ct)
    {
        var record = await Query.FirstOrDefaultAsync(
            x => x.UserId == userId && x.RecordKind == kind && x.RecordId == id && x.PurgedAt == null, ct)
            ?? throw new NotFoundException("Trash entry", id);
        _uow.Repository<UserRecordTrash>().Remove(record);
        await _uow.SaveChangesAsync(ct);
    }

    internal async Task PermanentlyHideAsync(Guid userId, string kind, Guid id, CancellationToken ct)
    {
        var record = await Query.FirstOrDefaultAsync(
            x => x.UserId == userId && x.RecordKind == kind && x.RecordId == id && x.PurgedAt == null, ct)
            ?? throw new NotFoundException("Trash entry", id);
        record.PurgedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
    }
}
