using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.DTOs.QuickRequests;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DistributionSystem.UnitTests;

public class GuestOrderRepAssignmentTests
{
    [Fact]
    public async Task Assignment_EnforcesRolesAndTeamScope_AndPreservesGuestOrder()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var admin = new User { Role = UserRole.Admin, Username = "admin" };
        var coordinator = new User { Role = UserRole.SalesCoordinator, Username = "coord" };
        var otherCoordinator = new User { Role = UserRole.SalesCoordinator, Username = "other-coord" };
        var repUser = new User { Role = UserRole.SalesRep, Username = "rep-one" };
        var otherRepUser = new User { Role = UserRole.SalesRep, Username = "rep-two" };
        var inactiveUser = new User { Role = UserRole.SalesRep, Username = "inactive", IsActive = false };
        var team = new CoordinatorProfile { User = coordinator, UserId = coordinator.Id };
        var otherTeam = new CoordinatorProfile { User = otherCoordinator, UserId = otherCoordinator.Id };
        var rep = new SalesRepProfile { User = repUser, UserId = repUser.Id, FullName = "First Rep" };
        var otherRep = new SalesRepProfile { User = otherRepUser, UserId = otherRepUser.Id, FullName = "Second Rep" };
        var inactive = new SalesRepProfile { User = inactiveUser, UserId = inactiveUser.Id };
        var order = new QuickRequest { Type = QuickRequestType.Order, RequestNumber = "QO-TEST", CreatedBy = "Quick Order Portal", Details = "Original order", CustomerName = "Guest", Status = QuickRequestStatus.Pending };
        var ordinary = new QuickRequest { Type = QuickRequestType.Order, RequestNumber = "QO-REG", CreatedBy = "rep", RepId = rep.Id };
        db.AddRange(admin, coordinator, otherCoordinator, repUser, otherRepUser, inactiveUser, team, otherTeam, rep, otherRep, inactive, order, ordinary,
            new RepCoordinator { CoordinatorId = team.Id, RepId = rep.Id },
            new RepCoordinator { CoordinatorId = otherTeam.Id, RepId = otherRep.Id });
        await db.SaveChangesAsync();
        var publisher = new Mock<INotificationPublisher>();
        var service = new QuickRequestService(new UnitOfWork(db), Mock.Of<IFileStorageService>(), publisher.Object);
        AssignGuestOrderRepDto Change(Guid? target, Guid? expected = null) => new() { RepId = target, ExpectedRepId = expected };

        Assert.Equal(new[] { rep.Id }, (await service.GetGuestOrderRepOptionsAsync(order.Id, coordinator.Id)).Select(r => r.Id));
        Assert.Equal(2, (await service.GetGuestOrderRepOptionsAsync(order.Id, admin.Id)).Count);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AssignGuestOrderRepAsync(order.Id, coordinator.Id, Change(otherRep.Id), "coord"));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AssignGuestOrderRepAsync(order.Id, admin.Id, Change(inactive.Id), "admin"));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AssignGuestOrderRepAsync(order.Id, repUser.Id, Change(rep.Id), "rep"));
        await Assert.ThrowsAsync<NotFoundException>(() => service.AssignGuestOrderRepAsync(ordinary.Id, admin.Id, Change(otherRep.Id, rep.Id), "admin"));

        Assert.Empty(await db.Notifications.ToListAsync());
        var assigned = await service.AssignGuestOrderRepAsync(order.Id, coordinator.Id, Change(rep.Id), "coord");
        Assert.Equal(rep.Id, assigned.RepId);
        Assert.Equal("First Rep", assigned.RepName);
        var firstNotification = Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(repUser.Id, firstNotification.UserId);
        Assert.Equal("New Order Assigned", firstNotification.Title);
        Assert.False(firstNotification.IsRead);
        Assert.Contains(order.Id.ToString(), firstNotification.Metadata!);
        await service.AssignGuestOrderRepAsync(order.Id, coordinator.Id, Change(rep.Id, rep.Id), "coord");
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Contains(await service.GetRepRequestsAsync(repUser.Id), r => r.Id == order.Id);
        Assert.DoesNotContain(await service.GetForCoordinatorAsync(otherCoordinator.Id), r => r.Id == order.Id);
        var unified = new OrderService(new UnitOfWork(db));
        var filter = new DistributionSystem.Application.DTOs.Order.UnifiedOrderFilterRequest { PageSize = 100 };
        Assert.DoesNotContain((await unified.GetUnifiedCoordinatorAsync(otherCoordinator.Id, filter, false)).Items, r => r.Id == order.Id);
        Assert.Contains((await unified.GetUnifiedRepAsync(repUser.Id, filter, false)).Items, r => r.Id == order.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AssignGuestOrderRepAsync(order.Id, otherCoordinator.Id, Change(null, rep.Id), "other"));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.AssignGuestOrderRepAsync(order.Id, admin.Id, Change(otherRep.Id), "admin"));

        await service.AssignGuestOrderRepAsync(order.Id, admin.Id, Change(otherRep.Id, rep.Id), "admin");
        Assert.DoesNotContain(await service.GetRepRequestsAsync(repUser.Id), r => r.Id == order.Id);
        Assert.Contains(await service.GetRepRequestsAsync(otherRepUser.Id), r => r.Id == order.Id);
        var removed = await service.AssignGuestOrderRepAsync(order.Id, otherCoordinator.Id, Change(null, otherRep.Id), "other");
        Assert.Null(removed.RepId);
        Assert.Equal("Quick Order Portal", removed.CreatedBy);
        Assert.Equal("Original order", removed.Details);
        Assert.Equal("Pending", removed.Status);
        Assert.Contains(await service.GetForCoordinatorAsync(coordinator.Id), r => r.Id == order.Id);
        Assert.DoesNotContain(await service.GetRepRequestsAsync(otherRepUser.Id), r => r.Id == order.Id);
        var notifications = await db.Notifications.ToListAsync();
        Assert.Equal(4, notifications.Count);
        Assert.Equal(2, notifications.Count(n => n.UserId == repUser.Id));
        Assert.Equal(2, notifications.Count(n => n.UserId == otherRepUser.Id));
        Assert.All(notifications.Where(n => n.Title == "Order Assignment Removed"), n => Assert.DoesNotContain("quickRequestId", n.Metadata!));
        publisher.Verify(p => p.PublishToUserAsync(repUser.Id, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        publisher.Verify(p => p.PublishToUserAsync(otherRepUser.Id, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        publisher.Verify(p => p.PublishToUserAsync(It.IsAny<Guid>(), It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }
}
