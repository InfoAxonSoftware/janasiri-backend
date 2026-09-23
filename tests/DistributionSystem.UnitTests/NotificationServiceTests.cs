using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DistributionSystem.UnitTests;

public class NotificationServiceTests
{
    private static (ApplicationDbContext Db, NotificationService Service, FakeNotificationPublisher Publisher) CreateSut()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);
        var publisher = new FakeNotificationPublisher();
        return (db, new NotificationService(new UnitOfWork(db), publisher), publisher);
    }

    [Fact]
    public async Task MarkAsReadAsync_AllowsTheNotificationOwner()
    {
        var (db, service, _) = CreateSut();
        var ownerId = Guid.NewGuid();
        var notification = new Notification { UserId = ownerId, Title = "Owner", Message = "Test" };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        await service.MarkAsReadAsync(notification.Id, ownerId);

        var updated = await db.Notifications.FindAsync(notification.Id);
        updated!.IsRead.Should().BeTrue();
        updated.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkAsReadAsync_RejectsAnotherUsersNotification()
    {
        var (db, service, _) = CreateSut();
        var ownerId = Guid.NewGuid();
        var notification = new Notification { UserId = ownerId, Title = "Private", Message = "Test" };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var act = async () => await service.MarkAsReadAsync(notification.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
        (await db.Notifications.FindAsync(notification.Id))!.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task SendToUserAsync_PersistsAndPublishesExactlyOnce_WithStructuredMetadata()
    {
        var (db, service, publisher) = CreateSut();
        var recipientId = Guid.NewGuid();
        db.Users.Add(new User { Id = recipientId, Username = "recipient", Email = "recipient@test.com", Role = UserRole.Customer, IsActive = true });
        await db.SaveChangesAsync();

        var dto = await service.SendToUserAsync(new SendNotificationRequest
        {
            UserId = recipientId,
            Title = "Direct",
            Message = "Message",
            Metadata = "{\"orderId\":\"order-123\",\"actorName\":\"Alice\"}"
        });

        (await db.Notifications.CountAsync(n => n.UserId == recipientId)).Should().Be(1);
        publisher.UserPushes.Should().ContainSingle();
        publisher.UserPushes[0].UserId.Should().Be(recipientId);
        dto.Metadata!.Value.GetProperty("orderId").GetString().Should().Be("order-123");
        publisher.UserPushes[0].Notification.Metadata!.Value.GetProperty("actorName").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task SendToRoleAsync_PersistsAndPublishesExactlyOncePerActiveRecipient()
    {
        var (db, service, publisher) = CreateSut();
        var adminA = new User { Id = Guid.NewGuid(), Username = "admin-a", Email = "admin-a@test.com", Role = UserRole.Admin, IsActive = true };
        var adminB = new User { Id = Guid.NewGuid(), Username = "admin-b", Email = "admin-b@test.com", Role = UserRole.Admin, IsActive = true };
        var inactiveAdmin = new User { Id = Guid.NewGuid(), Username = "admin-c", Email = "admin-c@test.com", Role = UserRole.Admin, IsActive = false };
        db.Users.AddRange(adminA, adminB, inactiveAdmin);
        await db.SaveChangesAsync();

        await service.SendToRoleAsync(new BroadcastNotificationRequest
        {
            Role = "Admin",
            Title = "Broadcast",
            Message = "Message",
            Metadata = "{\"customerName\":\"Acme\"}"
        });

        var notifications = await db.Notifications.OrderBy(n => n.UserId).ToListAsync();
        notifications.Should().HaveCount(2);
        notifications.Select(n => n.UserId).Should().BeEquivalentTo([adminA.Id, adminB.Id]);
        publisher.UserPushes.Should().HaveCount(2);
        publisher.UserPushes.Select(push => push.UserId).Should().BeEquivalentTo([adminA.Id, adminB.Id]);
        publisher.UserPushes.Should().OnlyContain(push => push.Notification.Metadata!.Value.GetProperty("customerName").GetString() == "Acme");
    }

    [Fact]
    public async Task GetByUserAsync_OrdersEqualTimestampsByIdDescending()
    {
        var (db, service, _) = CreateSut();
        var userId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
        var first = new Notification { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), UserId = userId, Title = "First", Message = "Test" };
        var second = new Notification { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), UserId = userId, Title = "Second", Message = "Test" };
        db.Notifications.AddRange(first, second);
        await db.SaveChangesAsync();
        first.CreatedAt = timestamp;
        second.CreatedAt = timestamp;
        await db.SaveChangesAsync();

        var result = await service.GetByUserAsync(userId, 1, 20);

        result.Items.Select(n => n.Id).Should().ContainInOrder(second.Id, first.Id);
    }

    [Fact]
    public async Task GetByUserAsync_ReturnsOnlyTheAuthenticatedUsersNotifications_ForEveryRole()
    {
        var (db, service, _) = CreateSut();
        var users = new[]
        {
            (Id: Guid.NewGuid(), Role: UserRole.Admin),
            (Id: Guid.NewGuid(), Role: UserRole.SalesCoordinator),
            (Id: Guid.NewGuid(), Role: UserRole.SalesRep),
            (Id: Guid.NewGuid(), Role: UserRole.Customer),
        };

        db.Notifications.AddRange(users.Select((user, index) => new Notification
        {
            UserId = user.Id,
            NotificationType = NotificationType.General,
            Title = user.Role.ToString(),
            Message = $"Notification {index}",
        }));
        await db.SaveChangesAsync();

        foreach (var user in users)
        {
            var result = await service.GetByUserAsync(user.Id, 1, 30);
            result.Items.Should().ContainSingle(notification => notification.Title == user.Role.ToString());
        }
    }

    [Fact]
    public async Task SendBroadcastAsync_ToAllUsers_PersistsAndPublishesOncePerActiveUser()
    {
        var (db, service, publisher) = CreateSut();
        var activeUsers = new[]
        {
            new User { Id = Guid.NewGuid(), Username = "admin", Email = "admin@test.com", Role = UserRole.Admin, IsActive = true },
            new User { Id = Guid.NewGuid(), Username = "coordinator", Email = "coordinator@test.com", Role = UserRole.SalesCoordinator, IsActive = true },
            new User { Id = Guid.NewGuid(), Username = "rep", Email = "rep@test.com", Role = UserRole.SalesRep, IsActive = true },
            new User { Id = Guid.NewGuid(), Username = "customer", Email = "customer@test.com", Role = UserRole.Customer, IsActive = true },
        };
        var inactiveUser = new User { Id = Guid.NewGuid(), Username = "inactive", Email = "inactive@test.com", Role = UserRole.Admin, IsActive = false };
        db.Users.AddRange(activeUsers.Append(inactiveUser));
        await db.SaveChangesAsync();

        await service.SendBroadcastAsync(new BroadcastNotificationRequest { SendToAll = true, Title = "All", Message = "Everyone" });

        var recipientIds = await db.Notifications.Select(notification => notification.UserId).ToListAsync();
        recipientIds.Should().BeEquivalentTo(activeUsers.Select(user => user.Id));
        publisher.UserPushes.Select(push => push.UserId).Should().BeEquivalentTo(activeUsers.Select(user => user.Id));
        publisher.UserPushes.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.SalesCoordinator)]
    [InlineData(UserRole.SalesRep)]
    [InlineData(UserRole.Customer)]
    public async Task SendToRoleAsync_TargetsOnlyTheRequestedActiveRole(UserRole targetRole)
    {
        var (db, service, publisher) = CreateSut();
        var target = new User { Id = Guid.NewGuid(), Username = "target", Email = "target@test.com", Role = targetRole, IsActive = true };
        var other = new User { Id = Guid.NewGuid(), Username = "other", Email = "other@test.com", Role = targetRole == UserRole.Customer ? UserRole.Admin : UserRole.Customer, IsActive = true };
        var inactiveTarget = new User { Id = Guid.NewGuid(), Username = "inactive", Email = "inactive@test.com", Role = targetRole, IsActive = false };
        db.Users.AddRange(target, other, inactiveTarget);
        await db.SaveChangesAsync();

        await service.SendToRoleAsync(new BroadcastNotificationRequest { Role = targetRole.ToString(), Title = "Role", Message = "Only role" });

        (await db.Notifications.Select(notification => notification.UserId).ToListAsync()).Should().BeEquivalentTo([target.Id]);
        publisher.UserPushes.Select(push => push.UserId).Should().BeEquivalentTo([target.Id]);
    }

    [Fact]
    public async Task GetActiveRecipientsAsync_IncludesActiveCoordinatorsAndExcludesInactiveUsers()
    {
        var (db, service, _) = CreateSut();
        var coordinator = new User { Id = Guid.NewGuid(), Username = "coordinator", Email = "coordinator@test.com", Role = UserRole.SalesCoordinator, IsActive = true };
        var inactiveCoordinator = new User { Id = Guid.NewGuid(), Username = "inactive", Email = "inactive@test.com", Role = UserRole.SalesCoordinator, IsActive = false };
        db.Users.AddRange(coordinator, inactiveCoordinator);
        await db.SaveChangesAsync();

        var recipients = await service.GetActiveRecipientsAsync();

        recipients.Should().ContainSingle(recipient => recipient.Id == coordinator.Id && recipient.Role == "SalesCoordinator");
        recipients.Should().NotContain(recipient => recipient.Id == inactiveCoordinator.Id);
    }

    [Theory]
    [InlineData(true, "Admin", true)]
    [InlineData(false, "", false)]
    [InlineData(false, "NotARole", false)]
    public async Task SendBroadcastAsync_RejectsContradictoryOrInvalidTargets(bool sendToAll, string role, bool includeUserIds)
    {
        var (_, service, _) = CreateSut();
        var request = new BroadcastNotificationRequest
        {
            SendToAll = sendToAll,
            Role = role,
            UserIds = includeUserIds ? [Guid.NewGuid()] : [],
            Title = "Invalid",
            Message = "Invalid"
        };

        var act = async () => await service.SendBroadcastAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendBroadcastAsync_CanTargetASelectedActiveCoordinator()
    {
        var (db, service, publisher) = CreateSut();
        var coordinator = new User { Id = Guid.NewGuid(), Username = "coordinator", Email = "coordinator@test.com", Role = UserRole.SalesCoordinator, IsActive = true };
        db.Users.Add(coordinator);
        await db.SaveChangesAsync();

        await service.SendBroadcastAsync(new BroadcastNotificationRequest { UserIds = [coordinator.Id], Title = "Direct", Message = "Coordinator" });

        (await db.Notifications.Select(notification => notification.UserId).ToListAsync()).Should().BeEquivalentTo([coordinator.Id]);
        publisher.UserPushes.Should().ContainSingle(push => push.UserId == coordinator.Id);
    }
}
