using Xunit;

public sealed class FactoryPlanningStageSevenTests
{
    private static DateOnly Monday => new(2026,10,26);
    private static WorkShift Shift(Guid center)=>
        new(){WorkCenterId=center,DayOfWeek=(int)Monday.DayOfWeek,
            StartTime=new TimeOnly(8,0),EndTime=new TimeOnly(16,0),
            EffectiveCapacityPercent=100m};
    [Fact]
    public void Finite_aps_never_double_books_same_workcenter()
    {
        var center=Guid.NewGuid();
        var a=Guid.NewGuid();var b=Guid.NewGuid();var route=Guid.NewGuid();
        ApsTask[] operations=[
            new(a,Guid.NewGuid(),Monday,1,route,10,center,240,"Weld A"),
            new(b,Guid.NewGuid(),Monday,1,route,10,center,240,"Weld B")
        ];
        var scheduled=FiniteFactoryScheduler.Schedule(operations,[Shift(center)],Monday,1);
        Assert.Empty(scheduled.Unscheduled);
        Assert.Equal(2,scheduled.Allocations.Count);
        var ordered=scheduled.Allocations.OrderBy(x=>x.StartsAt).ToList();
        Assert.True(ordered[0].FinishesAt<=ordered[1].StartsAt);
        Assert.Equal(new TimeOnly(8,0),ordered[0].StartsAt);
        Assert.Equal(new TimeOnly(16,0),ordered[1].FinishesAt);
    }
    [Fact]
    public void Finite_aps_enforces_operation_precedence_by_demand_line()
    {
        var centerA=Guid.NewGuid();var centerB=Guid.NewGuid();var demand=Guid.NewGuid();
        var item=Guid.NewGuid();var revision=Guid.NewGuid();
        ApsTask[] tasks=[
            new(demand,item,Monday,1,revision,10,centerA,120,"Welding"),
            new(demand,item,Monday,1,revision,20,centerB,180,"Assembly")
        ];
        var result=FiniteFactoryScheduler.Schedule(tasks,[Shift(centerA),Shift(centerB)],Monday,1);
        Assert.Empty(result.Unscheduled);
        Assert.Equal(new TimeOnly(10,0),result.Allocations[1].StartsAt);
        Assert.Equal(new TimeOnly(13,0),result.Allocations[1].FinishesAt);
    }
    [Fact]
    public void Finite_aps_refuses_work_bigger_than_one_shift_instead_of_overbooking()
    {
        var center=Guid.NewGuid();
        ApsTask[] tasks=[new(Guid.NewGuid(),Guid.NewGuid(),Monday,100,
            Guid.NewGuid(),10,center,600,"Long operation")];
        var result=FiniteFactoryScheduler.Schedule(tasks,[Shift(center)],Monday,1);
        Assert.Empty(result.Allocations);
        Assert.Single(result.Unscheduled);
    }
    [Fact]
    public void Reduced_capacity_scenario_can_make_work_unscheduled()
    {
        var center=Guid.NewGuid();
        ApsTask[] tasks=[new(Guid.NewGuid(),Guid.NewGuid(),Monday,100,
            Guid.NewGuid(),10,center,360,"Semi-finished assembly")];
        Assert.Single(FiniteFactoryScheduler.Schedule(tasks,[Shift(center)],Monday,1,1).Allocations);
        Assert.Single(FiniteFactoryScheduler.Schedule(tasks,[Shift(center)],Monday,1,0.5m).Unscheduled);
    }
    [Fact]
    public void Factory_material_variances_only_use_internal_confirmations()
    {
        var material=Guid.NewGuid();var product=Guid.NewGuid();
        var bom=new[]{new ManufacturingBomLine{ParentItemId=product,ComponentItemId=material,
            QuantityPer=2m,ScrapPercent=0m}};
        var usage=new[]{new FactoryMaterialUsage{ComponentItemId=material,QuantityConsumed=19}};
        var result=Assert.Single(FactoryOperationalMetrics.MaterialVariances(10,bom,usage));
        Assert.Equal(20m,result.PlannedRequired);
        Assert.Equal(19m,result.RecordedConsumed);
        Assert.Equal(-1m,result.Variance);
    }
    [Fact]
    public void Factory_adherence_never_claims_production_in_unfinished_orders()
    {
        var item=Guid.NewGuid();
        var mps=new[]{new MpsDemandLine{ItemId=item,NeededOn=Monday,Quantity=30}};
        var orders=new[]{
            new ManufacturingOrder{ItemId=item,DueOn=Monday,Status=ManufacturingOrderStatus.InProgress,
                Operations=[new ManufacturingOrderOperation{Sequence=10,GoodQuantity=20}]},
            new ManufacturingOrder{ItemId=item,DueOn=Monday,Status=ManufacturingOrderStatus.Completed,
                Operations=[new ManufacturingOrderOperation{Sequence=10,GoodQuantity=12}]}
        };
        var summary=Assert.Single(FactoryOperationalMetrics.Adherence(mps,orders,Monday,Monday));
        Assert.Equal(30m,summary.MpsRequired);
        Assert.Equal(12m,summary.CompletedGood);
        Assert.Equal(-18m,summary.Variance);
        Assert.Equal(40m,summary.FulfilmentPercent);
    }
}
