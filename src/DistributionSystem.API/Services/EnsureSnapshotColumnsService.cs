using System;
using System.Threading;
using System.Threading.Tasks;
using DistributionSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributionSystem.API.Services
{
    /// <summary>
    /// Hosted service that ensures the two new columns exist on OrderItems table.
    /// This needs to run before any EF query against OrderItem occurs, because the
    /// domain model now includes ProductName/ProductSKU.
    /// The operations are idempotent and safe to execute multiple times.
    /// </summary>
    public class EnsureSnapshotColumnsService : IHostedService
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<EnsureSnapshotColumnsService> _logger;

        public EnsureSnapshotColumnsService(IServiceProvider provider, ILogger<EnsureSnapshotColumnsService> logger)
        {
            _provider = provider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                // execute raw SQL to add columns if they don't exist
                await ctx.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE \"OrderItems\" ADD COLUMN IF NOT EXISTS \"ProductName\" text NOT NULL DEFAULT '';",
                    cancellationToken);
                await ctx.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE \"OrderItems\" ADD COLUMN IF NOT EXISTS \"ProductSKU\" text;",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure order item snapshot columns");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
