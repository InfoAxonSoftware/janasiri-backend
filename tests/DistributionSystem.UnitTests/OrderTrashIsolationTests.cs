using DistributionSystem.Application.DTOs.Order;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Application.Services.Interfaces;
using Moq;
using DistributionSystem.Application.Services.Background;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.UnitTests;

public sealed class OrderTrashFixture : IDisposable
{
    public ApplicationDbContext Db { get; }
    public OrderService Service { get; }
    public Guid RepUserId { get; } = Guid.NewGuid();
    public Guid CoordinatorUserId { get; } = Guid.NewGuid();
    public Guid RepId { get; } = Guid.NewGuid();
    public Guid CoordinatorId { get; } = Guid.NewGuid();
    public Guid CustomerId { get; } = Guid.NewGuid();
    public Guid OrderId { get; } = Guid.NewGuid();
    public Guid QuickOrderId { get; } = Guid.NewGuid();

    public OrderTrashFixture()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Db = new ApplicationDbContext(options);
        Service = new OrderService(new UnitOfWork(Db));
        Seed();
    }

    private void Seed()
    {
        var repUser = new User { Id = RepUserId, Username = nameof(RepUserId), Role = UserRole.SalesRep };
        var coordinatorUser = new User { Id = CoordinatorUserId, Username = nameof(CoordinatorUserId), Role = UserRole.SalesCoordinator };
        var customerUser = new User { Username = nameof(CustomerId), Role = UserRole.Customer };
        var rep = new SalesRepProfile { Id = RepId, UserId = RepUserId, User = repUser, FullName = nameof(RepId), EmployeeCode = nameof(RepId) };
        var coordinator = new CoordinatorProfile { Id = CoordinatorId, UserId = CoordinatorUserId, User = coordinatorUser, FullName = nameof(CoordinatorId), EmployeeCode = nameof(CoordinatorId) };
        var link = new RepCoordinator { RepId = RepId, CoordinatorId = CoordinatorId, Rep = rep, Coordinator = coordinator };
        rep.Coordinators.Add(link); coordinator.RepCoordinators.Add(link);
        var customer = new CustomerProfile { Id = CustomerId, UserId = customerUser.Id, User = customerUser, ShopName = nameof(CustomerId), AssignedRepId = RepId, AssignedCoordinatorId = CoordinatorId };
        var order = new Order { Id = OrderId, OrderNumber = nameof(OrderId), CustomerId = CustomerId, Customer = customer, RepId = RepId, Rep = rep, Status = OrderStatus.Pending, TotalAmount = 100 };
        var quick = new QuickRequest { Id = QuickOrderId, RequestNumber = nameof(QuickOrderId), Type = QuickRequestType.Order, CustomerName = customer.ShopName, Details = nameof(QuickOrderId), RepId = RepId, Rep = rep };
        Db.AddRange(repUser, coordinatorUser, customerUser, rep, coordinator, link, customer, order, quick);
        Db.SaveChanges();
    }

    public void Dispose() => Db.Dispose();
}

public class OrderTrashIsolationTests
{
    private enum ItemKind { Order, QuickOrder }

    private static UnifiedOrderFilterRequest All() => new() { PageSize = 100 };

    [Fact]
    public async Task Unified_personal_trash_uses_the_per_user_deletion_timestamp_for_both_order_types()
    {
        using var f = new OrderTrashFixture();
        var quickService = new QuickRequestService(
            new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());

        await f.Service.RepSoftDeleteAsync(f.OrderId, f.RepUserId);
        await quickService.RepSoftDeleteAsync(f.QuickOrderId, f.RepUserId);

        var page = await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), true);
        var orderItem = page.Items.Should().ContainSingle(x => x.Id == f.OrderId).Subject;
        var orderDeletedAt = f.Db.UserRecordTrashes.Single(x =>
            x.UserId == f.RepUserId && x.RecordKind == "Order" && x.RecordId == f.OrderId).DeletedAt;
        orderItem.DeletedAt.Should().BeCloseTo(orderDeletedAt, TimeSpan.FromSeconds(1));
        orderItem.Order!.DeletedAt.Should().BeCloseTo(orderDeletedAt, TimeSpan.FromSeconds(1));

        var quickItem = page.Items.Should().ContainSingle(x => x.Id == f.QuickOrderId).Subject;
        var quickDeletedAt = f.Db.UserRecordTrashes.Single(x =>
            x.UserId == f.RepUserId && x.RecordKind == "QuickRequest" && x.RecordId == f.QuickOrderId).DeletedAt;
        quickItem.DeletedAt.Should().BeCloseTo(quickDeletedAt, TimeSpan.FromSeconds(1));
        quickItem.QuickOrder!.DeletedAt.Should().BeCloseTo(quickDeletedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Legacy_purged_personal_record_cannot_be_reopened_by_duplicate_delete()
    {
        using var f = new OrderTrashFixture();
        await f.Service.RepSoftDeleteAsync(f.OrderId, f.RepUserId);

        var marker = f.Db.UserRecordTrashes.Single(x =>
            x.UserId == f.RepUserId && x.RecordKind == "Order" && x.RecordId == f.OrderId);
        var firstDeletedAt = marker.DeletedAt;

        var duplicate = async () => await f.Service.RepSoftDeleteAsync(f.OrderId, f.RepUserId);
        await duplicate.Should().ThrowAsync<DistributionSystem.Application.Exceptions.NotFoundException>();
        marker.DeletedAt.Should().Be(firstDeletedAt);
        marker.PurgedAt.Should().BeNull();

        await new PersonalRecordTrash(new UnitOfWork(f.Db)).PermanentlyHideAsync(
            f.RepUserId, PersonalRecordTrash.OrderKind, f.OrderId, default);
        var purgedAt = marker.PurgedAt;
        await duplicate.Should().ThrowAsync<DistributionSystem.Application.Exceptions.NotFoundException>();
        marker.PurgedAt.Should().Be(purgedAt);

        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), true))
            .Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), false))
            .Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedAdminAsync(All(), false))
            .Items.Should().ContainSingle(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Admin_delete_hides_from_all_active_roles()
    {
        using var f = new OrderTrashFixture();

        await f.Service.AdminSoftDeleteAsync(f.OrderId, nameof(Admin_delete_hides_from_all_active_roles));

        (await f.Service.GetUnifiedAdminAsync(All(), false)).Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedAdminAsync(All(), true)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), false)).Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, All(), false)).Items.Should().NotContain(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Admin_deleted_quick_order_is_hidden_from_all_active_roles()
    {
        using var f = new OrderTrashFixture();
        var quick = await f.Db.QuickRequests.FindAsync(f.QuickOrderId);
        quick!.IsDeletedByAdmin = true;
        quick.AdminDeletedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();

        (await f.Service.GetUnifiedAdminAsync(All(), false)).Items.Should().NotContain(x => x.Id == f.QuickOrderId);
        (await f.Service.GetUnifiedAdminAsync(All(), true)).Items.Should().ContainSingle(x => x.Id == f.QuickOrderId);
        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), false)).Items.Should().NotContain(x => x.Id == f.QuickOrderId);
        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, All(), false)).Items.Should().NotContain(x => x.Id == f.QuickOrderId);

        quick.IsDeletedByAdmin = false;
        quick.AdminDeletedAt = null;
        await f.Db.SaveChangesAsync();
        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), false)).Items.Should().ContainSingle(x => x.Id == f.QuickOrderId);
    }

    [Fact]
    public async Task Rep_delete_hides_only_from_rep()
    {
        using var f = new OrderTrashFixture();

        await f.Service.RepSoftDeleteAsync(f.OrderId, f.RepUserId);

        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), false)).Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), true)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedAdminAsync(All(), false)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, All(), false)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Coordinator_delete_hides_only_from_coordinator()
    {
        using var f = new OrderTrashFixture();

        await f.Service.CoordinatorSoftDeleteAsync(f.OrderId, f.CoordinatorUserId);

        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, All(), false)).Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, All(), true)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedAdminAsync(All(), false)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), false)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Mixed_bulk_restore_restores_order_and_quick_order_for_only_that_role()
    {
        using var f = new OrderTrashFixture();
        var order = await f.Db.Orders.FindAsync(f.OrderId);
        var quick = await f.Db.QuickRequests.FindAsync(f.QuickOrderId);
        order!.IsDeleted = order.IsDeletedByRep = true;
        quick!.IsDeletedByAdmin = quick.IsDeletedByRep = true;
        await f.Db.SaveChangesAsync();

        await f.Service.AdminRestoreTrashAsync([
            new() { Id = f.OrderId, Kind = nameof(ItemKind.Order) },
            new() { Id = f.QuickOrderId, Kind = nameof(ItemKind.QuickOrder) }
        ]);

        order.IsDeleted.Should().BeFalse();
        quick.IsDeletedByAdmin.Should().BeFalse();
        quick.IsDeletedByRep.Should().BeTrue();
    }

    [Fact]
    public async Task Each_roles_restore_clears_only_its_own_state()
    {
        using var f = new OrderTrashFixture();
        var order = await f.Db.Orders.FindAsync(f.OrderId);
        order!.IsDeleted = true;
        await f.Db.SaveChangesAsync();
        var personal = new PersonalRecordTrash(new UnitOfWork(f.Db));
        await personal.HideAsync(f.RepUserId, PersonalRecordTrash.OrderKind, f.OrderId, default);
        await personal.HideAsync(f.CoordinatorUserId, PersonalRecordTrash.OrderKind, f.OrderId, default);

        await f.Service.AdminRestoreTrashAsync([new() { Id = f.OrderId, Kind = nameof(ItemKind.Order) }]);
        order.IsDeleted.Should().BeFalse();
        f.Db.UserRecordTrashes.Single(x => x.RecordId == f.OrderId && x.UserId == f.RepUserId).PurgedAt.Should().BeNull();

        await f.Service.RepRestoreTrashAsync(f.RepUserId, [new() { Id = f.OrderId, Kind = nameof(ItemKind.Order) }]);
        f.Db.UserRecordTrashes.Should().ContainSingle(x => x.UserId == f.CoordinatorUserId);

        await f.Service.CoordinatorRestoreTrashAsync(f.CoordinatorUserId, [new() { Id = f.OrderId, Kind = nameof(ItemKind.Order) }]);
        order.IsDeleted.Should().BeFalse();
        f.Db.UserRecordTrashes.Should().NotContain(x => x.UserId == f.CoordinatorUserId);
    }

    [Fact]
    public async Task Bulk_permanent_delete_supports_mixed_kinds_and_only_admin_state()
    {
        using var f = new OrderTrashFixture();
        var order = await f.Db.Orders.FindAsync(f.OrderId);
        var quick = await f.Db.QuickRequests.FindAsync(f.QuickOrderId);
        order!.IsDeleted = true;
        quick!.IsDeletedByAdmin = true;
        await f.Db.SaveChangesAsync();

        await f.Service.AdminPurgeTrashAsync([
            new() { Id = f.OrderId, Kind = nameof(ItemKind.Order) },
            new() { Id = f.QuickOrderId, Kind = nameof(ItemKind.QuickOrder) }
        ]);

        order.IsPurgedByAdmin.Should().BeTrue();
        quick.IsPurgedByAdmin.Should().BeTrue();
        order.IsPurgedByRep.Should().BeFalse();
        quick.IsPurgedByRep.Should().BeFalse();
        (await f.Db.Orders.FindAsync(f.OrderId)).Should().NotBeNull();
        (await f.Db.QuickRequests.FindAsync(f.QuickOrderId)).Should().NotBeNull();
    }

    [Fact]
    public async Task Permanent_delete_is_view_only_and_keeps_database_rows()
    {
        using var f = new OrderTrashFixture();
        await f.Service.RepSoftDeleteAsync(f.OrderId, f.RepUserId);

        await f.Service.RepPurgeTrashAsync(f.RepUserId,
            [new() { Id = f.OrderId, Kind = nameof(ItemKind.Order) }]);

        (await f.Db.Orders.FindAsync(f.OrderId)).Should().NotBeNull();
        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), true)).Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedAdminAsync(All(), false)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, All(), false)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Admin_purge_keeps_record_globally_hidden()
    {
        using var f = new OrderTrashFixture();
        await f.Service.AdminSoftDeleteAsync(f.OrderId, nameof(Admin_purge_keeps_record_globally_hidden));

        await f.Service.AdminPurgeTrashAsync([new() { Id = f.OrderId, Kind = nameof(ItemKind.Order) }]);

        (await f.Db.Orders.FindAsync(f.OrderId)).Should().NotBeNull();
        (await f.Service.GetUnifiedAdminAsync(All(), true)).Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), false)).Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, All(), false)).Items.Should().NotContain(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Coordinator_permanent_delete_affects_only_coordinator_visibility()
    {
        using var f = new OrderTrashFixture();
        await f.Service.CoordinatorSoftDeleteAsync(f.OrderId, f.CoordinatorUserId);

        await f.Service.CoordinatorPurgeTrashAsync(f.CoordinatorUserId,
            [new() { Id = f.OrderId, Kind = nameof(ItemKind.Order) }]);

        (await f.Db.Orders.FindAsync(f.OrderId)).Should().NotBeNull();
        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, All(), true)).Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedAdminAsync(All(), false)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), false)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Empty_trash_purges_only_the_current_roles_view()
    {
        using var f = new OrderTrashFixture();
        var order = await f.Db.Orders.FindAsync(f.OrderId);
        var quick = await f.Db.QuickRequests.FindAsync(f.QuickOrderId);
        order!.IsDeleted = true;
        quick!.IsDeletedByAdmin = true;
        await f.Db.SaveChangesAsync();
        var personal = new PersonalRecordTrash(new UnitOfWork(f.Db));
        await personal.HideAsync(f.CoordinatorUserId, PersonalRecordTrash.OrderKind, f.OrderId, default);
        await personal.HideAsync(f.CoordinatorUserId, PersonalRecordTrash.QuickRequestKind, f.QuickOrderId, default);

        await f.Service.CoordinatorEmptyTrashAsync(f.CoordinatorUserId);

        f.Db.UserRecordTrashes.Where(x => x.UserId == f.CoordinatorUserId)
            .Should().HaveCount(2).And.OnlyContain(x => x.PurgedAt.HasValue);
        order.IsPurgedByAdmin.Should().BeFalse();
        quick.IsPurgedByAdmin.Should().BeFalse();
        (await f.Service.GetUnifiedAdminAsync(All(), true)).Items.Should().Contain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedAdminAsync(All(), true)).Items.Should().Contain(x => x.Id == f.QuickOrderId);
    }

    [Fact]
    public async Task Unified_search_filters_before_pagination_and_returns_no_duplicates()
    {
        using var f = new OrderTrashFixture();
        var targetId = Guid.NewGuid();
        for (var index = 0; index < 25; index++)
        {
            f.Db.Orders.Add(new Order
            {
                Id = index == 24 ? targetId : Guid.NewGuid(),
                OrderNumber = index == 24 ? nameof(targetId) : $"page-{index}",
                CustomerId = f.CustomerId,
                RepId = f.RepId,
                Status = index % 2 == 0 ? OrderStatus.Pending : OrderStatus.Approved,
                OrderDate = DateTime.UtcNow.AddDays(-index),
                TotalAmount = index
            });
        }
        await f.Db.SaveChangesAsync();

        var search = await f.Service.GetUnifiedRepAsync(f.RepUserId,
            new() { Page = 1, PageSize = 5, Search = nameof(targetId) }, false);
        var all = await f.Service.GetUnifiedRepAsync(f.RepUserId, All(), false);

        search.TotalCount.Should().Be(1);
        search.Items.Should().ContainSingle(x => x.Id == targetId);
        all.Items.Select(x => (x.Id, x.Kind)).Should().OnlyHaveUniqueItems();
        all.TotalCount.Should().Be(all.Items.Count());
    }

    [Fact]
    public void Cleanup_job_expires_admin_trash_without_physical_deletion()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src"))) root = root.Parent;
        root.Should().NotBeNull();
        var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "DistributionSystem.Application",
            "Services", "Background", nameof(TrashCleanupService) + ".cs"));

        source.Should().Contain("ExpireAdminTrashAsync");
        source.Should().Contain("AdminRecordTrashPurge.MarkOrder");
source.Should().Contain("AdminRecordTrashPurge.MarkQuickRequest");
source.Should().Contain("AdminRecordTrashPurge.MarkQuotation");
        source.Should().NotContain("Repository<Order>().Remove");
        source.Should().NotContain("Repository<QuickRequest>().Remove");
        source.Should().NotContain("Repository<Quotation>().Remove");
    }
}
