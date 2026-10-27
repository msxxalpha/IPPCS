using Xunit;

public sealed class FactoryCapacityReservationTests
{
    static DateTime At(int h,int m=0)=>new(2026,10,26,h,m,0);
    static ManufacturingOrderAudit Event(Guid order,string action,string details,int minute=0)=>
        new(){OrderId=order,Action=action,Details=details,
            AtUtc=new DateTime(2026,10,26,6,0,0,DateTimeKind.Utc).AddMinutes(minute)};
    static FactoryCapacityReservation Slot(Guid id,Guid order,Guid operation,Guid center,
        DateTime start,DateTime end)=>new(id,order,operation,center,start,end,
            new DateTime(2026,10,26,6,0,0,DateTimeKind.Utc),"planner","CI slot");

    [Fact]
    public void Date_is_jalali_and_time_requires_strict_nonzero_local_interval()
    {
        var d=PersianPlanningDates.Format(DateOnly.FromDateTime(At(0)));
        Assert.True(FactoryCapacityReservations.TryWindow(new(Guid.NewGuid(),d,"۰۸:۳۰","۱۰:۰۰",null),
            out var start,out var end));
        Assert.Equal(At(8,30),start);
        Assert.Equal(At(10),end);
        Assert.False(FactoryCapacityReservations.TryWindow(new(Guid.NewGuid(),d,"10:00","10:00",null),
            out _,out _));
        Assert.False(FactoryCapacityReservations.TryWindow(new(Guid.NewGuid(),"1405/13/40","08:00","10:00",null),
            out _,out _));
    }
    [Fact]
    public void Half_open_slot_edges_are_not_false_conflicts()
    {
        Assert.False(FactoryCapacityReservations.Overlap(At(8),At(9),At(9),At(10)));
        Assert.True(FactoryCapacityReservations.Overlap(At(8),At(9),At(8,59),At(10)));
        Assert.True(FactoryCapacityReservations.Overlap(At(8),At(9),At(7),At(8,1)));
    }
    [Fact]
    public void Journal_replays_cancellation_without_erasing_history()
    {
        var order=Guid.NewGuid();var op=Guid.NewGuid();var center=Guid.NewGuid();
        var a=Slot(Guid.NewGuid(),order,op,center,At(8),At(9));
        var b=Slot(Guid.NewGuid(),order,Guid.NewGuid(),center,At(9),At(10));
        var original=new[]{Event(order,FactoryCapacityReservations.Reserved,
            FactoryCapacityReservations.Encode(a)),Event(order,FactoryCapacityReservations.Reserved,
            FactoryCapacityReservations.Encode(b),1)};
        Assert.Equal(2,FactoryCapacityReservations.Read(original).Count);
        var cancelled=original.Append(Event(order,FactoryCapacityReservations.Cancelled,
            a.Id.ToString("N"),2)).ToArray();
        var surviving=Assert.Single(FactoryCapacityReservations.Read(cancelled));
        Assert.Equal(b.Id,surviving.Id);
        Assert.Throws<InvalidOperationException>(()=>FactoryCapacityReservations.Read(
            cancelled.Append(Event(order,FactoryCapacityReservations.Cancelled,a.Id.ToString("N"),3))));
    }
    [Fact]
    public void Overlapping_same_center_slots_and_tampered_events_fail_closed()
    {
        var order=Guid.NewGuid();var center=Guid.NewGuid();
        var a=Slot(Guid.NewGuid(),order,Guid.NewGuid(),center,At(8),At(10));
        var b=Slot(Guid.NewGuid(),order,Guid.NewGuid(),center,At(9),At(11));
        Assert.Throws<InvalidOperationException>(()=>FactoryCapacityReservations.Read([
            Event(order,FactoryCapacityReservations.Reserved,FactoryCapacityReservations.Encode(a)),
            Event(order,FactoryCapacityReservations.Reserved,FactoryCapacityReservations.Encode(b),1)
        ]));
    }
    [Fact]
    public void Booking_must_fit_within_effective_shift_capacity()
    {
        var center=Guid.NewGuid();
        var shift=new WorkShift{WorkCenterId=center,DayOfWeek=(int)At(8).DayOfWeek,
            StartTime=new TimeOnly(8,0),EndTime=new TimeOnly(16,0),
            EffectiveCapacityPercent=50};
        Assert.True(FactoryCapacityReservations.WithinShift(center,At(8),At(12),[shift]));
        Assert.False(FactoryCapacityReservations.WithinShift(center,At(11),At(13),[shift]));
        Assert.False(FactoryCapacityReservations.WithinShift(Guid.NewGuid(),At(8),At(9),[shift]));
    }
    [Fact]
    public void Live_reservations_occupy_exact_minutes_and_disable_unbooked_full_day_fallback()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();var orderId=Guid.NewGuid();
        var operation=Guid.NewGuid();var baseline=new DateOnly(2026,10,26);
        var order=new ManufacturingOrder{Id=orderId,ItemId=item,
            Status=ManufacturingOrderStatus.Released,PlannedStart=baseline,DueOn=baseline,
            Operations=[new ManufacturingOrderOperation{Id=operation,WorkCenterId=center}]};
        var planned=Slot(Guid.NewGuid(),orderId,operation,center,At(8),At(9));
        var lot=new NetPegPlannedLot("PREVIEW-LOT",item,baseline,baseline,10,10,0,
            "Manufacturing",null,false,0);
        var route=new RoutingRevision{ItemId=item,State=RoutingState.Approved,
            EffectiveFrom=baseline,Operations=[new RoutingOperation{
                Sequence=10,Name="assembly",WorkCenterId=center,
                RunMinutesPerUnit=6,YieldPercent=100}]};
        var shift=new WorkShift{WorkCenterId=center,DayOfWeek=(int)baseline.DayOfWeek,
            StartTime=new TimeOnly(8,0),EndTime=new TimeOnly(16,0),
            EffectiveCapacityPercent=100};
        var graph=new NetPeggingReport([],[],[lot]);
        var before=MrpLotCapacityPreview.Evaluate(graph,[route],[shift],baseline,1,1,
            [order],[]);
        Assert.Equal(1,before.BlockedLots);
        var after=MrpLotCapacityPreview.Evaluate(graph,[route],[shift],baseline,1,1,
            [order],[],[planned]);
        Assert.Equal(1,after.OnTimeLots);
        Assert.Equal(new TimeOnly(9,0),Assert.Single(after.Allocations).StartsAt);
    }
    [Fact]
    public void Clock_comparison_calculates_start_and_duration_variances()
    {
        var order=Guid.NewGuid();var op=Guid.NewGuid();
        var booked=Slot(Guid.NewGuid(),order,op,Guid.NewGuid(),At(8),At(9));
        // Iran time zone is UTC+3:30; actual start 08:10 Tehran.
        var startUtc=new DateTime(2026,10,26,4,40,0,DateTimeKind.Utc);
        var actual=new FactoryClockSession(order,op,booked.WorkCenterId,
            startUtc,startUtc.AddMinutes(80));
        var result=FactoryCapacityReservations.Compare(booked,[actual]);
        Assert.Equal(10m,result.StartVarianceMinutes);
        Assert.Equal(80m,result.ActualMinutes);
        Assert.Equal(20m,result.DurationVarianceMinutes);
    }
}
