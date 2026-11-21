using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using FluentAssertions;
using Moq;

namespace DistributionSystem.UnitTests;

public class GlobalDeleteBehaviorTests
{
    [Fact]
    public async Task Global_order_delete_hides_from_customer_and_rep_standard_lists()
    {
        using var f = new OrderTrashFixture();
        var customerUserId = f.Db.CustomerProfiles.Single(c => c.Id == f.CustomerId).UserId;

        await f.Service.AdminSoftDeleteAsync(f.OrderId, "admin");

        (await f.Service.GetAllAsync(new() { RepId = f.RepId })).Items.Should()
            .NotContain(x => x.Id == f.OrderId);
        (await f.Service.GetAllAsync(new() { CustomerId = f.CustomerId })).Items.Should()
            .NotContain(x => x.Id == f.OrderId);
        var lookup = async () => await f.Service.GetByIdAsync(f.OrderId);
        await lookup.Should().ThrowAsync<NotFoundException>();

        await f.Service.AdminRestoreAsync(f.OrderId);
        (await f.Service.GetAllAsync(new() { CustomerId = f.CustomerId })).Items.Should()
            .ContainSingle(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Global_quick_request_delete_hides_rep_and_coordinator()
    {
        using var f = new OrderTrashFixture();
        var sut = new QuickRequestService(
            new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());

        await sut.AdminSoftDeleteAsync(f.QuickOrderId, "admin");

        (await sut.GetRepRequestsAsync(f.RepUserId)).Should()
            .NotContain(x => x.Id == f.QuickOrderId);
        (await sut.GetForCoordinatorAsync(f.CoordinatorUserId)).Should()
            .NotContain(x => x.Id == f.QuickOrderId);

        var access = async () => await sut.GetRepRequestByIdAsync(f.QuickOrderId, f.RepUserId);
        await access.Should().ThrowAsync<NotFoundException>();

        await sut.AdminRestoreAsync(f.QuickOrderId);
        (await sut.GetRepRequestsAsync(f.RepUserId)).Should()
            .ContainSingle(x => x.Id == f.QuickOrderId);
    }

    [Fact]
    public async Task Global_quotation_delete_hides_all_normal_role_lists()
    {
        using var f = new OrderTrashFixture();
        var quote = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QT-TEST-GLOBAL",
            CustomerId = f.CustomerId, RepId = f.RepId,
            CoordinatorId = f.CoordinatorId, Status = QuotationStatus.Draft
        };
        f.Db.Quotations.Add(quote);
        await f.Db.SaveChangesAsync();
        var service = new QuotationService(
            new UnitOfWork(f.Db),
            Mock.Of<INotificationService>(),
            Mock.Of<IEmailService>());
        var customerUserId = f.Db.CustomerProfiles.Single(c => c.Id == f.CustomerId).UserId;

        await service.AdminSoftDeleteAsync(quote.Id, "admin", default);

        (await service.CustomerGetQuotationsAsync(customerUserId, 1, 20, null, default))
            .Items.Should().NotContain(q => q.Id == quote.Id);
        (await service.RepGetQuotationsAsync(f.RepUserId, 1, 20, null, default))
            .Items.Should().NotContain(q => q.Id == quote.Id);
        (await service.CoordinatorGetQuotationsAsync(f.CoordinatorUserId, 1, 20, null, default))
            .Items.Should().NotContain(q => q.Id == quote.Id);

        var access = async () => await service.RepGetQuotationByIdAsync(f.RepUserId, quote.Id, default);
        await access.Should().ThrowAsync<NotFoundException>();

        await service.AdminRestoreAsync(quote.Id, default);
        (await service.RepGetQuotationsAsync(f.RepUserId, 1, 20, null, default))
            .Items.Should().ContainSingle(q => q.Id == quote.Id);
    }
}


public class CoordinatorIsolationIntegrationTests
{
    [Fact]
    public async Task Coordinator_delete_is_private_for_orders_quick_requests_and_quotations()
    {
        using var f = new OrderTrashFixture();
        var otherUserId = Guid.NewGuid();
        var otherCoordinatorId = Guid.NewGuid();
        var user = new User { Id = otherUserId, Username = "other-coordinator",
            Role = UserRole.SalesCoordinator, IsActive = true };
        var coordinator = new CoordinatorProfile { Id = otherCoordinatorId,
            UserId = otherUserId, User = user, EmployeeCode = "C-SECOND", FullName = "Second Coordinator" };
        var rep = f.Db.SalesRepProfiles.Single(x => x.Id == f.RepId);
        var link = new RepCoordinator { RepId = rep.Id, Rep = rep,
            CoordinatorId = coordinator.Id, Coordinator = coordinator };
        rep.Coordinators.Add(link);
        coordinator.RepCoordinators.Add(link);

        var quote = new Quotation { QuotationNumber = "QT-TWO-COORDS",
            CustomerId = f.CustomerId, RepId = f.RepId, CoordinatorId = f.CoordinatorId };
        f.Db.AddRange(user, coordinator, link, quote);
        await f.Db.SaveChangesAsync();

        var quick = new QuickRequestService(new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());
        var quotations = new QuotationService(new UnitOfWork(f.Db),
            Mock.Of<INotificationService>(), Mock.Of<IEmailService>());
        var all = new DistributionSystem.Application.DTOs.Order.UnifiedOrderFilterRequest { PageSize = 100 };

        await f.Service.CoordinatorSoftDeleteAsync(f.OrderId, f.CoordinatorUserId);
        await quick.CoordinatorSoftDeleteAsync(f.QuickOrderId, f.CoordinatorUserId);
        await quotations.CoordinatorSoftDeleteAsync(quote.Id, f.CoordinatorUserId, default);

        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, all, false))
            .Items.Should().NotContain(x => x.Id == f.OrderId || x.Id == f.QuickOrderId);
        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, all, true))
            .Items.Should().Contain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedCoordinatorAsync(otherUserId, all, false))
            .Items.Should().Contain(x => x.Id == f.OrderId);
        (await f.Service.GetUnifiedCoordinatorAsync(otherUserId, all, false))
            .Items.Should().Contain(x => x.Id == f.QuickOrderId);

        (await quick.GetForCoordinatorAsync(otherUserId)).Should()
            .ContainSingle(x => x.Id == f.QuickOrderId);
        (await quick.CoordinatorGetTrashAsync(f.CoordinatorUserId)).Should()
            .ContainSingle(x => x.Id == f.QuickOrderId);
        (await quotations.CoordinatorGetQuotationsAsync(otherUserId, 1, 10, null, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);
        (await quotations.CoordinatorGetQuotationsAsync(f.CoordinatorUserId, 1, 10, null, default))
            .Items.Should().NotContain(x => x.Id == quote.Id);

        await f.Service.CoordinatorRestoreAsync(f.OrderId, f.CoordinatorUserId);
        await quick.CoordinatorRestoreAsync(f.QuickOrderId, f.CoordinatorUserId);
        await quotations.CoordinatorRestoreAsync(quote.Id, f.CoordinatorUserId, default);

        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, all, false))
            .Items.Should().Contain(x => x.Id == f.OrderId && x.Kind == "Order");
        (await quick.GetForCoordinatorAsync(f.CoordinatorUserId)).Should()
            .ContainSingle(x => x.Id == f.QuickOrderId);
        (await quotations.CoordinatorGetQuotationsAsync(f.CoordinatorUserId, 1, 10, null, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);
    }
}


public class PersonalRecordTrashTests
{
    [Fact]
    public async Task Expired_personal_trash_is_purged_without_restoring_business_record()
    {
        using var f = new OrderTrashFixture();
        var service = new PersonalRecordTrash(new UnitOfWork(f.Db));
        await service.HideAsync(f.CoordinatorUserId, PersonalRecordTrash.OrderKind, f.OrderId, default);

        var entry = f.Db.UserRecordTrashes.Single();
        entry.DeletedAt = DateTime.UtcNow.AddDays(-8);
        await f.Db.SaveChangesAsync();

        var cutoff = DateTime.UtcNow.AddDays(-7);
        var expired = await DistributionSystem.Application.Services.Background.TrashCleanupService
            .ExpirePersonalTrashAsync(new UnitOfWork(f.Db), cutoff, default);
        expired.Should().Be(1);

        entry.PurgedAt.Should().NotBeNull();
        f.Db.Orders.Find(f.OrderId).Should().NotBeNull();
        var second = await DistributionSystem.Application.Services.Background.TrashCleanupService
            .ExpirePersonalTrashAsync(new UnitOfWork(f.Db), cutoff, default);
        second.Should().Be(0);
    }

    [Fact]
    public async Task Two_coordinators_cannot_delete_each_others_personal_view()
    {
        using var f = new OrderTrashFixture();
        var otherCoordinatorUserId = Guid.NewGuid();
        f.Db.Users.Add(new User
        {
            Id = otherCoordinatorUserId, Username = "second-coordinator",
            Role = UserRole.SalesCoordinator, IsActive = true
        });
        await f.Db.SaveChangesAsync();

        var service = new PersonalRecordTrash(new UnitOfWork(f.Db));
        await service.HideAsync(f.CoordinatorUserId, PersonalRecordTrash.OrderKind, f.OrderId, default);

        f.Db.UserRecordTrashes.Count(x => x.RecordId == f.OrderId &&
            x.UserId == f.CoordinatorUserId).Should().Be(1);
        f.Db.UserRecordTrashes.Count(x => x.RecordId == f.OrderId &&
            x.UserId == otherCoordinatorUserId).Should().Be(0);

        await service.PermanentlyHideAsync(f.CoordinatorUserId, PersonalRecordTrash.OrderKind, f.OrderId, default);
        f.Db.UserRecordTrashes.Single().PurgedAt.Should().NotBeNull();

        var restore = async () => await service.RestoreAsync(
            f.CoordinatorUserId, PersonalRecordTrash.OrderKind, f.OrderId, default);
        await restore.Should().ThrowAsync<NotFoundException>();
        f.Db.Orders.Find(f.OrderId).Should().NotBeNull();
    }
}
