using DistributionSystem.Application.Services.Background;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace DistributionSystem.IntegrationTests;

/// <summary>
/// Real PostgreSQL tests. Never targets a configured application database:
/// connection must be explicitly supplied and be local scratch port/database.
/// If no scratch connection is provided, the test does not execute DB operations.
/// Run explicitly with JANASIRI_TRASH_SCRATCH_CONNECTION set as documented.
/// </summary>
public sealed class PostgresTrashConcurrencyTests
{
    private readonly ITestOutputHelper _output;
    public PostgresTrashConcurrencyTests(ITestOutputHelper output) => _output = output;

    private DbContextOptions<ApplicationDbContext>? Options()
    {
        var source = Environment.GetEnvironmentVariable("JANASIRI_TRASH_SCRATCH_CONNECTION");
        if (string.IsNullOrWhiteSpace(source))
        {
            _output.WriteLine("Not executed: set JANASIRI_TRASH_SCRATCH_CONNECTION for isolated local PostgreSQL tests.");
            return null;
        }
        var connection = new NpgsqlConnectionStringBuilder(source);
        if (connection.Host != "127.0.0.1" || connection.Port != 55469 ||
            connection.Database != "janasiri_trash_scratch")
            throw new InvalidOperationException("Refusing connection: tests require 127.0.0.1:55469/janasiri_trash_scratch only.");
        return new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString).Options;
    }

    private static async Task PrepareAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();
    }

    [Fact]
    public async Task Postgres_personal_stale_restore_cannot_undo_permanent_purge()
    {
        var options = Options(); if (options is null) return;
        await PrepareAsync(options);
        var userId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Users.Add(new User {
                Id = userId, Username = $"scratch-{userId:N}",
                Email = $"{userId:N}@example.invalid", Role = UserRole.SalesRep
            });
            await seed.SaveChangesAsync();
            await new PersonalRecordTrash(new UnitOfWork(seed))
                .HideAsync(userId, PersonalRecordTrash.OrderKind, recordId, default);
        }

        await using var stale = new ApplicationDbContext(options);
        await using var current = new ApplicationDbContext(options);
        var old = await stale.UserRecordTrashes.SingleAsync(x =>
            x.UserId == userId && x.RecordKind == "Order" && x.RecordId == recordId);

        await new PersonalRecordTrash(new UnitOfWork(current))
            .PermanentlyHideAsync(userId, PersonalRecordTrash.OrderKind, recordId, default);

        stale.UserRecordTrashes.Remove(old);
        var staleSave = async () => await stale.SaveChangesAsync();
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var verify = new ApplicationDbContext(options);
        (await verify.UserRecordTrashes.SingleAsync(x => x.Id == old.Id))
            .PurgedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Postgres_admin_expiry_and_restore_race_rejects_stale_cleanup()
    {
        var options = Options(); if (options is null) return;
        await PrepareAsync(options);
        var recordId = Guid.NewGuid();
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.QuickRequests.Add(new QuickRequest {
                Id = recordId, RequestNumber = "SC-" + recordId.ToString("N")[..8],
                Type = QuickRequestType.Order, CustomerName = "Local only",
                Details = "Temporary concurrency fixture",
                IsDeletedByAdmin = true, AdminDeletedAt = DateTime.UtcNow.AddDays(-8)
            });
            await seed.SaveChangesAsync();
        }
        await using var staleCleanup = new ApplicationDbContext(options);
        await using var restoring = new ApplicationDbContext(options);
        var old = await staleCleanup.QuickRequests.SingleAsync(x => x.Id == recordId);
        var restore = await restoring.QuickRequests.SingleAsync(x => x.Id == recordId);
        restore.IsDeletedByAdmin = false;
        restore.AdminDeletedAt = null;
        await restoring.SaveChangesAsync();

        old.IsPurgedByAdmin = true;
        old.AdminPurgedAt = DateTime.UtcNow;
        var staleSave = async () => await staleCleanup.SaveChangesAsync();
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var verify = new ApplicationDbContext(options);
        var final = await verify.QuickRequests.SingleAsync(x => x.Id == recordId);
        final.IsDeletedByAdmin.Should().BeFalse();
        final.IsPurgedByAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Postgres_two_cleanup_instances_do_not_double_purge_admin_record()
    {
        var options = Options(); if (options is null) return;
        await PrepareAsync(options);
        var id = Guid.NewGuid();
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.QuickRequests.Add(new QuickRequest {
                Id = id, RequestNumber = "SC-" + id.ToString("N")[..8], Type = QuickRequestType.Quotation,
                CustomerName = "Local only", Details = "Two cleanup readers",
                IsDeletedByAdmin = true, AdminDeletedAt = DateTime.UtcNow.AddDays(-9)
            });
            await seed.SaveChangesAsync();
        }

        // Simulate two scheduler instances selecting the same row before
        // either finishes. Only one update may be committed.
        await using var a = new ApplicationDbContext(options);
        await using var b = new ApplicationDbContext(options);
        var x = await a.QuickRequests.SingleAsync(q => q.Id == id);
        var y = await b.QuickRequests.SingleAsync(q => q.Id == id);
        x.IsPurgedByAdmin = true;
        x.AdminPurgedAt = DateTime.UtcNow;
        await a.SaveChangesAsync();
        y.IsPurgedByAdmin = true;
        y.AdminPurgedAt = DateTime.UtcNow;
        var second = async () => await b.SaveChangesAsync();
        await second.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verify = new ApplicationDbContext(options);
        var final = await verify.QuickRequests.SingleAsync(q => q.Id == id);
        final.IsPurgedByAdmin.Should().BeTrue();
        final.IsDeletedByAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Postgres_hourly_expiry_is_idempotent_and_retains_business_rows()
    {
        var options = Options(); if (options is null) return;
        await PrepareAsync(options);
        var id = Guid.NewGuid();
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.QuickRequests.Add(new QuickRequest {
                Id = id, RequestNumber = "SC-" + id.ToString("N")[..8], Type = QuickRequestType.Order,
                CustomerName = "Local only", Details = "Expiry fixture",
                IsDeletedByAdmin = true, AdminDeletedAt = DateTime.UtcNow.AddDays(-9)
            });
            await seed.SaveChangesAsync();
        }
        // Expires any older shared scratch fixtures too; verification is scoped
        // to the newly created fixture and a second sweep must be idempotent.
        await using var cleanup = new ApplicationDbContext(options);
        var first = await TrashCleanupService.ExpireAdminTrashAsync(new UnitOfWork(cleanup),
            DateTime.UtcNow.AddDays(-7), default);
        first.Should().BeGreaterThanOrEqualTo(1);
        var second = await TrashCleanupService.ExpireAdminTrashAsync(new UnitOfWork(cleanup),
            DateTime.UtcNow.AddDays(-7), default);
        second.Should().Be(0);
        await using var verify = new ApplicationDbContext(options);
        var retained = await verify.QuickRequests.SingleAsync(x => x.Id == id);
        retained.IsPurgedByAdmin.Should().BeTrue();
        retained.AdminPurgedAt.Should().NotBeNull();
    }
}
