using Xunit;

public sealed class FactoryOperationClockTests
{
    private static readonly Guid Order=Guid.NewGuid();
    private static readonly Guid OtherOrder=Guid.NewGuid();
    private static readonly Guid Op=Guid.NewGuid();
    private static readonly Guid Center=Guid.NewGuid();
    private static readonly DateTime Start=new(2026,10,26,8,30,0,DateTimeKind.Utc);
    private static ManufacturingOrderAudit Event(Guid order,string action,Guid operation,
        Guid center,DateTime time)=>new(){OrderId=order,Action=action,
            Details=FactoryOperationClock.Detail(operation,center,"CI-Operator"),AtUtc=time};
    [Fact]
    public void Start_stop_pairs_are_append_only_and_return_correct_session()
    {
        var history=new[]{
            Event(Order,FactoryOperationClock.Started,Op,Center,Start),
            Event(Order,FactoryOperationClock.Stopped,Op,Center,Start.AddMinutes(35))
        };
        var session=Assert.Single(FactoryOperationClock.Read(history));
        Assert.Equal(Order,session.OrderId);
        Assert.Equal(Start,session.StartedUtc);
        Assert.Equal(Start.AddMinutes(35),session.StoppedUtc);
        Assert.Equal(Center,session.WorkCenterId);
    }

    [Fact]
    public void Open_session_is_not_mistaken_for_a_completed_operation()
    {
        var running=Assert.Single(FactoryOperationClock.Read([
            Event(Order,FactoryOperationClock.Started,Op,Center,Start)]));
        Assert.Null(running.StoppedUtc);
        Assert.Single(FactoryOperationClock.BusyIntervals([running],
            new DateTime(2026,10,26,18,0,0,DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Double_start_and_unmatched_stop_are_rejected()
    {
        var start=Event(Order,FactoryOperationClock.Started,Op,Center,Start);
        Assert.Throws<InvalidOperationException>(()=>FactoryOperationClock.Read([
            start,Event(Order,FactoryOperationClock.Started,Op,Center,Start.AddMinutes(1))]));
        Assert.Throws<InvalidOperationException>(()=>FactoryOperationClock.Read([
            Event(Order,FactoryOperationClock.Stopped,Op,Center,Start)]));
    }

    [Fact]
    public void Concurrent_open_sessions_on_one_center_are_rejected_across_orders()
    {
        Assert.Throws<InvalidOperationException>(()=>FactoryOperationClock.Read([
            Event(Order,FactoryOperationClock.Started,Op,Center,Start),
            Event(OtherOrder,FactoryOperationClock.Started,Guid.NewGuid(),Center,Start.AddMinutes(1))
        ]));
    }

    [Fact]
    public void Overlapping_closed_sessions_on_same_center_fail_closed()
    {
        var nextOp=Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(()=>FactoryOperationClock.Read([
            Event(Order,FactoryOperationClock.Started,Op,Center,Start),
            Event(Order,FactoryOperationClock.Stopped,Op,Center,Start.AddMinutes(10)),
            Event(OtherOrder,FactoryOperationClock.Started,nextOp,Center,Start.AddMinutes(5)),
            Event(OtherOrder,FactoryOperationClock.Stopped,nextOp,Center,Start.AddMinutes(15))
        ]));
    }

    [Fact]
    public void Time_slots_prevent_double_booking_but_allow_nonoverlapping_work()
    {
        var center=Center;
        var startDate=new DateOnly(2026,10,26);
        var shift=new WorkShift{WorkCenterId=center,DayOfWeek=(int)startDate.DayOfWeek,
            StartTime=new TimeOnly(8,0),EndTime=new TimeOnly(16,0),EffectiveCapacityPercent=100m};
        var task=new ApsTask(Guid.NewGuid(),Guid.NewGuid(),startDate,1,Guid.NewGuid(),
            10,center,60,"Damper assembly");
        var reserved=new WorkCenterOccupiedInterval(center,
            new DateTime(2026,10,26,8,0,0),new DateTime(2026,10,26,9,15,0));
        var result=FiniteFactoryScheduler.Schedule([task],[shift],startDate,1,
            occupiedIntervals:[reserved]);
        Assert.Empty(result.Unscheduled);
        Assert.Equal(new TimeOnly(9,15),Assert.Single(result.Allocations).StartsAt);
    }

    [Fact]
    public void Midnight_crossing_interval_is_split_into_independent_days()
    {
        var date=new DateOnly(2026,10,26);
        var next=date.AddDays(1);
        var shifts=new[]{
            new WorkShift{WorkCenterId=Center,DayOfWeek=(int)date.DayOfWeek,
                StartTime=new TimeOnly(22,0),EndTime=new TimeOnly(23,59),EffectiveCapacityPercent=100m},
            new WorkShift{WorkCenterId=Center,DayOfWeek=(int)next.DayOfWeek,
                StartTime=new TimeOnly(0,0),EndTime=new TimeOnly(4,0),EffectiveCapacityPercent=100m}
        };
        var reserved=new WorkCenterOccupiedInterval(Center,
            new DateTime(2026,10,26,23,30,0),new DateTime(2026,10,27,0,45,0));
        var task=new ApsTask(Guid.NewGuid(),Guid.NewGuid(),next,1,Guid.NewGuid(),10,Center,60,"Inspection");
        var preview=FiniteFactoryScheduler.Schedule([task],shifts,date,2,
            earliestStartDates:new Dictionary<Guid,DateOnly>{{task.DemandLineId,next}},
            occupiedIntervals:[reserved]);
        Assert.Empty(preview.Unscheduled);
        Assert.Equal(new TimeOnly(0,45),Assert.Single(preview.Allocations).StartsAt);
        Assert.Equal(next,preview.Allocations[0].StartDate);
    }

    [Fact]
    public void Back_to_back_sessions_are_valid_without_double_reserving()
    {
        var op2=Guid.NewGuid();
        var list=FactoryOperationClock.Read([
            Event(Order,FactoryOperationClock.Started,Op,Center,Start),
            Event(Order,FactoryOperationClock.Stopped,Op,Center,Start.AddMinutes(10)),
            Event(OtherOrder,FactoryOperationClock.Started,op2,Center,Start.AddMinutes(10)),
            Event(OtherOrder,FactoryOperationClock.Stopped,op2,Center,Start.AddMinutes(20))
        ]);
        Assert.Equal(2,list.Count);
    }
}
