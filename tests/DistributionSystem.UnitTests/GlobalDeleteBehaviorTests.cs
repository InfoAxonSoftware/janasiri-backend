using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Application.Services.Background;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using FluentAssertions;
using Moq;

namespace DistributionSystem.UnitTests;

public class QuotationDirectAccessTests
{
    [Fact]
    public async Task Admin_active_details_do_not_reopen_deleted_or_purged_quotation()
    {
        using var f = new OrderTrashFixture();
        var quote = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QT-TRASH-DIRECT-ACCESS",
            CustomerId = f.CustomerId, RepId = f.RepId, CoordinatorId = f.CoordinatorId,
            Status = QuotationStatus.Draft
        };
        f.Db.Quotations.Add(quote);
        await f.Db.SaveChangesAsync();

        var service = new QuotationService(new UnitOfWork(f.Db),
            Mock.Of<INotificationService>(), Mock.Of<IEmailService>());
        (await service.AdminGetQuotationByIdAsync(quote.Id, default)).Id.Should().Be(quote.Id);

        await service.AdminSoftDeleteAsync(quote.Id, "Admin", default);
        var getDeleted = async () => await service.AdminGetQuotationByIdAsync(quote.Id, default);
        await getDeleted.Should().ThrowAsync<NotFoundException>();

        await service.AdminPurgeAsync(quote.Id, default);
        var getPurged = async () => await service.AdminGetQuotationByIdAsync(quote.Id, default);
        await getPurged.Should().ThrowAsync<NotFoundException>();
    }
}

public class QuickRequestAttachmentAccessTests
{
    [Fact]
    public async Task Admin_can_inspect_attachment_metadata_in_Admin_Trash_but_not_after_purge()
    {
        using var f = new OrderTrashFixture();
        var service = new QuickRequestService(new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());
        await service.AdminSoftDeleteAsync(f.QuickOrderId, "Admin");

        var normal = async () => await service.GetByIdAsync(f.QuickOrderId);
        await normal.Should().ThrowAsync<NotFoundException>();

        (await service.GetByIdAsync(f.QuickOrderId, includeAdminTrash: true))
            .Id.Should().Be(f.QuickOrderId);

        await service.AdminPurgeAsync(f.QuickOrderId);
        var purged = async () => await service.GetByIdAsync(f.QuickOrderId, includeAdminTrash: true);
        await purged.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Rep_and_Coordinator_can_inspect_own_personal_Trash_only_until_purged()
    {
        using var f = new OrderTrashFixture();
        var service = new QuickRequestService(new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());

        await service.RepSoftDeleteAsync(f.QuickOrderId, f.RepUserId);
        var activeRep = async () => await service.GetRepRequestByIdAsync(f.QuickOrderId, f.RepUserId);
        await activeRep.Should().ThrowAsync<NotFoundException>();
        (await service.GetRepRequestByIdAsync(f.QuickOrderId, f.RepUserId,
            includePersonalTrash: true)).Id.Should().Be(f.QuickOrderId);

        await service.CoordinatorSoftDeleteAsync(f.QuickOrderId, f.CoordinatorUserId);
        var activeCoordinator = async () => await service.GetForCoordinatorByIdAsync(
            f.QuickOrderId, f.CoordinatorUserId);
        await activeCoordinator.Should().ThrowAsync<NotFoundException>();
        (await service.GetForCoordinatorByIdAsync(f.QuickOrderId, f.CoordinatorUserId,
            includePersonalTrash: true)).Id.Should().Be(f.QuickOrderId);

        await service.RepPurgeAsync(f.QuickOrderId, f.RepUserId);
        var purgedRep = async () => await service.GetRepRequestByIdAsync(
            f.QuickOrderId, f.RepUserId, includePersonalTrash: true);
        await purgedRep.Should().ThrowAsync<NotFoundException>();

        await service.CoordinatorPurgeAsync(f.QuickOrderId, f.CoordinatorUserId);
        var purgedCoordinator = async () => await service.GetForCoordinatorByIdAsync(
            f.QuickOrderId, f.CoordinatorUserId, includePersonalTrash: true);
        await purgedCoordinator.Should().ThrowAsync<NotFoundException>();
    }
}

public class QuickRequestHiddenMutationTests
{
    [Fact]
    public async Task Rep_upload_is_rejected_when_Admin_has_globally_deleted_request()
    {
        using var f = new OrderTrashFixture();
        var quick = new QuickRequestService(new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());
        var request = f.Db.QuickRequests.Single(x => x.Id == f.QuickOrderId);
        request.IsDeletedByAdmin = true;
        request.AdminDeletedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();

        var upload = async () => await quick.AddImagesAsync(
            f.QuickOrderId, f.RepUserId, new List<Microsoft.AspNetCore.Http.IFormFile>());
        await upload.Should().ThrowAsync<NotFoundException>();
        f.Db.QuickRequestAttachments.Should().BeEmpty();

        var update = async () => await quick.UpdateStatusAsync(f.QuickOrderId,
            new DistributionSystem.Application.DTOs.QuickRequests.UpdateQuickRequestStatusDto
            { Status = "Approved" }, "Admin");
        await update.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Rep_upload_is_rejected_after_personal_delete_but_Admin_can_update_record()
    {
        using var f = new OrderTrashFixture();
        var quick = new QuickRequestService(new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());
        await quick.RepSoftDeleteAsync(f.QuickOrderId, f.RepUserId);

        var upload = async () => await quick.AddImagesAsync(
            f.QuickOrderId, f.RepUserId, new List<Microsoft.AspNetCore.Http.IFormFile>());
        await upload.Should().ThrowAsync<NotFoundException>();

        var updated = await quick.UpdateStatusAsync(f.QuickOrderId,
            new DistributionSystem.Application.DTOs.QuickRequests.UpdateQuickRequestStatusDto
            { Status = "Approved" }, "Admin");
        updated.Status.Should().Be("Approved");
    }
}

public class TrashCleanupScheduleTests
{
    [Theory]
    [InlineData(2026, 11, 21, 10, 30, 0, 2026, 11, 21, 11)]
    [InlineData(2026, 11, 21, 23, 59, 0, 2026, 11, 22, 0)]
    [InlineData(2026, 11, 21, 10, 0, 0, 2026, 11, 21, 11)]
    public void Next_run_is_the_next_utc_hour(
        int year, int month, int day, int hour, int minute, int second,
        int expectedYear, int expectedMonth, int expectedDay, int expectedHour)
    {
        var now = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
        var expected = new DateTime(expectedYear, expectedMonth, expectedDay, expectedHour, 0, 0, DateTimeKind.Utc);

        TrashCleanupService.GetNextRunUtc(now).Should().Be(expected);
    }
}

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





public class ManualPersonalPurgeTests
{
    [Fact]
    public async Task Rep_and_customer_manual_quotation_purge_only_hides_from_their_own_trash()
    {
        using var f = new OrderTrashFixture();
        var customerUserId = f.Db.CustomerProfiles.Single(c => c.Id == f.CustomerId).UserId;
        var quote = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QT-PERSONAL-PURGE",
            CustomerId = f.CustomerId, RepId = f.RepId, CoordinatorId = f.CoordinatorId,
            Status = QuotationStatus.Draft
        };
        f.Db.Quotations.Add(quote);
        await f.Db.SaveChangesAsync();
        var service = new QuotationService(new UnitOfWork(f.Db),
            Mock.Of<INotificationService>(), Mock.Of<IEmailService>());

        await service.RepSoftDeleteAsync(quote.Id, f.RepUserId, default);
        var repEntry = f.Db.UserRecordTrashes.Single(x => x.UserId == f.RepUserId && x.RecordId == quote.Id);
        (await service.RepGetTrashAsync(f.RepUserId, 1, 20, default)).Items.Single().DeletedAt
            .Should().BeCloseTo(repEntry.DeletedAt, TimeSpan.FromSeconds(1));
        await service.RepPurgeAsync(quote.Id, f.RepUserId, default);
        (await service.RepGetTrashAsync(f.RepUserId, 1, 20, default)).Items.Should().BeEmpty();
        (await service.CustomerGetQuotationsAsync(customerUserId, 1, 20, null, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);
        f.Db.Quotations.Should().ContainSingle(x => x.Id == quote.Id);

        await service.CustomerSoftDeleteAsync(quote.Id, customerUserId, default);
        await service.CustomerPurgeAsync(quote.Id, customerUserId, default);
        (await service.CustomerGetTrashAsync(customerUserId, 1, 20, default)).Items.Should().BeEmpty();
        (await service.RepGetQuotationsAsync(f.RepUserId, 1, 20, null, default))
            .Items.Should().NotContain(x => x.Id == quote.Id);
        f.Db.Quotations.Should().ContainSingle(x => x.Id == quote.Id);
    }

    [Fact]
    public async Task Customer_order_manual_purge_hides_only_from_customer_and_keeps_order()
    {
        using var f = new OrderTrashFixture();
        var customerUserId = f.Db.CustomerProfiles.Single(c => c.Id == f.CustomerId).UserId;
        await f.Service.CustomerSoftDeleteAsync(f.OrderId, customerUserId);
        await f.Service.CustomerPurgeAsync(f.OrderId, customerUserId);

        (await f.Service.CustomerGetTrashAsync(customerUserId, 1, 20)).Items.Should().BeEmpty();
        (await f.Service.GetForRepAsync(f.RepUserId, new() { PageSize = 100 })).Items
            .Should().ContainSingle(x => x.Id == f.OrderId);
        f.Db.Orders.Should().ContainSingle(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Coordinator_manual_quotation_purge_hides_only_from_coordinator()
    {
        using var f = new OrderTrashFixture();
        var quote = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QT-COORD-PERSONAL-PURGE",
            CustomerId = f.CustomerId, RepId = f.RepId, CoordinatorId = f.CoordinatorId,
            Status = QuotationStatus.Draft
        };
        f.Db.Quotations.Add(quote);
        await f.Db.SaveChangesAsync();
        var service = new QuotationService(new UnitOfWork(f.Db),
            Mock.Of<INotificationService>(), Mock.Of<IEmailService>());

        await service.CoordinatorSoftDeleteAsync(quote.Id, f.CoordinatorUserId, default);
        await service.CoordinatorPurgeAsync(quote.Id, f.CoordinatorUserId, default);

        (await service.CoordinatorGetTrashAsync(f.CoordinatorUserId, 1, 20, default)).Items.Should().BeEmpty();
        (await service.RepGetQuotationsAsync(f.RepUserId, 1, 20, null, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);
        f.Db.Quotations.Should().ContainSingle(x => x.Id == quote.Id);
    }

    [Fact]
    public async Task Rep_quick_request_manual_purge_keeps_shared_record_and_coordinator_view()
    {
        using var f = new OrderTrashFixture();
        var service = new QuickRequestService(new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());
        await service.RepSoftDeleteAsync(f.QuickOrderId, f.RepUserId);
        var entry = f.Db.UserRecordTrashes.Single(x => x.UserId == f.RepUserId && x.RecordId == f.QuickOrderId);
        (await service.RepGetTrashAsync(f.RepUserId)).Single().DeletedAt
            .Should().BeCloseTo(entry.DeletedAt, TimeSpan.FromSeconds(1));

        await service.RepPurgeAsync(f.QuickOrderId, f.RepUserId);
        (await service.RepGetTrashAsync(f.RepUserId)).Should().BeEmpty();
        (await service.GetForCoordinatorAsync(f.CoordinatorUserId)).Should()
            .ContainSingle(x => x.Id == f.QuickOrderId);
        f.Db.QuickRequests.Should().ContainSingle(x => x.Id == f.QuickOrderId);
    }
}


public class AuditSafeAdminPurgeTests
{
    [Fact]
    public async Task Payment_report_expiry_retains_evidence_and_is_idempotent()
    {
        using var f = new OrderTrashFixture();
        var payment = new RepPayment
        {
            Id = Guid.NewGuid(),
            RepId = f.RepId,
            CustomerName = "Audit Payment",
            Amount = 250,
            ImageUrl = "rep-payments/retained-proof.jpg",
            IsDeletedByAdmin = true,
            AdminDeletedAt = DateTime.UtcNow.AddDays(-8)
        };
        f.Db.RepPayments.Add(payment);
        await f.Db.SaveChangesAsync();

        var cutoff = DateTime.UtcNow.AddDays(-7);
        (await TrashCleanupService.ExpireAdminTrashAsync(new UnitOfWork(f.Db), cutoff, default))
            .Should().Be(1);
        payment.IsPurgedByAdmin.Should().BeTrue();
        payment.AdminPurgedAt.Should().NotBeNull();
        f.Db.RepPayments.Single(p => p.Id == payment.Id).ImageUrl
            .Should().Be("rep-payments/retained-proof.jpg");

        (await TrashCleanupService.ExpireAdminTrashAsync(new UnitOfWork(f.Db), cutoff, default))
            .Should().Be(0);
    }

    [Fact]
    public async Task Admin_expiry_hides_old_records_from_trash_without_deleting_business_rows()
    {
        using var f = new OrderTrashFixture();
        var order = f.Db.Orders.Single(x => x.Id == f.OrderId);
        order.IsDeleted = true;
        order.DeletedAt = DateTime.UtcNow.AddDays(-8);

        var quick = f.Db.QuickRequests.Single(x => x.Id == f.QuickOrderId);
        quick.IsDeletedByAdmin = true;
        quick.AdminDeletedAt = DateTime.UtcNow.AddDays(-8);

        var linkedOrder = new Order
        {
            Id = Guid.NewGuid(), OrderNumber = "ORD-AUDIT-LINKED",
            CustomerId = f.CustomerId, RepId = f.RepId,
            Status = OrderStatus.Completed, TotalAmount = 400
        };
        var quote = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QT-AUDIT-LINKED",
            CustomerId = f.CustomerId, RepId = f.RepId, CoordinatorId = f.CoordinatorId,
            Status = QuotationStatus.ConvertedToOrder, ConvertedOrderId = linkedOrder.Id,
            IsDeleted = true, DeletedAt = DateTime.UtcNow.AddDays(-8)
        };
        f.Db.AddRange(linkedOrder, quote);
        await f.Db.SaveChangesAsync();

        var changed = await TrashCleanupService.ExpireAdminTrashAsync(
            new UnitOfWork(f.Db), DateTime.UtcNow.AddDays(-7), default);

        changed.Should().Be(3);
        order.IsPurgedByAdmin.Should().BeTrue();
        quick.IsPurgedByAdmin.Should().BeTrue();
        quote.IsPurgedByAdmin.Should().BeTrue();
        f.Db.Orders.Should().Contain(x => x.Id == f.OrderId);
        f.Db.QuickRequests.Should().Contain(x => x.Id == f.QuickOrderId);
        f.Db.Quotations.Should().Contain(x => x.Id == quote.Id);
        f.Db.Orders.Should().Contain(x => x.Id == linkedOrder.Id);
        f.Db.Quotations.Single(x => x.Id == quote.Id).ConvertedOrderId.Should().Be(linkedOrder.Id);

        var secondRun = await TrashCleanupService.ExpireAdminTrashAsync(
            new UnitOfWork(f.Db), DateTime.UtcNow.AddDays(-7), default);
        secondRun.Should().Be(0);
    }

    [Fact]
    public async Task Legacy_hard_delete_methods_are_disabled_and_leave_business_rows_intact()
    {
        using var f = new OrderTrashFixture();
        var orderDelete = async () => await f.Service.DeleteAsync(f.OrderId);
        await orderDelete.Should().ThrowAsync<BusinessException>();
        f.Db.Orders.Should().ContainSingle(x => x.Id == f.OrderId);

        var quick = new QuickRequestService(new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());
        var quickDelete = async () => await quick.DeleteAsync(f.QuickOrderId);
        await quickDelete.Should().ThrowAsync<BusinessException>();
        f.Db.QuickRequests.Should().ContainSingle(x => x.Id == f.QuickOrderId);
    }

    [Fact]
    public async Task Admin_manual_quotation_purge_removes_from_trash_but_retains_record()
    {
        using var f = new OrderTrashFixture();
        var quote = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QT-MANUAL-PURGE",
            CustomerId = f.CustomerId, RepId = f.RepId,
            CoordinatorId = f.CoordinatorId, Status = QuotationStatus.Draft,
            IsDeleted = true, DeletedAt = DateTime.UtcNow
        };
        f.Db.Quotations.Add(quote);
        await f.Db.SaveChangesAsync();
        var service = new QuotationService(new UnitOfWork(f.Db),
            Mock.Of<INotificationService>(), Mock.Of<IEmailService>());

        await service.AdminPurgeAsync(quote.Id, default);

        (await service.AdminGetTrashAsync(1, 20, default)).Items.Should().NotContain(x => x.Id == quote.Id);
        f.Db.Quotations.Should().ContainSingle(x => x.Id == quote.Id && x.IsDeleted && x.IsPurgedByAdmin);
        var restore = async () => await service.AdminRestoreAsync(quote.Id, default);
        await restore.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Admin_manual_quick_request_purge_retains_request_and_attachments()
    {
        using var f = new OrderTrashFixture();
        var request = f.Db.QuickRequests.Single(x => x.Id == f.QuickOrderId);
        request.IsDeletedByAdmin = true;
        request.AdminDeletedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        var service = new QuickRequestService(new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());

        await service.AdminPurgeAsync(f.QuickOrderId);

        (await service.AdminGetTrashAsync()).Should().NotContain(x => x.Id == f.QuickOrderId);
        f.Db.QuickRequests.Should().ContainSingle(x => x.Id == f.QuickOrderId && x.IsPurgedByAdmin);
    }
}


public class RepCustomerPersonalTrashIntegrationTests
{
    [Fact]
    public async Task Rep_and_customer_order_deletions_are_private_and_admin_still_sees_record()
    {
        using var f = new OrderTrashFixture();
        var customerUserId = f.Db.CustomerProfiles.Single(c => c.Id == f.CustomerId).UserId;
        var all = new DistributionSystem.Application.DTOs.Order.OrderFilterRequest { PageSize = 100 };

        await f.Service.RepSoftDeleteAsync(f.OrderId, f.RepUserId);
        (await f.Service.GetForRepAsync(f.RepUserId, all)).Items.Should().NotContain(x => x.Id == f.OrderId);
        var repTrashOrder = (await f.Service.RepGetTrashAsync(f.RepUserId, 1, 20)).Items.Should().ContainSingle(x => x.Id == f.OrderId).Subject;
        var repDeletedAt = f.Db.UserRecordTrashes.Single(x => x.UserId == f.RepUserId && x.RecordId == f.OrderId).DeletedAt;
        repTrashOrder.DeletedAt.Should().BeCloseTo(repDeletedAt, TimeSpan.FromSeconds(1));
        (await f.Service.GetForCustomerAsync(customerUserId, all)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        (await f.Service.GetAllAsync(new() { RepId = f.RepId })).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        var repDetail = async () => await f.Service.RepGetByIdAsync(f.OrderId, f.RepUserId);
        await repDetail.Should().ThrowAsync<NotFoundException>();

        await f.Service.RepRestoreAsync(f.OrderId, f.RepUserId);
        (await f.Service.GetForRepAsync(f.RepUserId, all)).Items.Should().ContainSingle(x => x.Id == f.OrderId);

        await f.Service.CustomerSoftDeleteAsync(f.OrderId, customerUserId);
        (await f.Service.GetForCustomerAsync(customerUserId, all)).Items.Should().NotContain(x => x.Id == f.OrderId);
        (await f.Service.CustomerGetTrashAsync(customerUserId, 1, 20)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        (await f.Service.GetForRepAsync(f.RepUserId, all)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
        var customerDetail = async () => await f.Service.CustomerGetByIdAsync(f.OrderId, customerUserId);
        await customerDetail.Should().ThrowAsync<NotFoundException>();
        (await f.Service.GetAllAsync(new() { CustomerId = f.CustomerId })).Items.Should().ContainSingle(x => x.Id == f.OrderId);

        await f.Service.CustomerRestoreAsync(f.OrderId, customerUserId);
        (await f.Service.GetForCustomerAsync(customerUserId, all)).Items.Should().ContainSingle(x => x.Id == f.OrderId);
    }

    [Fact]
    public async Task Rep_quick_request_delete_is_private_and_uses_the_common_trash_table()
    {
        using var f = new OrderTrashFixture();
        var quick = new QuickRequestService(new UnitOfWork(f.Db), Mock.Of<IFileStorageService>());

        await quick.RepSoftDeleteAsync(f.QuickOrderId, f.RepUserId);

        (await quick.GetRepRequestsAsync(f.RepUserId)).Should().NotContain(x => x.Id == f.QuickOrderId);
        var quickTrashItem = (await quick.RepGetTrashAsync(f.RepUserId)).Should().ContainSingle(x => x.Id == f.QuickOrderId).Subject;
        var quickDeletedAt = f.Db.UserRecordTrashes.Single(x => x.UserId == f.RepUserId && x.RecordId == f.QuickOrderId).DeletedAt;
        quickTrashItem.DeletedAt.Should().BeCloseTo(quickDeletedAt, TimeSpan.FromSeconds(1));
        f.Db.UserRecordTrashes.Should().ContainSingle(x =>
            x.UserId == f.RepUserId && x.RecordKind == PersonalRecordTrash.QuickRequestKind &&
            x.RecordId == f.QuickOrderId && x.PurgedAt == null);

        await quick.RepRestoreAsync(f.QuickOrderId, f.RepUserId);
        (await quick.GetRepRequestsAsync(f.RepUserId)).Should().ContainSingle(x => x.Id == f.QuickOrderId);
    }

    [Fact]
    public async Task Rep_and_customer_quotation_deletions_are_private()
    {
        using var f = new OrderTrashFixture();
        var customerUserId = f.Db.CustomerProfiles.Single(c => c.Id == f.CustomerId).UserId;
        var quote = new Quotation
        {
            Id = Guid.NewGuid(), QuotationNumber = "QT-PRIVATE-TRASH",
            CustomerId = f.CustomerId, RepId = f.RepId,
            CoordinatorId = f.CoordinatorId, Status = QuotationStatus.Draft
        };
        f.Db.Quotations.Add(quote);
        await f.Db.SaveChangesAsync();
        var service = new QuotationService(new UnitOfWork(f.Db),
            Mock.Of<INotificationService>(), Mock.Of<IEmailService>());

        await service.RepSoftDeleteAsync(quote.Id, f.RepUserId, default);
        (await service.RepGetQuotationsAsync(f.RepUserId, 1, 20, null, default))
            .Items.Should().NotContain(x => x.Id == quote.Id);
        (await service.RepGetTrashAsync(f.RepUserId, 1, 20, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);
        (await service.CustomerGetQuotationsAsync(customerUserId, 1, 20, null, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);
        (await service.CoordinatorGetQuotationsAsync(f.CoordinatorUserId, 1, 20, null, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);

        await service.RepRestoreAsync(quote.Id, f.RepUserId, default);
        await service.CustomerSoftDeleteAsync(quote.Id, customerUserId, default);
        var customerCancel = async () => await service.CustomerCancelQuotationAsync(
            customerUserId, quote.Id,
            new DistributionSystem.Application.DTOs.Quotation.CancelQuotationRequest(), default);
        await customerCancel.Should().ThrowAsync<NotFoundException>();
        (await service.CustomerGetQuotationsAsync(customerUserId, 1, 20, null, default))
            .Items.Should().NotContain(x => x.Id == quote.Id);
        (await service.CustomerGetTrashAsync(customerUserId, 1, 20, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);
        (await service.RepGetQuotationsAsync(f.RepUserId, 1, 20, null, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);
        var customerDetail = async () => await service.CustomerGetQuotationByIdAsync(customerUserId, quote.Id, default);
        await customerDetail.Should().ThrowAsync<NotFoundException>();

        await service.CustomerRestoreAsync(quote.Id, customerUserId, default);
        (await service.CustomerGetQuotationsAsync(customerUserId, 1, 20, null, default))
            .Items.Should().ContainSingle(x => x.Id == quote.Id);
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

        var orderDetail = async () => await f.Service.CoordinatorGetByIdAsync(f.OrderId, f.CoordinatorUserId);
        await orderDetail.Should().ThrowAsync<NotFoundException>();
        var quickAction = async () => await quick.CoordinatorUpdateStatusAsync(
            f.QuickOrderId, new DistributionSystem.Application.DTOs.QuickRequests.UpdateQuickRequestStatusDto { Status = "Approved" },
            f.CoordinatorUserId, default);
        await quickAction.Should().ThrowAsync<NotFoundException>();
        var quotationAction = async () => await quotations.ApproveQuotationAsync(
            f.CoordinatorUserId, quote.Id,
            new DistributionSystem.Application.DTOs.Quotation.ApproveQuotationRequest(), default);
        await quotationAction.Should().ThrowAsync<NotFoundException>();

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

        await quick.CoordinatorSoftDeleteAsync(f.QuickOrderId, f.CoordinatorUserId);
        await quick.CoordinatorPurgeAsync(f.QuickOrderId, f.CoordinatorUserId);
        (await quick.CoordinatorGetTrashAsync(f.CoordinatorUserId)).Should().BeEmpty();
        (await quick.GetForCoordinatorAsync(otherUserId)).Should().ContainSingle(x => x.Id == f.QuickOrderId);
        f.Db.QuickRequests.Should().ContainSingle(x => x.Id == f.QuickOrderId);

        await f.Service.CoordinatorGetByIdAsync(f.OrderId, f.CoordinatorUserId);
        (await f.Service.GetUnifiedCoordinatorAsync(f.CoordinatorUserId, all, false))
            .Items.Should().Contain(x => x.Id == f.OrderId && x.Kind == "Order");
        (await quick.GetForCoordinatorAsync(f.CoordinatorUserId)).Should()
            .NotContain(x => x.Id == f.QuickOrderId);
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
