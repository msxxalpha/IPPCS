using Xunit;

public sealed class FactoryStageFortyTwoSharedToolTests
{
    private static readonly DateOnly Day=new(2026,10,28);
    private static WorkShift Shift(Guid center)=>new(){
        WorkCenterId=center,DayOfWeek=(int)Day.DayOfWeek,
        StartTime=new TimeOnly(8,0),EndTime=new TimeOnly(16,0),
        EffectiveCapacityPercent=100
    };
    private static ApsTask Task(Guid center)=>new(
        Guid.NewGuid(),Guid.NewGuid(),Day,1,Guid.NewGuid(),
        10,center,90,"Shock absorber fixture operation");
    private static DateTime Local(ApsAllocation a)=>a.StartDate.ToDateTime(a.StartsAt);
    private static DateTime End(ApsAllocation a)=>a.FinishDate.ToDateTime(a.FinishesAt);

    [Fact]
    public void Shared_fixture_blocks_parallel_centers_but_preserves_both_jobs()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();
        var tasks=new[]{Task(a),Task(b)};
        var shifts=new[]{Shift(a),Shift(b)};
        var baseline=FiniteFactoryScheduler.Schedule(tasks,shifts,Day,1);
        Assert.Equal(2,baseline.Allocations.Count);
        Assert.All(baseline.Allocations,x=>Assert.Equal(new TimeOnly(8,0),x.StartsAt));

        var guarded=FiniteFactoryScheduler.Schedule(tasks,shifts,Day,1,
            sharedToolGroups:new Dictionary<Guid,string>{{a,"FIXTURE_A"},{b,"FIXTURE_A"}});
        Assert.Empty(guarded.Unscheduled);
        var times=guarded.Allocations.OrderBy(Local).ToArray();
        Assert.Equal(2,times.Length);
        Assert.True(End(times[0])<=Local(times[1]));
        Assert.Equal(new TimeOnly(9,30),times[1].StartsAt);
    }

    [Fact]
    public void Independent_tools_do_not_block_each_other()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();
        var run=FiniteFactoryScheduler.Schedule([Task(a),Task(b)],
            [Shift(a),Shift(b)],Day,1,
            sharedToolGroups:new Dictionary<Guid,string>{{a,"TOOL_A"},{b,"TOOL_B"}});
        Assert.Equal(2,run.Allocations.Count);
        Assert.All(run.Allocations,x=>Assert.Equal(new TimeOnly(8,0),x.StartsAt));
    }

    [Fact]
    public void Actual_reservation_on_one_center_blocks_the_shared_tool_in_another()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();
        var reservation=new WorkCenterOccupiedInterval(a,
            Day.ToDateTime(new TimeOnly(8,0)),
            Day.ToDateTime(new TimeOnly(10,0)));
        var run=FiniteFactoryScheduler.Schedule([Task(b)],
            [Shift(a),Shift(b)],Day,1,
            occupiedIntervals:[reservation],
            sharedToolGroups:new Dictionary<Guid,string>{{a,"TOOL_A"},{b,"TOOL_A"}});
        Assert.Equal(new TimeOnly(10,0),Assert.Single(run.Allocations).StartsAt);
    }

    [Fact]
    public void Contradictory_active_evidence_for_one_shared_tool_fails_closed()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();
        var from=Day.ToDateTime(new TimeOnly(8,0));
        var history=new[]{
            new WorkCenterOccupiedInterval(a,from,from.AddHours(2)),
            new WorkCenterOccupiedInterval(b,from.AddHours(1),from.AddHours(3))
        };
        Assert.Throws<InvalidOperationException>(()=>FiniteFactoryScheduler.Schedule(
            [Task(a)],[Shift(a),Shift(b)],Day,1,
            occupiedIntervals:history,
            sharedToolGroups:new Dictionary<Guid,string>{{a,"TOOL_A"},{b,"TOOL_A"}}));
    }

    [Fact]
    public void Shared_tool_is_an_ipccs_master_option_not_a_calibration_or_erp_resource()
    {
        Assert.True(FactoryMasterData.ValidKind(FactoryMasterData.SharedToolGroup));
        Assert.True(FactoryMasterData.ValidCode(
            FactoryMasterData.SharedToolGroup,"DAMPER_JIG"));
        Assert.False(FactoryMasterData.ValidCode(
            FactoryMasterData.SharedToolGroup,"unsafe|tool"));
    }
}
