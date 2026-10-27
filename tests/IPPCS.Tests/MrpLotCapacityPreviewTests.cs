using Xunit;

public sealed class MrpLotCapacityPreviewTests
{
    private static readonly DateOnly Monday=new(2026,10,26);
    private static NetPegPlannedLot Lot(Guid item,DateOnly release,DateOnly due,decimal quantity=20m) =>
        new("PLAN:"+item.ToString("N")+":"+due.ToString("yyyyMMdd"),item,due,release,
            quantity,quantity,0,"Manufacturing",Guid.NewGuid(),false,0);

    private static RoutingRevision Route(Guid item,Guid center,int sequence=10,
        decimal runMinutes=1m) =>
        new(){ItemId=item,State=RoutingState.Approved,EffectiveFrom=Monday.AddDays(-7),
            Operations=[new RoutingOperation { Sequence=sequence,WorkCenterId=center,
                Name="Damper weld",RunMinutesPerUnit=runMinutes,YieldPercent=100 }]};

    private static WorkShift Shift(Guid center,DateOnly day) =>
        new(){WorkCenterId=center,DayOfWeek=(int)day.DayOfWeek,
            StartTime=new TimeOnly(8,0),EndTime=new TimeOnly(16,0),
            EffectiveCapacityPercent=100};

    private static NetPeggingReport Graph(params NetPegPlannedLot[] lots) =>
        new([],[],lots);

    [Fact]
    public void Does_not_schedule_a_lot_before_its_proposed_release()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        var tuesday=Monday.AddDays(1);
        var report=MrpLotCapacityPreview.Evaluate(
            Graph(Lot(item,tuesday,tuesday,50)),[Route(item,center)],
            [Shift(center,Monday),Shift(center,tuesday)],Monday,2);
        Assert.Equal("OnTime",Assert.Single(report.Lots).Status);
        Assert.Equal(tuesday,Assert.Single(report.Allocations).StartDate);
        Assert.Equal(0,report.BlockedLots);
    }

    [Fact]
    public void Shared_center_batches_do_not_double_book_and_precedence_is_preserved()
    {
        var item1=Guid.NewGuid();var item2=Guid.NewGuid();var center=Guid.NewGuid();
        var a=Route(item1,center,10,10);
        var b=Route(item2,center,10,10);
        b.Operations.Add(new RoutingOperation{Sequence=20,WorkCenterId=center,
            Name="Final testing",RunMinutesPerUnit=1m,YieldPercent=100});
        var result=MrpLotCapacityPreview.Evaluate(
            Graph(Lot(item1,Monday,Monday,20),Lot(item2,Monday,Monday,20)),[a,b],
            [Shift(center,Monday)],Monday,1);
        Assert.Equal(2,result.OnTimeLots);
        Assert.Equal(3,result.Allocations.Count);
        var ops=result.Allocations.OrderBy(x=>x.StartsAt).ToArray();
        for(var i=1;i<ops.Length;i++)
            Assert.True(ops[i-1].FinishesAt<=ops[i].StartsAt);
        var secondOps=result.Allocations.Where(x=>x.ItemId==item2)
            .OrderBy(x=>x.Sequence).ToArray();
        Assert.True(secondOps[0].FinishesAt<=secondOps[1].StartsAt);
    }

    [Fact]
    public void Capacity_exhaustion_is_reported_not_silently_overbooked()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var center=Guid.NewGuid();
        var result=MrpLotCapacityPreview.Evaluate(
            Graph(Lot(a,Monday,Monday,30),Lot(b,Monday,Monday,30)),
            [Route(a,center,10,10),Route(b,center,10,10)],
            [Shift(center,Monday)],Monday,1);
        Assert.Single(result.Allocations);
        Assert.Single(result.Unscheduled);
        Assert.Equal(1,result.BlockedLots);
        Assert.Contains(result.Lots,x=>x.Status=="Unscheduled" && x.OperationsScheduled==0);
    }

    [Fact]
    public void Missing_route_and_release_before_baseline_are_blocked()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();var center=Guid.NewGuid();
        var result=MrpLotCapacityPreview.Evaluate(
            Graph(Lot(a,Monday.AddDays(-1),Monday),Lot(b,Monday,Monday)),
            [Route(a,center)],[Shift(center,Monday)],Monday,1);
        Assert.Equal(2,result.BlockedLots);
        Assert.Empty(result.Allocations);
        Assert.All(result.Lots,x=>Assert.Equal("Blocked",x.Status));
    }

    [Fact]
    public void Batch_finishing_after_due_is_late_but_not_falsely_on_time()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        var tuesday=Monday.AddDays(1);
        var outcome=MrpLotCapacityPreview.Evaluate(Graph(Lot(item,Monday,Monday,30)),
            [Route(item,center)],[Shift(center,tuesday)],Monday,2);
        Assert.Equal(0,outcome.OnTimeLots);
        Assert.Equal(1,outcome.LateLots);
        Assert.True(Assert.Single(outcome.Lots).Late);
        Assert.Equal(tuesday,Assert.Single(outcome.Allocations).FinishDate);
    }

    [Fact]
    public void Capacity_multiplier_changes_feasibility_without_mutating_inputs()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        var lots=Graph(Lot(item,Monday,Monday,30));
        var route=Route(item,center,10,12);
        var shift=Shift(center,Monday);
        Assert.Equal(1,MrpLotCapacityPreview.Evaluate(lots,[route],[shift],Monday,1,1m).OnTimeLots);
        Assert.Equal(1,MrpLotCapacityPreview.Evaluate(lots,[route],[shift],Monday,1,0.5m).BlockedLots);
        Assert.Equal(100m,shift.EffectiveCapacityPercent);
        Assert.Equal(30m,Assert.Single(lots.PlannedLots).Quantity);
    }
}
