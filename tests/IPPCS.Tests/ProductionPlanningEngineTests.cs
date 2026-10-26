using Xunit;

public sealed class ProductionPlanningEngineTests
{
    private static DateOnly Due => new(2026,11,10);

    [Fact]
    public void MRP_nets_expected_supply_then_explodes_only_planned_manufacture()
    {
        var finished=Guid.NewGuid();
        var raw=Guid.NewGuid();
        var bom=new ManufacturingBomRevision{
            ParentItemId=finished,Revision="A",State=ManufacturingRevisionState.Approved,
            ValidFrom=Due.AddMonths(-1),
            Lines=[new ManufacturingBomLine{ComponentItemId=raw,QuantityPer=2m,ScrapPercent=0}]
        };
        var plan=new MpsDemandLine{ItemId=finished,Quantity=10,NeededOn=Due};
        var balances=new []{
            new PlanningBalance{ItemId=finished,PhysicalQuantity=4,MinimumBatch=4,BatchMultiple=2,LeadDays=2,SnapshotDate=Due.AddDays(-5)},
            new PlanningBalance{ItemId=raw,PhysicalQuantity=1,MinimumBatch=1,BatchMultiple=1,LeadDays=3,SnapshotDate=Due.AddDays(-5)}
        };
        var receipts=new []{
            new PlanningExpectedReceipt{ItemId=finished,AvailableOn=Due,Quantity=3m}
        };
        var type=new Dictionary<Guid,PlanningItemType>{{finished,PlanningItemType.Finished},{raw,PlanningItemType.RawMaterial}};
        var result=PlanningCalculators.RunMrp([plan],[bom],balances,receipts,type);
        var root=Assert.Single(result.Where(x=>x.ItemId==finished));
        Assert.Equal(10m,root.GrossRequired);
        Assert.Equal(7m,root.OpeningAvailable);
        Assert.Equal(3m,root.NetShortage);
        Assert.Equal(4m,root.SuggestedQuantity); // minimum lot + lot multiple
        Assert.Equal(Due.AddDays(-2),root.SuggestedRelease);
        var material=Assert.Single(result.Where(x=>x.ItemId==raw));
        Assert.Equal(Due.AddDays(-2),material.NeededOn);
        Assert.Equal(8m,material.GrossRequired); // demand from planned 4, not whole 10
        Assert.Equal(7m,material.NetShortage);
        Assert.Equal(7m,material.SuggestedQuantity);
    }

    [Fact]
    public void MRP_rejects_cyclic_approved_BOM()
    {
        var a=Guid.NewGuid();var b=Guid.NewGuid();
        var revisions=new[]{
            new ManufacturingBomRevision{ParentItemId=a,State=ManufacturingRevisionState.Approved,ValidFrom=Due.AddDays(-10),
                Lines=[new ManufacturingBomLine{ComponentItemId=b,QuantityPer=1m}]},
            new ManufacturingBomRevision{ParentItemId=b,State=ManufacturingRevisionState.Approved,ValidFrom=Due.AddDays(-10),
                Lines=[new ManufacturingBomLine{ComponentItemId=a,QuantityPer=1m}]}
        };
        var types=new Dictionary<Guid,PlanningItemType>{{a,PlanningItemType.Finished},{b,PlanningItemType.SemiFinished}};
        Assert.Throws<InvalidOperationException>(()=>PlanningCalculators.RunMrp(
            [new MpsDemandLine{ItemId=a,Quantity=1,NeededOn=Due}],revisions,[],[],types));
    }

    [Fact]
    public void CRP_flags_overload_against_real_shift_capacity()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        var routes=new []{new RoutingRevision{
            ItemId=item,State=RoutingState.Approved,RevisionCode="A",
            EffectiveFrom=Due.AddDays(-1),
            Operations=[new RoutingOperation{WorkCenterId=center,SetupMinutes=60,RunMinutesPerUnit=5,YieldPercent=100}]
        }};
        var shifts=new []{new WorkShift{WorkCenterId=center,DayOfWeek=(int)Due.DayOfWeek,
            StartTime=new TimeOnly(8,0),EndTime=new TimeOnly(16,0),EffectiveCapacityPercent=80m}};
        var result=PlanningCalculators.RunRoughCutCrp(
            [new MpsDemandLine{ItemId=item,Quantity=100,NeededOn=Due}],routes,shifts);
        var row=Assert.Single(result);
        Assert.Equal(560m,row.RequiredMinutes);
        Assert.Equal(384m,row.CapacityMinutes);
        Assert.Equal(176m,row.OverloadMinutes);
        Assert.True(row.UtilizationPercent>100m);
    }

    [Fact]
    public void CRP_refuses_unapproved_routing()
    {
        var item=Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(()=>PlanningCalculators.RunRoughCutCrp(
            [new MpsDemandLine{ItemId=item,Quantity=4,NeededOn=Due}],[],[]));
    }
}
