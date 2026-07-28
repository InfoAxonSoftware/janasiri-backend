using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using DistributionSystem.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DistributionSystem.UnitTests;

public class FakeEmailService : IEmailService
{
    public Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendOrderNotificationToAdminAsync(string orderNumber, string customerInfo, decimal totalAmount, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class AuthServiceLockoutFixture : IDisposable
{
    public ApplicationDbContext Db { get; }
    public AuthService Service { get; }

    public Guid AdminUserId = Guid.NewGuid();
    public Guid LockedRepUserId = Guid.NewGuid();
    public Guid LockedRepProfileId = Guid.NewGuid();
    public Guid LockedCoordUserId = Guid.NewGuid();
    public Guid LockedCoordProfileId = Guid.NewGuid();
    public Guid UnlockedUserId = Guid.NewGuid();
    public Guid ExpiredLockUserId = Guid.NewGuid();

    public AuthServiceLockoutFixture()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new ApplicationDbContext(options);

        var uow = new UnitOfWork(Db);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "test-secret-key-that-is-long-enough-1234567890",
            })
            .Build();
        var jwtService = new JwtService(config);

        Service = new AuthService(uow, jwtService, config, new FakeEmailService(), NullLogger<AuthService>.Instance);

        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        Db.Users.AddRange(
            new User { Id = AdminUserId, Username = "admin", Email = "admin@test.com", Role = UserRole.Admin, IsActive = true },
            new User
            {
                Id = LockedRepUserId, Username = "repLocked", Email = "repLocked@test.com", Role = UserRole.SalesRep,
                IsActive = true, FailedLoginAttempts = 5, LockoutEnd = DateTime.UtcNow.AddMinutes(15)
            },
            new User
            {
                Id = LockedCoordUserId, Username = "coordLocked", Email = "coordLocked@test.com", Role = UserRole.SalesCoordinator,
                IsActive = true, FailedLoginAttempts = 7, LockoutEnd = DateTime.UtcNow.AddMinutes(30)
            },
            new User { Id = UnlockedUserId, Username = "repFine", Email = "repFine@test.com", Role = UserRole.SalesRep, IsActive = true },
            new User
            {
                Id = ExpiredLockUserId, Username = "repExpired", Email = "repExpired@test.com", Role = UserRole.SalesRep,
                IsActive = true, FailedLoginAttempts = 5, LockoutEnd = DateTime.UtcNow.AddMinutes(-5)
            }
        );

        Db.SalesRepProfiles.Add(new SalesRepProfile { Id = LockedRepProfileId, UserId = LockedRepUserId, FullName = "Locked Rep", EmployeeCode = "REP008" });
        Db.CoordinatorProfiles.Add(new CoordinatorProfile { Id = LockedCoordProfileId, UserId = LockedCoordUserId, FullName = "Locked Coordinator", EmployeeCode = "COORD01" });

        await Db.SaveChangesAsync();
    }

    public void Dispose() => Db.Dispose();
}

// Each test gets its own fresh in-memory database/fixture — several tests mutate lockout
// state (unlock), so a fixture shared across the whole class (xUnit's IClassFixture) would
// make test outcomes depend on execution order.
public class AuthServiceLockoutTests
{
    private readonly AuthServiceLockoutFixture _f = new();

    [Fact]
    public async Task GetLockedUsersAsync_ReturnsOnlyCurrentlyLockedUsers()
    {
        var result = await _f.Service.GetLockedUsersAsync(1, 20, null, null, null, null);

        result.TotalCount.Should().Be(2);
        result.Items.Select(i => i.Username).Should().BeEquivalentTo("repLocked", "coordLocked");
        result.Items.Select(i => i.Username).Should().NotContain("repFine");
        result.Items.Select(i => i.Username).Should().NotContain("repExpired");
    }

    [Fact]
    public async Task GetLockedUsersAsync_MapsSafeFieldsIncludingEmployeeCodeAndDisplayName()
    {
        // Search uses Npgsql's EF.Functions.ILike, which the in-memory provider cannot
        // translate — filtering by role instead exercises the same mapping logic without
        // requiring a real Postgres connection.
        var result = await _f.Service.GetLockedUsersAsync(1, 20, null, "SalesRep", null, null);

        var dto = result.Items.Single();
        dto.Username.Should().Be("repLocked");
        dto.DisplayName.Should().Be("Locked Rep");
        dto.EmployeeCode.Should().Be("REP008");
        dto.Role.Should().Be("SalesRep");
        dto.AccessFailedCount.Should().Be(5);
        dto.LockoutEnd.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task GetLockedUsersAsync_FiltersByRole()
    {
        var result = await _f.Service.GetLockedUsersAsync(1, 20, null, "SalesCoordinator", null, null);

        result.Items.Should().ContainSingle(i => i.Username == "coordLocked");
        result.Items.Should().NotContain(i => i.Username == "repLocked");
    }

    [Fact]
    public async Task AdminUnlockUserAsync_ResetsFailedCountAndClearsLockoutEnd_WithoutTouchingPasswordOrLockoutEnabled()
    {
        var passwordHashBefore = (await _f.Db.Users.FindAsync(_f.LockedRepUserId))!.PasswordHash;

        var result = await _f.Service.AdminUnlockUserAsync(_f.LockedRepUserId, _f.AdminUserId);

        result.WasLocked.Should().BeTrue();
        result.Username.Should().Be("repLocked");

        var user = await _f.Db.Users.AsNoTracking().FirstAsync(u => u.Id == _f.LockedRepUserId);
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
        user.PasswordHash.Should().Be(passwordHashBefore);
    }

    [Fact]
    public async Task AdminUnlockUserAsync_OnAlreadyUnlockedUser_ReturnsIdempotentResultWithoutError()
    {
        var result = await _f.Service.AdminUnlockUserAsync(_f.UnlockedUserId, _f.AdminUserId);

        result.WasLocked.Should().BeFalse();
        result.Message.Should().Contain("already unlocked");
    }

    [Fact]
    public async Task AdminUnlockUserAsync_OnExpiredLockout_TreatsAsAlreadyUnlocked()
    {
        var result = await _f.Service.AdminUnlockUserAsync(_f.ExpiredLockUserId, _f.AdminUserId);

        result.WasLocked.Should().BeFalse();
    }

    [Fact]
    public async Task AdminUnlockUserAsync_OnMissingUser_ThrowsNotFoundException()
    {
        var act = () => _f.Service.AdminUnlockUserAsync(Guid.NewGuid(), _f.AdminUserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
