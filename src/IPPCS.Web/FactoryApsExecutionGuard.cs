// Stage 37: identical conservative capacity accounting in APS and net-MRP.
// Actual factory records are independent IPPCS observations, never PLC data.
public record FactoryApsCapacityEvidence(
    IReadOnlySet<(Guid WorkCenterId,DateOnly Date)> ReservedDays,
    IReadOnlyList<WorkCenterOccupiedInterval> OccupiedIntervals,
    int ActiveOrders,int ActiveReservations,int OpenClocks,int BlockedOperations);

public static class FactoryApsExecutionGuard
{
    public static FactoryApsCapacityEvidence Evaluate(
        IReadOnlyList<ManufacturingOrder> orders,
        IReadOnlyList<FactoryClockSession> clocks,
        IReadOnlyList<FactoryCapacityReservation> reservations,
        DateOnly start,int horizonDays)
    {
        if(horizonDays is <1 or >90)
            throw new ArgumentOutOfRangeException(nameof(horizonDays));
        var end=start.AddDays(horizonDays);
        var active=orders.Where(x=>x.Status is
            ManufacturingOrderStatus.Released or
            ManufacturingOrderStatus.InProgress or
            ManufacturingOrderStatus.OnHold).ToArray();
        var activeById=active.ToDictionary(x=>x.Id);
        var pending=active.SelectMany(x=>x.Operations
            .Where(op=>!op.IsComplete).Select(op=>new {Order=x,Operation=op})).ToArray();
        if(active.Any(x=>x.PlannedStart>x.DueOn || x.Operations.Count==0) ||
           pending.Any(x=>x.Operation.WorkCenterId==Guid.Empty))
            throw new InvalidOperationException("دستور فعال دارای تاریخ، عملیات یا مرکز کاری نامعتبر است؛ زمان‌بندی قابل اعتماد نیست.");
        var booked=reservations.Where(x=>
            activeById.TryGetValue(x.OrderId,out var order)&&
            order.Operations.Any(op=>!op.IsComplete&&op.Id==x.OperationId&&
                op.WorkCenterId==x.WorkCenterId)).ToArray();
        var open=clocks.Where(x=>x.StoppedUtc is null).ToArray();
        var occupied=FactoryOperationClock.BusyIntervals(clocks,
            end.ToDateTime(TimeOnly.MinValue))
            .Concat(FactoryCapacityReservations.Occupied(booked)).ToArray();
        var blocked=new HashSet<(Guid WorkCenterId,DateOnly Date)>();
        foreach(var x in pending)
        {
            if(open.Any(c=>c.OrderId==x.Order.Id&&c.OperationId==x.Operation.Id&&
                    c.WorkCenterId==x.Operation.WorkCenterId)||
               booked.Any(r=>r.OrderId==x.Order.Id&&r.OperationId==x.Operation.Id))
                continue;
            var from=x.Order.PlannedStart>start?x.Order.PlannedStart:start;
            for(var date=from;date<end;date=date.AddDays(1))
                blocked.Add((x.Operation.WorkCenterId,date));
        }
        return new FactoryApsCapacityEvidence(blocked,occupied,active.Length,
            booked.Length,open.Length,pending.Length);
    }
}
