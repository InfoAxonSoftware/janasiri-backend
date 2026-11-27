using DistributionSystem.Application.DTOs.QuickRequests;
using DistributionSystem.Application.Exceptions;
using DistributionSystem.Application.Services.Implementations;
using DistributionSystem.Application.Services.Interfaces;
using DistributionSystem.Domain.Entities;
using DistributionSystem.Domain.Enums;
using DistributionSystem.Infrastructure.Data.Repositories.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DistributionSystem.UnitTests;

public class MultiRepAssignmentTests
{
    [Fact]
    public async Task Admin_can_add_replace_and_remove_reps_on_all_record_types()
    {
        using var f = new OrderTrashFixture();
        var db = f.Db;
        var admin = new User { Username = "assign-admin", Role = UserRole.Admin, IsActive = true };
        var u2 = new User { Username = "rep-two", Role = UserRole.SalesRep, IsActive = true };
        var u3 = new User { Username = "rep-three", Role = UserRole.SalesRep, IsActive = true };
        var r2 = new SalesRepProfile { User = u2, UserId = u2.Id, FullName = "Rep Two", EmployeeCode = "TWO" };
        var r3 = new SalesRepProfile { User = u3, UserId = u3.Id, FullName = "Rep Three", EmployeeCode = "THREE" };
        var quotation = new Quotation { CustomerId = f.CustomerId, RepId = f.RepId, QuotationNumber = "QT-MULTI" };
        var guest = new QuickRequest { Type = QuickRequestType.Order, CreatedBy = "Quick Order Portal", RequestNumber = "QO-GUEST-MULTI", RepId = f.RepId };
        var quickQuote = new QuickRequest { Type = QuickRequestType.Quotation, CreatedBy = "rep", RequestNumber = "QQ-MULTI", RepId = f.RepId };
        db.AddRange(admin, u2, u3, r2, r3, quotation, guest, quickQuote);
        await db.SaveChangesAsync();
        var publisher = Mock.Of<INotificationPublisher>();
        var svc = new StandardRepAssignmentService(new UnitOfWork(db), publisher, NullLogger<StandardRepAssignmentService>.Instance);
        var qs = new QuickRequestService(new UnitOfWork(db), Mock.Of<IFileStorageService>(), publisher);
        var quoteSvc = new QuotationService(new UnitOfWork(db), Mock.Of<INotificationService>(), Mock.Of<IEmailService>());
        var actor = admin.Id;
        var second = r2.Id;
        var third = r3.Id;
        var repTwoUser = u2.Id;
        var repThreeUser = u3.Id;
        var quotationId = quotation.Id;
        var guestId = guest.Id;
        var quickQuoteId = quickQuote.Id;
        AssignGuestOrderRepsDto Change(IEnumerable<Guid> target, IEnumerable<Guid> expected, Guid? version) =>
            new() { RepIds = target.ToList(), ExpectedRepIds = expected.ToList(), ExpectedVersion = version };
        foreach (var (kind,id) in new[] { ("orders",f.OrderId), ("quotations",quotationId) })
        {
            var initial = await svc.GetAsync(kind,id,actor,default);
            Assert.Equal(f.RepId, Assert.Single(initial.AssignedReps).Id);
            await Assert.ThrowsAsync<ForbiddenException>(() => svc.GetAsync(kind,id,f.CoordinatorUserId,default));
            var added = await svc.SetAsync(kind,id,actor,Change([f.RepId,second,third],[f.RepId],initial.AssignmentVersion),"admin",default);
            Assert.Equal(3,added.AssignedReps.Count);
            db.ChangeTracker.Clear();
            var after = await svc.GetAsync(kind,id,actor,default);
            Assert.Equal(3,after.AssignedReps.Count);
            if (kind == "orders")
            {
                Assert.Equal(id,(await f.Service.RepGetByIdAsync(id,repTwoUser)).Id);
                Assert.Contains((await f.Service.GetUnifiedRepAsync(repThreeUser,new(){PageSize=100},false)).Items,x=>x.Id==id);
            }
            else Assert.Equal(id,(await quoteSvc.RepGetQuotationByIdAsync(repTwoUser,id,default)).Id);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => svc.SetAsync(kind,id,actor,Change([],[],initial.AssignmentVersion),"admin",default));
            var replaced = await svc.SetAsync(kind,id,actor,Change([second],[f.RepId,second,third],after.AssignmentVersion),"admin",default);
            Assert.Equal(second,Assert.Single(replaced.AssignedReps).Id);
            db.ChangeTracker.Clear();
            if (kind == "orders") await Assert.ThrowsAsync<NotFoundException>(()=>f.Service.RepGetByIdAsync(id,repThreeUser));
            else await Assert.ThrowsAsync<NotFoundException>(()=>quoteSvc.RepGetQuotationByIdAsync(repThreeUser,id,default));
            var cleared = await svc.SetAsync(kind,id,actor,Change([],[second],replaced.AssignmentVersion),"admin",default);
            Assert.Empty(cleared.AssignedReps);
        }
        foreach (var id in new[] { f.QuickOrderId, guestId, quickQuoteId })
        {
            var initial = await qs.GetByIdAsync(id);
            var added = await qs.AssignGuestOrderRepsAsync(id,actor,Change([f.RepId,second,third],[f.RepId],initial.AssignmentVersion),"admin");
            Assert.Equal(3,added.AssignedReps.Count);
            db.ChangeTracker.Clear();
            Assert.Equal(3,(await qs.GetRepRequestByIdAsync(id,repTwoUser)).AssignedReps.Count);
            Assert.Equal(id,(await qs.GetRepRequestByIdAsync(id,repThreeUser)).Id);
            if (id == guestId)
                await Assert.ThrowsAsync<ForbiddenException>(()=>qs.AssignGuestOrderRepsAsync(id,f.CoordinatorUserId,Change([f.RepId],[f.RepId,second,third],added.AssignmentVersion),"coord"));
            else await Assert.ThrowsAsync<NotFoundException>(()=>qs.GetGuestOrderRepOptionsAsync(id,f.CoordinatorUserId));
            var replaced = await qs.AssignGuestOrderRepsAsync(id,actor,Change([second],[f.RepId,second,third],added.AssignmentVersion),"admin");
            Assert.Equal(second,Assert.Single(replaced.AssignedReps).Id);
            db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<NotFoundException>(()=>qs.GetRepRequestByIdAsync(id,repThreeUser));
            Assert.Empty((await qs.AssignGuestOrderRepsAsync(id,actor,Change([],[second],replaced.AssignmentVersion),"admin")).AssignedReps);
        }
        Assert.Equal(3, await db.QuickRequests.CountAsync());
        Assert.Single(await db.Orders.ToListAsync());
        Assert.Single(await db.Quotations.ToListAsync());
        Assert.Equal(25,await db.Notifications.CountAsync());
    }
}
