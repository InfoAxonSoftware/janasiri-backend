using DistributionSystem.Application.Configuration;
using DistributionSystem.Application.DTOs.Notification;
using DistributionSystem.Application.DTOs.RepPayments;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using DistributionSystem.Infrastructure.Data.Repositories.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DistributionSystem.UnitTests;

/// <summary>Captures every publish call instead of touching SignalR, so tests can assert exact recipients.</summary>
public class FakeNotificationPublisher : INotificationPublisher
{
    public List<(Guid UserId, NotificationDto Notification)> UserPushes { get; } = [];
    public List<PaymentReportEventDto> Events { get; } = [];
    public List<Guid> LastEventTargets { get; private set; } = [];

    public Task PublishToUserAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default)
    {
        UserPushes.Add((userId, notification));
        return Task.CompletedTask;
    }

    public Task PublishToRoleAsync(string role, object payload, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PublishPaymentReportEventAsync(PaymentReportEventDto evt, IEnumerable<Guid> targetUserIds, CancellationToken cancellationToken = default)
    {
        Events.Add(evt);
        LastEventTargets = targetUserIds.ToList();
        return Task.CompletedTask;
    }
}

public class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
}

public class RepPaymentServiceFixture : IDisposable
{
    public ApplicationDbContext Db { get; }

    // A second DbContext pointed at the same in-memory database, used only for test
    // assertions/polling. RepPaymentService dispatches notifications on a fire-and-forget
    // background task that shares the *same* DbContext as the main flow (mirroring the
    // pre-existing production pattern) — DbContext is not thread-safe, so reading from that
    // same instance concurrently from the test thread would intermittently race with it.
    public ApplicationDbContext ReadDb { get; }

    public IUnitOfWork Uow { get; }
    public FakeNotificationPublisher Publisher { get; } = new();
    public NotificationService Notifications { get; }
    public RepPaymentService Service { get; }
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "distsys-tests", Guid.NewGuid().ToString("N"));

    public Guid AdminUserId = Guid.NewGuid();
    public Guid SuperAdminUserId = Guid.NewGuid();
    public Guid CoordinatorAUserId = Guid.NewGuid();
    public Guid CoordinatorBUserId = Guid.NewGuid();
    public Guid RepAUserId = Guid.NewGuid();
    public Guid RepBUserId = Guid.NewGuid();
    public Guid RepAProfileId = Guid.NewGuid();
    public Guid RepBProfileId = Guid.NewGuid();
    public Guid CoordAProfileId = Guid.NewGuid();
    public Guid CoordBProfileId = Guid.NewGuid();

    public RepPaymentServiceFixture()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        Db = new ApplicationDbContext(options);
        ReadDb = new ApplicationDbContext(options);
        Uow = new UnitOfWork(Db);
        Notifications = new NotificationService(Uow, Publisher);
        var env = new FakeWebHostEnvironment();
        var fileStorage = new PhysicalFileStorageService(
            Options.Create(new FileStorageOptions { RootPath = _storageRoot }),
            env,
            NullLogger<PhysicalFileStorageService>.Instance);
        Service = new RepPaymentService(Uow, env, Notifications, Publisher, NullLogger<RepPaymentService>.Instance, fileStorage);

        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        Db.Users.AddRange(
            new User { Id = AdminUserId, Username = "admin", Email = "admin@test.com", Role = UserRole.Admin, IsActive = true },
            new User { Id = SuperAdminUserId, Username = "super", Email = "super@test.com", Role = UserRole.SuperAdmin, IsActive = true },
            new User { Id = CoordinatorAUserId, Username = "coordA", Email = "coordA@test.com", Role = UserRole.SalesCoordinator, IsActive = true },
            new User { Id = CoordinatorBUserId, Username = "coordB", Email = "coordB@test.com", Role = UserRole.SalesCoordinator, IsActive = true },
            new User { Id = RepAUserId, Username = "repA", Email = "repA@test.com", Role = UserRole.SalesRep, IsActive = true },
            new User { Id = RepBUserId, Username = "repB", Email = "repB@test.com", Role = UserRole.SalesRep, IsActive = true }
        );

        Db.CoordinatorProfiles.AddRange(
            new CoordinatorProfile { Id = CoordAProfileId, UserId = CoordinatorAUserId, FullName = "Coordinator A", EmployeeCode = "CA1" },
            new CoordinatorProfile { Id = CoordBProfileId, UserId = CoordinatorBUserId, FullName = "Coordinator B", EmployeeCode = "CB1" }
        );

        Db.SalesRepProfiles.AddRange(
            new SalesRepProfile { Id = RepAProfileId, UserId = RepAUserId, FullName = "Rep A", EmployeeCode = "RA1" },
            new SalesRepProfile { Id = RepBProfileId, UserId = RepBUserId, FullName = "Rep B", EmployeeCode = "RB1" }
        );

        // Only Rep A is assigned to Coordinator A. Rep B has no coordinator assigned.
        Db.Set<RepCoordinator>().Add(new RepCoordinator { RepId = RepAProfileId, CoordinatorId = CoordAProfileId });

        await Db.SaveChangesAsync();
    }

    public void Dispose()
    {
        Db.Dispose();
        ReadDb.Dispose();
        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

    public static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs) return false;
            await Task.Delay(25);
        }
        return true;
    }

    /// <summary>
    /// Submission notifications run on a fire-and-forget background task against the same
    /// (non-thread-safe) DbContext. Tests must wait for that task to finish writing before
    /// issuing the next operation, or EF Core's concurrency detector throws.
    /// </summary>
    public async Task<RepPaymentDto> CreateAndSettleAsync(Guid repUserId, string customerName, decimal amount, int expectedNotifications)
    {
        var result = await Service.CreateAsync(repUserId, new CreateRepPaymentDto { CustomerName = customerName, ReferenceNumber = "REF-0001", Amount = amount }, null);
        await WaitUntilAsync(() => ReadDb.Set<Notification>().AsNoTracking().Count(n => n.Metadata != null && n.Metadata.Contains(result.Id.ToString())) >= expectedNotifications);
        return result;
    }
}

public class RepPaymentReferenceNumberTests : IClassFixture<RepPaymentServiceFixture>
{
    private readonly RepPaymentServiceFixture _f;
    public RepPaymentReferenceNumberTests(RepPaymentServiceFixture f) => _f = f;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithMissingReferenceNumber_ThrowsArgumentException(string? referenceNumber)
    {
        var act = async () => await _f.Service.CreateAsync(
            _f.RepBUserId,
            new CreateRepPaymentDto { CustomerName = "Ref Test Co", ReferenceNumber = referenceNumber!, Amount = 100 },
            null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Create_WithReferenceNumberOver100Chars_ThrowsArgumentException()
    {
        var act = async () => await _f.Service.CreateAsync(
            _f.RepBUserId,
            new CreateRepPaymentDto { CustomerName = "Ref Test Co", ReferenceNumber = new string('A', 101), Amount = 100 },
            null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Create_TrimsReferenceNumberWhitespace()
    {
        var result = await _f.Service.CreateAsync(
            _f.RepBUserId,
            new CreateRepPaymentDto { CustomerName = "Ref Trim Co", ReferenceNumber = "  REF-42  ", Amount = 100 },
            null);

        result.ReferenceNumber.Should().Be("REF-42");
    }
}

public class RepPaymentSubmissionNotificationTests : IClassFixture<RepPaymentServiceFixture>
{
    private readonly RepPaymentServiceFixture _f;
    public RepPaymentSubmissionNotificationTests(RepPaymentServiceFixture f) => _f = f;

    [Fact]
    public async Task Submission_Notifies_ActiveAdminsSuperAdminsAndAssignedCoordinator_ButNotUnrelatedCoordinator()
    {
        var result = await _f.CreateAndSettleAsync(_f.RepAUserId, "Acme Store", 1500, expectedNotifications: 3);

        var recipientIds = _f.ReadDb.Set<Notification>()
            .Where(n => n.Metadata != null && n.Metadata.Contains(result.Id.ToString()))
            .Select(n => n.UserId)
            .ToList();

        recipientIds.Should().Contain(_f.AdminUserId);
        recipientIds.Should().Contain(_f.SuperAdminUserId);
        recipientIds.Should().Contain(_f.CoordinatorAUserId);
        recipientIds.Should().NotContain(_f.CoordinatorBUserId);
    }

    [Fact]
    public async Task Submission_WithNoAssignedCoordinator_StillNotifiesAdmins_AndDoesNotThrow()
    {
        var result = await _f.CreateAndSettleAsync(_f.RepBUserId, "Unassigned Customer", 500, expectedNotifications: 2);

        var recipientIds = _f.ReadDb.Set<Notification>()
            .Where(n => n.Metadata != null && n.Metadata.Contains(result.Id.ToString()))
            .Select(n => n.UserId)
            .ToList();

        recipientIds.Should().Contain(_f.AdminUserId);
        recipientIds.Should().Contain(_f.SuperAdminUserId);
        recipientIds.Should().NotContain(_f.CoordinatorAUserId);
        recipientIds.Should().NotContain(_f.CoordinatorBUserId);
    }
}

public class RepPaymentStatusChangeTests : IClassFixture<RepPaymentServiceFixture>
{
    private readonly RepPaymentServiceFixture _f;
    public RepPaymentStatusChangeTests(RepPaymentServiceFixture f) => _f = f;

    private async Task<RepPaymentDto> SeedPaymentAsync(Guid repUserId, string customer = "Status Test Co")
    {
        // Rep A is assigned to Coordinator A (3 notifications); any other rep has none assigned (2).
        var expected = repUserId == _f.RepAUserId ? 3 : 2;
        return await _f.CreateAndSettleAsync(repUserId, customer, 1000, expected);
    }

    [Fact]
    public async Task ActualStatusChange_CreatesExactlyOneNotification_ForSubmittingRep()
    {
        var payment = await SeedPaymentAsync(_f.RepAUserId, "Status Change Co");

        var updated = await _f.Service.UpdateStatusAsync(payment.Id, new UpdateRepPaymentStatusDto { Status = "Confirmed" }, _f.AdminUserId, "Admin User");
        updated.Status.Should().Be("Confirmed");

        await RepPaymentServiceFixture.WaitUntilAsync(() =>
            _f.ReadDb.Set<Notification>().Count(n => n.NotificationType == NotificationType.PaymentReportStatusChanged
                && n.Metadata != null && n.Metadata.Contains(payment.Id.ToString())) >= 1);

        var count = _f.ReadDb.Set<Notification>().Count(n => n.NotificationType == NotificationType.PaymentReportStatusChanged
            && n.Metadata != null && n.Metadata.Contains(payment.Id.ToString()) && n.UserId == _f.RepAUserId);

        count.Should().Be(1);
    }

    [Fact]
    public async Task SameStatus_DoesNotCreateStatusChangeNotification()
    {
        var payment = await SeedPaymentAsync(_f.RepAUserId, "Same Status Co");

        await _f.Service.UpdateStatusAsync(payment.Id, new UpdateRepPaymentStatusDto { Status = payment.Status }, _f.AdminUserId, "Admin User");

        await Task.Delay(300); // give any (incorrectly fired) background task a chance to run

        var count = _f.ReadDb.Set<Notification>().Count(n => n.NotificationType == NotificationType.PaymentReportStatusChanged
            && n.Metadata != null && n.Metadata.Contains(payment.Id.ToString()));

        count.Should().Be(0);
    }

    [Fact]
    public async Task UnrelatedCoordinator_CannotUpdateStatus_ForRepNotAssignedToThem()
    {
        var payment = await SeedPaymentAsync(_f.RepAUserId, "Ownership Co");

        var act = async () => await _f.Service.CoordinatorUpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Confirmed" }, _f.CoordinatorBUserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AssignedCoordinator_CanUpdateStatus_ForOwnRep()
    {
        var payment = await SeedPaymentAsync(_f.RepAUserId, "Assigned Co");

        var updated = await _f.Service.CoordinatorUpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Confirmed" }, _f.CoordinatorAUserId);

        updated.Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task InvalidStatusValue_IsRejected()
    {
        var payment = await SeedPaymentAsync(_f.RepAUserId, "Invalid Status Co");

        var act = async () => await _f.Service.UpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "NotARealStatus" }, _f.AdminUserId, "Admin User");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

public class RepPaymentApprovedStatusTests : IClassFixture<RepPaymentServiceFixture>
{
    private readonly RepPaymentServiceFixture _f;
    public RepPaymentApprovedStatusTests(RepPaymentServiceFixture f) => _f = f;

    private async Task<RepPaymentDto> SeedConfirmedPaymentAsync(Guid repUserId, string customer)
    {
        var expected = repUserId == _f.RepAUserId ? 3 : 2;
        var payment = await _f.CreateAndSettleAsync(repUserId, customer, 750, expected);

        return await _f.Service.UpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Confirmed" }, _f.AdminUserId, "Admin User");
    }

    [Fact]
    public async Task Admin_CanChange_ConfirmedPayment_ToApproved()
    {
        var payment = await SeedConfirmedPaymentAsync(_f.RepAUserId, "Approve Co (Admin)");

        var updated = await _f.Service.UpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Approved" }, _f.AdminUserId, "Admin User");

        updated.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task AssignedCoordinator_CanChange_ConfirmedPayment_ToApproved_WithinScope()
    {
        var payment = await SeedConfirmedPaymentAsync(_f.RepAUserId, "Approve Co (Coordinator)");

        var updated = await _f.Service.CoordinatorUpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Approved" }, _f.CoordinatorAUserId);

        updated.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task UnrelatedCoordinator_CannotSetApproved_ForRepNotAssignedToThem()
    {
        var payment = await SeedConfirmedPaymentAsync(_f.RepAUserId, "Approve Co (Unauthorized)");

        var act = async () => await _f.Service.CoordinatorUpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Approved" }, _f.CoordinatorBUserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Approved_IsMappedIntoDtoResponses()
    {
        var payment = await SeedConfirmedPaymentAsync(_f.RepAUserId, "Approve Co (DTO Mapping)");

        var updated = await _f.Service.UpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Approved" }, _f.AdminUserId, "Admin User");

        updated.Status.Should().Be("Approved");

        var fetched = await _f.Service.GetByIdForAdminAsync(payment.Id);
        fetched.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task StatusFilter_Approved_ReturnsOnlyApprovedRecords()
    {
        var approved = await SeedConfirmedPaymentAsync(_f.RepAUserId, "Approve Co (Filter Match)");
        await _f.Service.UpdateStatusAsync(
            approved.Id, new UpdateRepPaymentStatusDto { Status = "Approved" }, _f.AdminUserId, "Admin User");

        var confirmedOnly = await SeedConfirmedPaymentAsync(_f.RepAUserId, "Approve Co (Filter NonMatch)");

        var filter = new RepPaymentQueryDto { Page = 1, PageSize = 500, Status = "Approved" };
        var page = await _f.Service.GetAllAsync(filter, trash: false);

        page.Items.Should().Contain(p => p.Id == approved.Id);
        page.Items.Should().NotContain(p => p.Id == confirmedOnly.Id);
        page.Items.Should().OnlyContain(p => p.Status == "Approved");
    }

    [Fact]
    public async Task StatusChange_ToApproved_PublishesEventWithNewStatusApproved()
    {
        var payment = await SeedConfirmedPaymentAsync(_f.RepAUserId, "Approve Co (SignalR)");

        await _f.Service.UpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Approved" }, _f.AdminUserId, "Admin User");

        await RepPaymentServiceFixture.WaitUntilAsync(() =>
            _f.Publisher.Events.Any(e => e.ReportId == payment.Id && e.NewStatus == "Approved"));

        _f.Publisher.Events.Should().Contain(e => e.ReportId == payment.Id && e.NewStatus == "Approved");

        await RepPaymentServiceFixture.WaitUntilAsync(() =>
            _f.ReadDb.Set<Notification>().Count(n => n.NotificationType == NotificationType.PaymentReportStatusChanged
                && n.Metadata != null && n.Metadata.Contains(payment.Id.ToString()) && n.UserId == _f.RepAUserId) >= 1);
    }

    [Fact]
    public async Task Approved_CanStillBeChanged_BecauseExistingArchitectureAllowsAnyTransition()
    {
        // The service has no from-state transition whitelist for any status today, so
        // Approved does not introduce a hard "final" lock — it simply follows the same
        // permissive rule every other status already follows.
        var payment = await SeedConfirmedPaymentAsync(_f.RepAUserId, "Approve Co (Reopen)");

        await _f.Service.UpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Approved" }, _f.AdminUserId, "Admin User");

        var reverted = await _f.Service.UpdateStatusAsync(
            payment.Id, new UpdateRepPaymentStatusDto { Status = "Rejected" }, _f.AdminUserId, "Admin User");

        reverted.Status.Should().Be("Rejected");
    }
}

public class RepPaymentTrashIndependenceTests : IClassFixture<RepPaymentServiceFixture>
{
    private readonly RepPaymentServiceFixture _f;
    public RepPaymentTrashIndependenceTests(RepPaymentServiceFixture f) => _f = f;

    [Fact]
    public async Task AdminTrash_DoesNotAffect_CoordinatorOrRepVisibility()
    {
        var payment = await _f.CreateAndSettleAsync(_f.RepAUserId, "Trash Co", 250, expectedNotifications: 3);

        await _f.Service.AdminSoftDeleteAsync(payment.Id, "Admin User");

        var adminActive = await _f.Service.GetAllAsync();
        adminActive.Should().NotContain(p => p.Id == payment.Id);

        var coordinatorActive = await _f.Service.GetForCoordinatorAsync(_f.CoordinatorAUserId, trash: false);
        coordinatorActive.Should().Contain(p => p.Id == payment.Id);

        var repActive = await _f.Service.GetRepPaymentsAsync(_f.RepAUserId, trash: false);
        repActive.Should().Contain(p => p.Id == payment.Id);

        // Trashing must not alter status or delete evidence metadata.
        var reloaded = coordinatorActive.First(p => p.Id == payment.Id);
        reloaded.Status.Should().Be("AwaitingConfirmation");
    }

    [Fact]
    public async Task CoordinatorTrash_DoesNotAffect_AdminOrRepVisibility()
    {
        var payment = await _f.CreateAndSettleAsync(_f.RepAUserId, "Coord Trash Co", 300, expectedNotifications: 3);

        await _f.Service.CoordinatorTrashAsync(payment.Id, _f.CoordinatorAUserId);

        var coordinatorActive = await _f.Service.GetForCoordinatorAsync(_f.CoordinatorAUserId, trash: false);
        coordinatorActive.Should().NotContain(p => p.Id == payment.Id);

        var coordinatorTrash = await _f.Service.GetForCoordinatorAsync(_f.CoordinatorAUserId, trash: true);
        coordinatorTrash.Should().Contain(p => p.Id == payment.Id);

        var adminActive = await _f.Service.GetAllAsync();
        adminActive.Should().Contain(p => p.Id == payment.Id);

        var repActive = await _f.Service.GetRepPaymentsAsync(_f.RepAUserId, trash: false);
        repActive.Should().Contain(p => p.Id == payment.Id);
    }

    [Fact]
    public async Task RepTrash_DoesNotAffect_AdminOrCoordinatorVisibility()
    {
        var payment = await _f.CreateAndSettleAsync(_f.RepAUserId, "Rep Trash Co", 400, expectedNotifications: 3);

        await _f.Service.RepTrashAsync(payment.Id, _f.RepAUserId);

        var repActive = await _f.Service.GetRepPaymentsAsync(_f.RepAUserId, trash: false);
        repActive.Should().NotContain(p => p.Id == payment.Id);

        var repTrash = await _f.Service.GetRepPaymentsAsync(_f.RepAUserId, trash: true);
        repTrash.Should().Contain(p => p.Id == payment.Id);

        var adminActive = await _f.Service.GetAllAsync();
        adminActive.Should().Contain(p => p.Id == payment.Id);

        var coordinatorActive = await _f.Service.GetForCoordinatorAsync(_f.CoordinatorAUserId, trash: false);
        coordinatorActive.Should().Contain(p => p.Id == payment.Id);
    }

    [Fact]
    public async Task Restore_OnlyClearsCurrentRoleTrashState()
    {
        var payment = await _f.CreateAndSettleAsync(_f.RepAUserId, "Restore Co", 600, expectedNotifications: 3);

        await _f.Service.AdminSoftDeleteAsync(payment.Id, "Admin User");
        await _f.Service.CoordinatorTrashAsync(payment.Id, _f.CoordinatorAUserId);

        await _f.Service.AdminRestoreAsync(payment.Id);

        var adminActive = await _f.Service.GetAllAsync();
        adminActive.Should().Contain(p => p.Id == payment.Id);

        // Coordinator trash state must remain untouched by the admin restore.
        var coordinatorTrash = await _f.Service.GetForCoordinatorAsync(_f.CoordinatorAUserId, trash: true);
        coordinatorTrash.Should().Contain(p => p.Id == payment.Id);
    }
}

/// <summary>
/// Covers the ownership-based authorization a private-file download endpoint relies on:
/// each role's scoped GetById* method must succeed only when the payment is actually in that
/// caller's scope, and throw NotFoundException otherwise — mirroring RepPaymentController.GetEvidence.
/// </summary>
public class RepPaymentEvidenceAccessTests : IClassFixture<RepPaymentServiceFixture>
{
    private readonly RepPaymentServiceFixture _f;
    public RepPaymentEvidenceAccessTests(RepPaymentServiceFixture f) => _f = f;

    private async Task<RepPaymentDto> CreatePaymentWithEvidenceAsync()
    {
        var image = TestFormFile.Create("evidence.jpg", "image/jpeg");
        var result = await _f.Service.CreateAsync(
            _f.RepAUserId, new CreateRepPaymentDto { CustomerName = "Evidence Co", ReferenceNumber = "REF-EVID", Amount = 100 }, image);
        await RepPaymentServiceFixture.WaitUntilAsync(
            () => _f.ReadDb.Set<Notification>().AsNoTracking().Any(n => n.Metadata != null && n.Metadata.Contains(result.Id.ToString())));
        return result;
    }

    [Fact]
    public async Task Create_WithImage_PersistsPrivateStorageKey_AndExposesAuthenticatedEndpointUrl()
    {
        var payment = await CreatePaymentWithEvidenceAsync();

        payment.HasEvidence.Should().BeTrue();
        payment.ImageUrl.Should().Be($"/api/rep-payments/{payment.Id}/evidence");

        var storageKey = await _f.Service.GetEvidenceStorageKeyAsync(payment.Id);
        storageKey.Should().StartWith("private/rep-payments/");
    }

    [Fact]
    public async Task Rep_CanAccess_OwnEvidence()
    {
        var payment = await CreatePaymentWithEvidenceAsync();

        var act = async () => await _f.Service.GetForRepByIdAsync(payment.Id, _f.RepAUserId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Rep_CannotAccess_AnotherRepsEvidence()
    {
        var payment = await CreatePaymentWithEvidenceAsync();

        var act = async () => await _f.Service.GetForRepByIdAsync(payment.Id, _f.RepBUserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AssignedCoordinator_CanAccess_RepsEvidence()
    {
        var payment = await CreatePaymentWithEvidenceAsync();

        var act = async () => await _f.Service.GetForCoordinatorByIdAsync(payment.Id, _f.CoordinatorAUserId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnassignedCoordinator_CannotAccess_RepsEvidence()
    {
        var payment = await CreatePaymentWithEvidenceAsync();

        var act = async () => await _f.Service.GetForCoordinatorByIdAsync(payment.Id, _f.CoordinatorBUserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Admin_CanAccess_AnyEvidence()
    {
        var payment = await CreatePaymentWithEvidenceAsync();

        var act = async () => await _f.Service.GetByIdForAdminAsync(payment.Id);

        await act.Should().NotThrowAsync();
    }
}
