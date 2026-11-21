using DistributionSystem.Application.Exceptions;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
    internal const string RepPaymentKind = "RepPayment";

    private readonly IUnitOfWork _uow;

    internal PersonalRecordTrash(IUnitOfWork uow) => _uow = uow;

    internal IQueryable<UserRecordTrash> Query => _uow.Repository<UserRecordTrash>().Query();

    internal async Task HideAsync(Guid userId, string kind, Guid id, CancellationToken ct)
    {
        var existing = await Query.FirstOrDefaultAsync(
            x => x.UserId == userId && x.RecordKind == kind && x.RecordId == id, ct);

        // A record already in Trash (or permanently hidden after expiry/purge)
        // must never be recreated by a duplicate or stale Delete request.
        // Restore explicitly removes the marker before a future Delete is allowed.
        if (existing is not null)
            throw new NotFoundException("Active record", id);

        await _uow.Repository<UserRecordTrash>().AddAsync(
            new UserRecordTrash { UserId = userId, RecordKind = kind, RecordId = id }, ct);

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
                                            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Another concurrent Delete inserted the unique (user,kind,record) marker.
            // Fail closed instead of resetting the earlier Trash/Purge decision.
            throw new NotFoundException("Active record", id);
        }
    }

    internal async Task RestoreAsync(Guid userId, string kind, Guid id, CancellationToken ct)
    {
        var record = await Query.FirstOrDefaultAsync(
            x => x.UserId == userId && x.RecordKind == kind && x.RecordId == id && x.PurgedAt == null, ct)
            ?? throw new NotFoundException("Trash entry", id);
        _uow.Repository<UserRecordTrash>().Remove(record);
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent permanent purge must win over a stale Restore.
            throw new NotFoundException("Restorable Trash entry", id);
        }
    }

    internal async Task PermanentlyHideAsync(Guid userId, string kind, Guid id, CancellationToken ct)
    {
        var record = await Query.FirstOrDefaultAsync(
            x => x.UserId == userId && x.RecordKind == kind && x.RecordId == id && x.PurgedAt == null, ct)
            ?? throw new NotFoundException("Trash entry", id);
        record.PurgedAt = DateTime.UtcNow;
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent Restore/Purge changed the marker: never override it.
            throw new NotFoundException("Trash entry", id);
        }
    }
}
