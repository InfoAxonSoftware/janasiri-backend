namespace DistributionSystem.Domain.Enums;

public enum OrderStatus
{
    Pending = 1,
    Approved = 2,
    Processing = 3,
    Dispatched = 4,
    Delivered = 5,
    Completed = 6,
    Cancelled = 7,
    OnHold = 8,
    Rejected = 9,
    PartiallyDelivered = 10,
    SalesOrderDone = 11
}
