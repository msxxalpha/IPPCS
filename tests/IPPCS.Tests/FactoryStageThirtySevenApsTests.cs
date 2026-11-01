using Xunit;

public sealed class FactoryStageThirtySevenApsTests
{
    private static ManufacturingOrder Active(Guid center,DateOnly from,DateOnly due)
    {
        return new ManufacturingOrder{
            Id=Guid.NewGuid(),Status=ManufacturingOrderStatus.Released,
            PlannedStart=from,DueOn=due,
            Operations=[new ManufacturingOrderOperation{
                Id=Guid.NewGuid(),WorkCenterId=center,Sequence=10,IsComplete=false
            }]
        };
    }

    [Fact]
    public void Active_order_without_reliable_clock_or_slot_conservatively_blocks_center()
    {
        var start=new DateOnly(2026,10,27);
        var center=Guid.NewGuid();
        var order=Active(center,start,start.AddDays(3));
        var guard=FactoryApsExecutionGuard.Evaluate([order],[],[],start,3);
        Assert.Equal(3,guard.ReservedDays.Count);
        Assert.Equal(1,guard.ActiveOrders);
        Assert.Equal(1,guard.BlockedOperations);
        Assert.Empty(guard.OccupiedIntervals);
        Assert.All(Enumerable.Range(0,3),i=>
            Assert.Contains((center,start.AddDays(i)),guard.ReservedDays));
    }

    [Fact]
    public void Scheduled_machine_slot_only_blocks_its_real_interval()
    {
        var start=new DateOnly(2026,10,27);
        var center=Guid.NewGuid();
        var order=Active(center,start,start.AddDays(2));
        var operation=order.Operations.Single();
        var from=start.ToDateTime(new TimeOnly(8,0));
        var booked=new FactoryCapacityReservation(Guid.NewGuid(),order.Id,operation.Id,
            center,from,from.AddHours(1),DateTime.UtcNow,"CI TEST","planned slot");
        var guard=FactoryApsExecutionGuard.Evaluate([order],[],[booked],start,2);
        Assert.Empty(guard.ReservedDays);
        Assert.Single(guard.OccupiedIntervals);
        Assert.Equal(from,guard.OccupiedIntervals[0].StartLocal);
        Assert.Equal(1,guard.ActiveReservations);
    }

    [Fact]
    public void Open_actual_clock_blocks_remaining_horizon_without_fake_stop()
    {
        var start=new DateOnly(2026,10,27);
        var center=Guid.NewGuid();
        var order=Active(center,start,start.AddDays(2));
        var op=order.Operations.Single();
        var tehran=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        var local=start.ToDateTime(new TimeOnly(8,0));
        var utc=TimeZoneInfo.ConvertTimeToUtc(local,tehran);
        var session=new FactoryClockSession(order.Id,op.Id,center,utc,null);
        var guard=FactoryApsExecutionGuard.Evaluate([order],[session],[],start,2);
        Assert.Single(guard.OccupiedIntervals);
        Assert.Empty(guard.ReservedDays);
        Assert.Equal(start.AddDays(2).ToDateTime(TimeOnly.MinValue),
            guard.OccupiedIntervals[0].EndLocal);
        Assert.Equal(1,guard.OpenClocks);
    }
}
