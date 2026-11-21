using DistributionSystem.Application.Exceptions;
using DistributionSystem.Domain.Entities;

namespace DistributionSystem.Application.Services.Implementations;

/// <summary>
/// Shared guarded transition used by both manual Admin purge and scheduled
/// 7-day expiry. It removes records from Admin Trash without deleting the
/// underlying business rows or stored files.
/// </summary>
internal static class AdminRecordTrashPurge
{
    public static void MarkOrder(Order order, DateTime nowUtc)
    {
        if (!order.IsDeleted || order.IsPurgedByAdmin)
            throw new BusinessException("Order is not eligible for Admin Trash purge.", "ORDER_ADMIN_PURGE_NOT_ALLOWED");
        order.IsPurgedByAdmin = true;
        order.AdminPurgedAt = nowUtc;
    }

    public static void MarkQuickRequest(QuickRequest request, DateTime nowUtc)
    {
        if (!request.IsDeletedByAdmin || request.IsPurgedByAdmin)
            throw new BusinessException("Quick request is not eligible for Admin Trash purge.", "QUICK_REQUEST_ADMIN_PURGE_NOT_ALLOWED");
        request.IsPurgedByAdmin = true;
        request.AdminPurgedAt = nowUtc;
    }

    public static void MarkRepPayment(RepPayment payment, DateTime nowUtc)
    {
        if (!payment.IsDeletedByAdmin || payment.IsPurgedByAdmin)
            throw new BusinessException("Payment report is not eligible for Admin Trash purge.", "REP_PAYMENT_ADMIN_PURGE_NOT_ALLOWED");
        payment.IsPurgedByAdmin = true;
        payment.AdminPurgedAt = nowUtc;
    }

    public static void MarkQuotation(Quotation quotation, DateTime nowUtc)
    {
        if (!quotation.IsDeleted || quotation.IsPurgedByAdmin)
            throw new BusinessException("Quotation is not eligible for Admin Trash purge.", "QUOTATION_ADMIN_PURGE_NOT_ALLOWED");
        quotation.IsPurgedByAdmin = true;
        quotation.AdminPurgedAt = nowUtc;
    }
}
