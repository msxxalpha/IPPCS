using Xunit;

public sealed class MaterialConstrainedPlanningTests
{
    private static readonly DateOnly Start=new(2026,10,26);
    private static (MpsDemandLine line,ManufacturingBomRevision bom,Guid raw,
        Dictionary<Guid,PlanningItemType> types) Setup(decimal demand=10)
    {
        var product=Guid.NewGuid();var raw=Guid.NewGuid();
        var line=new MpsDemandLine{Id=Guid.NewGuid(),ItemId=product,Quantity=demand,NeededOn=Start.AddDays(3)};
        var bom=new ManufacturingBomRevision{
            ParentItemId=product,Revision="A",State=ManufacturingRevisionState.Approved,
            ValidFrom=Start.AddDays(-10),
            Lines=[new ManufacturingBomLine{ComponentItemId=raw,QuantityPer=2,ScrapPercent=0}]
        };
        return(line,bom,raw,new(){{product,PlanningItemType.Finished},{raw,PlanningItemType.RawMaterial}});
    }
    [Fact]
    public void Adequate_manual_material_balance_allows_finite_schedule_preview()
    {
        var x=Setup();var b=new PlanningBalance{ItemId=x.raw,PhysicalQuantity=25,SnapshotDate=Start};
        var r=Assert.Single(MaterialConstrainedPlanning.Check([x.line],[x.bom],[b],[],x.types,Start));
        Assert.True(r.Feasible);
    }
    [Fact]
    public void Quality_quarantine_and_safety_stock_block_unsafe_schedule()
    {
        var x=Setup();var b=new PlanningBalance{ItemId=x.raw,PhysicalQuantity=25,
            QuarantinedQuantity=4,SafetyQuantity=3,SnapshotDate=Start};
        var r=Assert.Single(MaterialConstrainedPlanning.Check([x.line],[x.bom],[b],[],x.types,Start));
        Assert.False(r.Feasible);
    }
    [Fact]
    public void Later_receipt_is_not_assumed_available_at_schedule_start()
    {
        var x=Setup();var b=new PlanningBalance{ItemId=x.raw,PhysicalQuantity=5,SnapshotDate=Start.AddDays(-1)};
        var late=new PlanningExpectedReceipt{ItemId=x.raw,Quantity=20,AvailableOn=Start.AddDays(1)};
        Assert.False(Assert.Single(MaterialConstrainedPlanning.Check([x.line],[x.bom],[b],[late],x.types,Start)).Feasible);
        late.AvailableOn=Start;
        Assert.True(Assert.Single(MaterialConstrainedPlanning.Check([x.line],[x.bom],[b],[late],x.types,Start)).Feasible);
    }
    [Fact]
    public void Two_finished_demands_cannot_double_allocate_the_same_raw_parts()
    {
        var x=Setup();var b=new PlanningBalance{ItemId=x.raw,PhysicalQuantity=30,SnapshotDate=Start};
        var other=new MpsDemandLine{Id=Guid.NewGuid(),ItemId=x.line.ItemId,Quantity=10,NeededOn=Start.AddDays(4)};
        var result=MaterialConstrainedPlanning.Check([x.line,other],[x.bom],[b],[],x.types,Start);
        Assert.Equal(2,result.Count);
        Assert.True(result[0].Feasible);
        Assert.False(result[1].Feasible);
    }
    [Fact]
    public void Infeasible_earlier_demand_still_reserves_its_material_priority()
    {
        var x=Setup(demand:10); // requires 20 units of raw material
        var earlier=x.line;
        earlier.NeededOn=Start.AddDays(1);
        var later=new MpsDemandLine{Id=Guid.NewGuid(),ItemId=x.line.ItemId,
            Quantity=2,NeededOn=Start.AddDays(2)}; // requires 4 more units
        var balance=new PlanningBalance{ItemId=x.raw,PhysicalQuantity=10,SnapshotDate=Start};
        var results=MaterialConstrainedPlanning.Check([earlier,later],[x.bom],[balance],[],x.types,Start);
        Assert.False(results[0].Feasible);
        Assert.False(results[1].Feasible);
    }
    [Fact]
    public void Invalid_or_duplicate_planning_supply_fails_closed()
    {
        var x=Setup();
        var balance=new PlanningBalance{ItemId=x.raw,PhysicalQuantity=25,SnapshotDate=Start};
        var invalidReceipt=new PlanningExpectedReceipt{ItemId=x.raw,Quantity=-1,AvailableOn=Start.AddDays(1)};
        Assert.Throws<InvalidOperationException>(()=>
            MaterialConstrainedPlanning.Check([x.line],[x.bom],[balance],[invalidReceipt],x.types,Start));
        Assert.Throws<InvalidOperationException>(()=>
            MaterialConstrainedPlanning.Check([x.line],[x.bom],[balance,balance],[],x.types,Start));
    }
    [Fact]
    public void Missing_approved_bom_or_planning_balance_fails_closed()
    {
        var x=Setup();
        Assert.False(Assert.Single(MaterialConstrainedPlanning.Check([x.line],[],[],[],x.types,Start)).Feasible);
        Assert.False(Assert.Single(MaterialConstrainedPlanning.Check([x.line],[x.bom],[],[],x.types,Start)).Feasible);
    }
}