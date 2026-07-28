using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributionSystem.Application.Services.Background;

/// <summary>
/// Daily cleanup for legacy retained entities. Orders and Quick Requests are
/// intentionally excluded because their role-specific trash is manual-only.
/// </summary>
public class TrashCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TrashCleanupService> _logger;
    private const int TrashRetentionDays = 7;

    public TrashCleanupService(IServiceProvider services, ILogger<TrashCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Run once per day at 02:00 UTC
                var now = DateTime.UtcNow;
                var nextRun = now.Date.AddDays(1).AddHours(2);
                var delay = nextRun - now;
                await Task.Delay(delay, stoppingToken);

                _logger.LogInformation("TrashCleanupService: purging records older than {Days} days", TrashRetentionDays);

                using var scope = _services.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();
                var repService = scope.ServiceProvider.GetRequiredService<IRepService>();
                var coordinatorService = scope.ServiceProvider.GetRequiredService<ICoordinatorService>();

                var cutoff = DateTime.UtcNow.AddDays(-TrashRetentionDays);

                // ── Admin-deleted Quotations ──────────────────────────────────
                var adminDeletedQuotations = await uow.Repository<Quotation>().Query()
                    .Where(q => q.IsDeleted && q.DeletedAt.HasValue && q.DeletedAt.Value < cutoff)
                    .ToListAsync(stoppingToken);
                foreach (var q in adminDeletedQuotations) uow.Repository<Quotation>().Remove(q);
                if (adminDeletedQuotations.Count > 0) await uow.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("TrashCleanupService: purged {Count} admin-deleted quotations", adminDeletedQuotations.Count);

                // ── Rep-deleted Quotations ────────────────────────────────────
                var repDeletedQuotations = await uow.Repository<Quotation>().Query()
                    .Where(q => q.IsDeletedByRep && q.RepDeletedAt.HasValue && q.RepDeletedAt.Value < cutoff)
                    .ToListAsync(stoppingToken);
                foreach (var q in repDeletedQuotations) { q.IsDeletedByRep = false; q.RepDeletedAt = null; uow.Repository<Quotation>().Update(q); }
                if (repDeletedQuotations.Count > 0) await uow.SaveChangesAsync(stoppingToken);

                // ── Coordinator-deleted Quotations ────────────────────────────
                var coordDeletedQuotations = await uow.Repository<Quotation>().Query()
                    .Where(q => q.IsDeletedByCoordinator && q.CoordinatorDeletedAt.HasValue && q.CoordinatorDeletedAt.Value < cutoff)
                    .ToListAsync(stoppingToken);
                foreach (var q in coordDeletedQuotations) { q.IsDeletedByCoordinator = false; q.CoordinatorDeletedAt = null; uow.Repository<Quotation>().Update(q); }
                if (coordDeletedQuotations.Count > 0) await uow.SaveChangesAsync(stoppingToken);

                // ── Customer-deleted Quotations ───────────────────────────────
                var custDeletedQuotations = await uow.Repository<Quotation>().Query()
                    .Where(q => q.IsDeletedByCustomer && q.CustomerDeletedAt.HasValue && q.CustomerDeletedAt.Value < cutoff)
                    .ToListAsync(stoppingToken);
                foreach (var q in custDeletedQuotations) { q.IsDeletedByCustomer = false; q.CustomerDeletedAt = null; uow.Repository<Quotation>().Update(q); }
                if (custDeletedQuotations.Count > 0) await uow.SaveChangesAsync(stoppingToken);

                // ── Existing user/profile purge ───────────────────────────────
                await repService.PurgeOldTrashedAsync(TrashRetentionDays, stoppingToken);
                await coordinatorService.PurgeOldTrashedAsync(TrashRetentionDays, stoppingToken);

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
