using DistributionSystem.Application.DTOs.Payment;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using FluentAssertions;

namespace DistributionSystem.UnitTests;

public class PaymentAccessIsolationTests
{
    private static (Guid UserId, Guid ProfileId) AddSecondRep(OrderTrashFixture f)
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var user = new User
        {
            Id = userId, Username = "unassigned-rep", Role = UserRole.SalesRep
        };
        var rep = new SalesRepProfile
        {
            Id = profileId, UserId = userId, User = user,
            FullName = "Other Rep", EmployeeCode = "OTHER-REP"
        };
        f.Db.AddRange(user, rep);
        f.Db.SaveChanges();
        return (userId, profileId);
    }

    [Fact]
    public async Task Rep_cannot_read_unassigned_customer_ledger_even_with_known_customer_ID()
    {
        using var f = new OrderTrashFixture();
        var other = AddSecondRep(f);
        var service = new PaymentService(new UnitOfWork(f.Db));

        var unauthorized = async () =>
            await service.GetCustomerLedgerForRepAsync(f.CustomerId, other.UserId);
        await unauthorized.Should().ThrowAsync<NotFoundException>();

        var allowed = await service.GetCustomerLedgerForRepAsync(f.CustomerId, f.RepUserId);
        allowed.TotalOutstanding.Should().Be(100m);
        allowed.Entries.Should().ContainSingle(e => e.Type == "Invoice");
    }

    [Fact]
    public async Task Rep_cannot_create_payment_for_unassigned_customer()
    {
        using var f = new OrderTrashFixture();
        var other = AddSecondRep(f);
        var service = new PaymentService(new UnitOfWork(f.Db));

        var request = new CreatePaymentRequest
        {
            CustomerId = f.CustomerId,
            Amount = 20m,
            PaymentMethod = PaymentMethod.Cash
        };

        var unauthorized = async () => await service.CreateForRepAsync(request, other.UserId);
        await unauthorized.Should().ThrowAsync<NotFoundException>();
        f.Db.Payments.Should().BeEmpty();

        var paid = await service.CreateForRepAsync(request, f.RepUserId);
        paid.CustomerId.Should().Be(f.CustomerId);
        f.Db.Payments.Single().CollectedByRepId.Should().Be(f.RepId);
    }

    [Fact]
    public async Task Rep_cannot_link_payment_to_an_order_owned_by_different_customer()
    {
        using var f = new OrderTrashFixture();
        var service = new PaymentService(new UnitOfWork(f.Db));
        var anotherUser = new User { Username = "second-customer", Role = UserRole.Customer };
        var another = new CustomerProfile
        {
            UserId = anotherUser.Id, User = anotherUser,
            ShopName = "Second Shop", AssignedRepId = f.RepId
        };
        var anotherOrder = new Order
        {
            CustomerId = another.Id, Customer = another,
            OrderNumber = "OTHER-CUSTOMER-ORDER",
            RepId = f.RepId, Status = OrderStatus.Pending,
            TotalAmount = 500m
        };
        f.Db.AddRange(anotherUser, another, anotherOrder);
        await f.Db.SaveChangesAsync();

        var wrongLink = async () => await service.CreateForRepAsync(
            new CreatePaymentRequest
            {
                CustomerId = f.CustomerId,
                OrderId = anotherOrder.Id,
                Amount = 10m,
                PaymentMethod = PaymentMethod.Cash
            }, f.RepUserId);

        await wrongLink.Should().ThrowAsync<NotFoundException>();
        f.Db.Payments.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_order_globally_does_not_erase_accounting_ledger_history()
    {
        using var f = new OrderTrashFixture();
        var service = new PaymentService(new UnitOfWork(f.Db));

        await service.CreateForRepAsync(new CreatePaymentRequest
        {
            CustomerId = f.CustomerId,
            OrderId = f.OrderId,
            Amount = 30m,
            PaymentMethod = PaymentMethod.Cash
        }, f.RepUserId);

        var before = await service.GetCustomerLedgerForRepAsync(f.CustomerId, f.RepUserId);
        before.TotalOutstanding.Should().Be(70m);

        await f.Service.AdminSoftDeleteAsync(f.OrderId, "Admin");
        var after = await service.GetCustomerLedgerForRepAsync(f.CustomerId, f.RepUserId);
        after.TotalOutstanding.Should().Be(70m);
        after.Entries.Should().HaveCount(2);
        f.Db.Payments.Should().ContainSingle();
    }

    [Fact]
    public async Task Negative_and_zero_payment_amounts_are_rejected_before_database_write()
    {
        using var f = new OrderTrashFixture();
        var service = new PaymentService(new UnitOfWork(f.Db));
        foreach (var amount in new[] { 0m, -1m })
        {
            var request = new CreatePaymentRequest
            {
                CustomerId = f.CustomerId, Amount = amount, PaymentMethod = PaymentMethod.Cash
            };
            var create = async () => await service.CreateForRepAsync(request, f.RepUserId);
            await create.Should().ThrowAsync<BusinessException>();
        }
        f.Db.Payments.Should().BeEmpty();
    }

    [Fact]
    public async Task Reassignment_immediately_revokes_previous_Rep_ledger_access()
    {
        using var f = new OrderTrashFixture();
        var other = AddSecondRep(f);
        var service = new PaymentService(new UnitOfWork(f.Db));
        (await service.GetCustomerLedgerForRepAsync(f.CustomerId, f.RepUserId))
            .TotalOutstanding.Should().Be(100m);

        f.Db.CustomerProfiles.Single(x => x.Id == f.CustomerId).AssignedRepId = other.ProfileId;
        await f.Db.SaveChangesAsync();

        var formerRep = async () =>
            await service.GetCustomerLedgerForRepAsync(f.CustomerId, f.RepUserId);
        await formerRep.Should().ThrowAsync<NotFoundException>();
        (await service.GetCustomerLedgerForRepAsync(f.CustomerId, other.UserId))
            .TotalOutstanding.Should().Be(100m);
    }
}
