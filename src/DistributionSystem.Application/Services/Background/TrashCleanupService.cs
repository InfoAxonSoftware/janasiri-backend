using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributionSystem.Application.Services.Background;

/// <summary>
/// Expires personal and global Trash visibility after seven days.
/// Business rows and attachments remain stored for audit and financial retention.
/// </summary>
public class TrashCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TrashCleanupService> _logger;
    private const int RecordTrashRetentionDays = 7;
    private const int ProfileTrashRetentionDays = 30;

    public TrashCleanupService(IServiceProvider services, ILogger<TrashCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    // Batched and idempotent; callable from unit tests without running the scheduler.
    internal static async Task<int> ExpirePersonalTrashAsync(
        IUnitOfWork uow, DateTime cutoffUtc, CancellationToken ct)
    {
        var changed = 0;
        const int batchSize = 200;
        while (true)
        {
            var batch = await uow.Repository<UserRecordTrash>().Query()
                .Where(x => x.PurgedAt == null && x.DeletedAt < cutoffUtc)
                .OrderBy(x => x.DeletedAt).Take(batchSize).ToListAsync(ct);
            if (batch.Count == 0) break;
            var now = DateTime.UtcNow;
            foreach (var entry in batch) entry.PurgedAt = now;
            await uow.SaveChangesAsync(ct);
            changed += batch.Count;
        }
        return changed;
    }

    // Expire Admin Trash without physically deleting business or financial rows.
    // Batched and idempotent so retries and concurrent app instances are safe.
    internal static async Task<int> ExpireAdminTrashAsync(
        IUnitOfWork uow, DateTime cutoffUtc, CancellationToken ct)
    {
        var changed = 0;
        const int batchSize = 200;

        while (true)
        {
            var orders = await uow.Repository<Order>().Query()
                .Where(x => x.IsDeleted && !x.IsPurgedByAdmin &&
                    x.DeletedAt.HasValue && x.DeletedAt.Value < cutoffUtc)
                .OrderBy(x => x.DeletedAt).Take(batchSize).ToListAsync(ct);
            if (orders.Count == 0) break;
            var now = DateTime.UtcNow;
            foreach (var order in orders)
                AdminRecordTrashPurge.MarkOrder(order, now);
            await uow.SaveChangesAsync(ct);
            changed += orders.Count;
        }

        while (true)
        {
            var quickRequests = await uow.Repository<QuickRequest>().Query()
                .Where(x => x.IsDeletedByAdmin && !x.IsPurgedByAdmin &&
                    x.AdminDeletedAt.HasValue && x.AdminDeletedAt.Value < cutoffUtc)
                .OrderBy(x => x.AdminDeletedAt).Take(batchSize).ToListAsync(ct);
            if (quickRequests.Count == 0) break;
            foreach (var request in quickRequests)
            {
                AdminRecordTrashPurge.MarkQuickRequest(request, DateTime.UtcNow);
            }
            await uow.SaveChangesAsync(ct);
            changed += quickRequests.Count;
        }

        while (true)
        {
            var payments = await uow.Repository<RepPayment>().Query()
                .Where(x => x.IsDeletedByAdmin && !x.IsPurgedByAdmin &&
                    x.AdminDeletedAt.HasValue && x.AdminDeletedAt.Value < cutoffUtc)
                .OrderBy(x => x.AdminDeletedAt).Take(batchSize).ToListAsync(ct);
            if (payments.Count == 0) break;
            var now = DateTime.UtcNow;
            foreach (var payment in payments)
                AdminRecordTrashPurge.MarkRepPayment(payment, now);
            await uow.SaveChangesAsync(ct);
            changed += payments.Count;
        }

        while (true)
        {
            var quotations = await uow.Repository<Quotation>().Query()
                .Where(x => x.IsDeleted && !x.IsPurgedByAdmin &&
                    x.DeletedAt.HasValue && x.DeletedAt.Value < cutoffUtc)
                .OrderBy(x => x.DeletedAt).Take(batchSize).ToListAsync(ct);
            if (quotations.Count == 0) break;
            foreach (var quotation in quotations)
            {
                AdminRecordTrashPurge.MarkQuotation(quotation, DateTime.UtcNow);
            }
            await uow.SaveChangesAsync(ct);
            changed += quotations.Count;
        }

        return changed;
    }

    internal static DateTime GetNextRunUtc(DateTime nowUtc)
    {
        var currentHour = new DateTime(
            nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
        return currentHour.AddHours(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Run hourly so seven-day retention expires within one hour of its cutoff.
                var now = DateTime.UtcNow;
                var nextRun = GetNextRunUtc(now);
                await Task.Delay(nextRun - now, stoppingToken);

                _logger.LogInformation("TrashCleanupService: expiring business Trash after {RecordDays} days and profile Trash after {ProfileDays} days",
                    RecordTrashRetentionDays, ProfileTrashRetentionDays);

                using var scope = _services.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repService = scope.ServiceProvider.GetRequiredService<IRepService>();
                var coordinatorService = scope.ServiceProvider.GetRequiredService<ICoordinatorService>();

                var cutoff = DateTime.UtcNow.AddDays(-RecordTrashRetentionDays);

                // Per-user Trash expiry never restores a shared record. Purge the
                // private Trash view while retaining the permanent hidden marker.
                var expiredPersonalCount = await ExpirePersonalTrashAsync(uow, cutoff, stoppingToken);
                _logger.LogInformation("TrashCleanupService: expired {Count} personal trash entries", expiredPersonalCount);

                // Expire global Admin Trash and personal Trash through the same
                // non-destructive retention rules. No Order/Quotation/QuickRequest
                // business rows or stored files are physically deleted here.
                var expiredAdminCount = await ExpireAdminTrashAsync(uow, cutoff, stoppingToken);
                _logger.LogInformation("TrashCleanupService: expired {Count} Admin trash entries", expiredAdminCount);

                // ── Existing user/profile purge ───────────────────────────────
                await repService.PurgeOldTrashedAsync(ProfileTrashRetentionDays, stoppingToken);
                await coordinatorService.PurgeOldTrashedAsync(ProfileTrashRetentionDays, stoppingToken);

                _logger.LogInformation("TrashCleanupService: purge complete");
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TrashCleanupService encountered an error");
            }
        }
    }
}
