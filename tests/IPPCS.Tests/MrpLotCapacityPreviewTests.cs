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
    public void Parent_batch_cannot_start_until_its_manufactured_component_finishes()
    {
        var semi=Guid.NewGuid();var finished=Guid.NewGuid();var center=Guid.NewGuid();
        var child=Lot(semi,Monday,Monday,20);
        var parent=Lot(finished,Monday,Monday,10);
        var need=new NetPegDemand("COMP:"+parent.LotKey+":1",semi,Monday,10,
            null,parent.LotKey,Guid.NewGuid(),"finished/semi",1);
        var source=new NetPegAllocation(need.DemandKey,semi,Monday,
            "ProposedManufacturingReceipt",child.LotKey,Monday,10,false);
        var graph=new NetPeggingReport([need],[source],[parent,child]);
        var result=MrpLotCapacityPreview.Evaluate(graph,
            [Route(semi,center,10,5),Route(finished,center,10,5)],
            [Shift(center,Monday)],Monday,1);
        Assert.Equal(2,result.OnTimeLots);
        var first=Assert.Single(result.Allocations,x=>x.ItemId==semi);
        var second=Assert.Single(result.Allocations,x=>x.ItemId==finished);
        Assert.True(first.FinishesAt<=second.StartsAt);
    }

    [Fact]
    public void Failed_component_batch_blocks_parent_instead_of_using_unfinished_material()
    {
        var semi=Guid.NewGuid();var finished=Guid.NewGuid();var center=Guid.NewGuid();
        var child=Lot(semi,Monday,Monday,100);var parent=Lot(finished,Monday,Monday,5);
        var need=new NetPegDemand("COMP:"+parent.LotKey+":1",semi,Monday,5,
            null,parent.LotKey,Guid.NewGuid(),"finished/semi",1);
        var source=new NetPegAllocation(need.DemandKey,semi,Monday,
            "ProposedManufacturingReceipt",child.LotKey,Monday,5,false);
        var result=MrpLotCapacityPreview.Evaluate(new NetPeggingReport([need],[source],[child,parent]),
            [Route(semi,center,10,10),Route(finished,center,10,1)],
            [Shift(center,Monday)],Monday,1);
        Assert.Equal(2,result.BlockedLots);
        Assert.Empty(result.Allocations);
        Assert.Contains(result.Lots,x=>x.ItemId==finished&&x.Status=="Unscheduled"&&
            x.Reason!.Contains("پیش‌نیاز"));
    }

    [Fact]
    public void Unfinished_active_order_conservatively_reserves_its_center_but_draft_does_not()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        var active=new ManufacturingOrder {
            Status=ManufacturingOrderStatus.InProgress,
            PlannedStart=Monday.AddDays(-2),DueOn=Monday,
            Operations=[new ManufacturingOrderOperation{WorkCenterId=center,Sequence=10,IsComplete=false}]
        };
        var result=MrpLotCapacityPreview.Evaluate(Graph(Lot(item,Monday,Monday,10)),
            [Route(item,center)],[Shift(center,Monday)],Monday,1,1m,[active]);
        Assert.Equal(1,result.BlockedLots);
        Assert.Empty(result.Allocations);
        active.Status=ManufacturingOrderStatus.Draft;
        var draft=MrpLotCapacityPreview.Evaluate(Graph(Lot(item,Monday,Monday,10)),
            [Route(item,center)],[Shift(center,Monday)],Monday,1,1m,[active]);
        Assert.Equal(1,draft.OnTimeLots);
    }

    [Fact]
    public void Completed_operations_of_active_order_do_not_reserve_workcenter()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        var active=new ManufacturingOrder {
            Status=ManufacturingOrderStatus.OnHold, PlannedStart=Monday,DueOn=Monday,
            Operations=[new ManufacturingOrderOperation{WorkCenterId=center,Sequence=10,IsComplete=true}]
        };
        var report=MrpLotCapacityPreview.Evaluate(Graph(Lot(item,Monday,Monday,10)),
            [Route(item,center)],[Shift(center,Monday)],Monday,1,1m,[active]);
        Assert.Equal(1,report.OnTimeLots);
    }

    [Fact]
    public void Two_lots_of_same_item_and_due_day_remain_individually_attributed()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        var a=Lot(item,Monday,Monday,10);
        var b=Lot(item,Monday,Monday,10) with{LotKey="PLAN:second-batch"};
        var result=MrpLotCapacityPreview.Evaluate(Graph(a,b),
            [Route(item,center,10,10)],[Shift(center,Monday)],Monday,1);
        Assert.Equal(2,result.OnTimeLots);
        Assert.Equal(2,result.Allocations.Select(x=>x.DemandLineId).Distinct().Count());
    }

    [Fact]
    public void Cyclic_lot_dependencies_are_rejected()
    {
        var a=Lot(Guid.NewGuid(),Monday,Monday);var b=Lot(Guid.NewGuid(),Monday,Monday);
        NetPegDemand Need(NetPegPlannedLot parent)=>
            new("COMP:"+parent.LotKey,parent.ItemId,Monday,1,null,parent.LotKey,null,"loop",1);
        var d1=Need(a);var d2=Need(b);
        var graph=new NetPeggingReport([d1,d2],[
            new NetPegAllocation(d1.DemandKey,a.ItemId,Monday,
                "ProposedManufacturingReceipt",b.LotKey,Monday,1,false),
            new NetPegAllocation(d2.DemandKey,b.ItemId,Monday,
                "ProposedManufacturingReceipt",a.LotKey,Monday,1,false)
        ],[a,b]);
        var center=Guid.NewGuid();
        Assert.Throws<ArgumentException>(()=>MrpLotCapacityPreview.Evaluate(graph,
            [Route(a.ItemId,center),Route(b.ItemId,center)],[Shift(center,Monday)],Monday,1));
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
