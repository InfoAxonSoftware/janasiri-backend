using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributionSystem.Application.Services.Background;

/// <summary>
/// Daily job that runs once per midnight UTC and creates "planned" visit
/// records for every active route scheduled on that day of week.
/// This ensures reps see a pre-populated "today" list without manually
/// adding visits.
/// </summary>
public class DailyVisitGenerator : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DailyVisitGenerator> _logger;

    public DailyVisitGenerator(IServiceProvider services, ILogger<DailyVisitGenerator> logger)
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
                var now = DateTime.UtcNow;
                var nextRun = now.Date.AddDays(1);
                var delay = nextRun - now;
                await Task.Delay(delay, stoppingToken);
                // delegate to rep service so logic can be triggered manually as well
                using var scope2 = _services.CreateScope();
                var repService = scope2.ServiceProvider.GetRequiredService<DistributionSystem.Application.Services.Interfaces.IRepService>();
                await repService.GenerateTodayVisitsAsync(stoppingToken);
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyVisitGenerator encountered an error");
            }
        }
    }

    // old implementation kept for reference; not used anymore
    private async Task GenerateVisitsAsync(CancellationToken ct)
    {
        // this method is no longer invoked; logic moved to RepService.GenerateTodayVisitsAsync
        await Task.CompletedTask;
    }
}
