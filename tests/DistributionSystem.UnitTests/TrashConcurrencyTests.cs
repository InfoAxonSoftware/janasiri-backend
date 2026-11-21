using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DistributionSystem.UnitTests;

/// <summary>
/// Models two separate requests that read the same personal Trash entry.
/// The EF concurrency token on PurgedAt must prevent a stale Restore from
/// removing a marker which another request already permanently purged.
/// </summary>
public class TrashConcurrencyTests
{
    private static (DbContextOptions<ApplicationDbContext> Options, Guid Id, Guid UserId) Seed()
    {
        var root = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), root)
            .Options;
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();
        using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(new User
            {
                Id = userId, Username = "trash-concurrency-user", Role = UserRole.SalesRep
            });
            db.UserRecordTrashes.Add(new UserRecordTrash
            {
                Id = id, UserId = userId, RecordKind = "Order",
                RecordId = Guid.NewGuid(), DeletedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        return (options, id, userId);
    }

    [Fact]
    public async Task Restore_stale_after_concurrent_purge_cannot_reexpose_business_record()
    {
        var (options, id, _) = Seed();
        await using var staleRestore = new ApplicationDbContext(options);
        await using var concurrentPurge = new ApplicationDbContext(options);

        var restoringEntity = await staleRestore.UserRecordTrashes.SingleAsync(x => x.Id == id);
        var purgingEntity = await concurrentPurge.UserRecordTrashes.SingleAsync(x => x.Id == id);

        purgingEntity.PurgedAt = DateTime.UtcNow;
        await concurrentPurge.SaveChangesAsync();

        staleRestore.UserRecordTrashes.Remove(restoringEntity);
        var staleSave = async () => await staleRestore.SaveChangesAsync();
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verify = new ApplicationDbContext(options);
        (await verify.UserRecordTrashes.SingleAsync(x => x.Id == id))
            .PurgedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Purge_stale_after_concurrent_restore_does_not_recreate_marker()
    {
        var (options, id, _) = Seed();
        await using var concurrentRestore = new ApplicationDbContext(options);
        await using var stalePurge = new ApplicationDbContext(options);

        var restoringEntity = await concurrentRestore.UserRecordTrashes.SingleAsync(x => x.Id == id);
        var purgingEntity = await stalePurge.UserRecordTrashes.SingleAsync(x => x.Id == id);

        concurrentRestore.UserRecordTrashes.Remove(restoringEntity);
        await concurrentRestore.SaveChangesAsync();

        purgingEntity.PurgedAt = DateTime.UtcNow;
        var staleSave = async () => await stalePurge.SaveChangesAsync();
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verify = new ApplicationDbContext(options);
        (await verify.UserRecordTrashes.AnyAsync(x => x.Id == id)).Should().BeFalse();
    }
}
