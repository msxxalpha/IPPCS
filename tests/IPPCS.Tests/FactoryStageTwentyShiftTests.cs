using Xunit;
public sealed class FactoryStageTwentyShiftTests
{
    private static WorkShift Night(Guid center,int day,int start=22,int end=6)=>
        new(){WorkCenterId=center,DayOfWeek=day,StartTime=new TimeOnly(start,0),
            EndTime=new TimeOnly(end,0),EffectiveCapacityPercent=100};
    [Fact]
    public void Overnight_weekly_shifts_have_exact_previous_day_CRP_distribution()
    {
        var center=Guid.NewGuid();
        var sunday=new DateOnly(2026,10,25);
        var previousSaturday=Night(center,6,22,6);
        var sundayShift=Night(center,0,22,6);
        var shifts=new[]{previousSaturday,sundayShift};
        Assert.Equal(480,FactoryShiftCalendar.AvailableMinutes(center,sunday,shifts));
        Assert.Equal(120,FactoryShiftCalendar.AvailableMinutes(center,sunday.AddDays(1),shifts));
        Assert.True(FactoryShiftCalendar.ConflictsWeekly(
            new WorkShift{WorkCenterId=center,DayOfWeek=0,
                StartTime=new TimeOnly(5,30),EndTime=new TimeOnly(7,0),
                EffectiveCapacityPercent=100},shifts));
        Assert.False(FactoryShiftCalendar.ConflictsWeekly(
            new WorkShift{WorkCenterId=center,DayOfWeek=0,
                StartTime=new TimeOnly(6,0),EndTime=new TimeOnly(7,0),
                EffectiveCapacityPercent=100},shifts));
    }
    [Fact]
    public void Reservation_can_end_next_day_and_reader_accepts_immutable_cross_midnight()
    {
        var center=Guid.NewGuid();var sunday=new DateOnly(2026,10,25);
        var shift=Night(center,0,22,6);
        var date=PersianPlanningDates.Format(sunday);
        Assert.True(FactoryCapacityReservations.TryWindow(new FactoryReservationInput(
            Guid.NewGuid(),date,"23:15","01:45","Night test"),out var start,out var end));
        Assert.Equal(sunday.AddDays(1),DateOnly.FromDateTime(end));
        Assert.True(FactoryCapacityReservations.WithinShift(center,start,end,[shift]));
        var order=Guid.NewGuid();var operation=Guid.NewGuid();
        var reservation=new FactoryCapacityReservation(Guid.NewGuid(),order,operation,
            center,start,end,DateTime.UtcNow,"planner","overnight");
        var evt=new ManufacturingOrderAudit{OrderId=order,Action=FactoryCapacityReservations.Reserved,
            Details=FactoryCapacityReservations.Encode(reservation)};
        var parsed=Assert.Single(FactoryCapacityReservations.Read([evt]));
        Assert.Equal(end,parsed.EndLocal);
        var overlap=new FactoryCapacityReservation(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            center,start.AddMinutes(30),end.AddMinutes(10),DateTime.UtcNow,"planner","collision");
        Assert.Throws<InvalidOperationException>(()=>FactoryCapacityReservations.Read([
            evt,new ManufacturingOrderAudit{OrderId=overlap.OrderId,
                Action=FactoryCapacityReservations.Reserved,
                Details=FactoryCapacityReservations.Encode(overlap),AtUtc=evt.AtUtc.AddMinutes(1)}
        ]));
    }
    [Fact]
    public void APS_night_operation_reserves_both_days_and_obeys_sequential_dependencies()
    {
        var center=Guid.NewGuid();var date=new DateOnly(2026,10,25);
        var shift=Night(center,0,22,6);
        var demand=Guid.NewGuid();var item=Guid.NewGuid();
        var tasks=new[]{
            new ApsTask(demand,item,date.AddDays(1),1,Guid.NewGuid(),10,center,180,"night"),
            new ApsTask(demand,item,date.AddDays(1),1,Guid.NewGuid(),20,center,120,"next")
        };
        var result=FiniteFactoryScheduler.Schedule(tasks,[shift],date,3);
        Assert.Empty(result.Unscheduled);
        Assert.Equal(2,result.Allocations.Count);
        var first=result.Allocations[0];var second=result.Allocations[1];
        Assert.Equal(date,first.StartDate);
        Assert.Equal(date.AddDays(1),first.FinishDate);
        Assert.Equal(new TimeOnly(22,0),first.StartsAt);
        Assert.Equal(new TimeOnly(1,0),first.FinishesAt);
        Assert.Equal(date.AddDays(1),second.StartDate);
        Assert.True(second.StartsAt>=first.FinishesAt);
    }
    [Fact]
    public void CRP_includes_previous_night_capacity_in_next_local_day()
    {
        var center=Guid.NewGuid();var item=Guid.NewGuid();
        var sun=new DateOnly(2026,10,25);var monday=sun.AddDays(1);
        var routing=new RoutingRevision{ItemId=item,State=RoutingState.Approved,
            EffectiveFrom=sun,Operations=[new RoutingOperation{
                Sequence=10,WorkCenterId=center,RunMinutesPerUnit=1,YieldPercent=100}]};
        var source=new MpsDemandLine{ItemId=item,NeededOn=monday,Quantity=100};
        var crp=Assert.Single(PlanningCalculators.RunRoughCutCrp([source],[routing],[Night(center,0,22,6)]));
        Assert.Equal(360,crp.AvailableMinutes);
        Assert.Equal(0,crp.OverloadMinutes);
    }
}
