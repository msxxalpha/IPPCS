using Xunit;

public sealed class TimePhasedMaterialPeggingTests
{
    private static readonly DateOnly Baseline=new(2026,10,27);
    private static readonly Guid Item=Guid.NewGuid();
    private static BomDemandOrigin Need(Guid line,DateOnly date,decimal qty,string path="P/C") =>
        new(line,Guid.NewGuid(),Item,Guid.NewGuid(),Guid.NewGuid(),date,qty,path,1);
    private static PlanningBalance Balance(decimal physical) =>
        new(){ItemId=Item,SnapshotDate=Baseline,PhysicalQuantity=physical,
            ReservedQuantity=2,SafetyQuantity=1,QuarantinedQuantity=1};
    [Fact]
    public void Future_receipts_cannot_cover_earlier_demand()
    {
        var line=Guid.NewGuid();
        var receipt=new PlanningExpectedReceipt{Id=Guid.NewGuid(),ItemId=Item,
            Quantity=9,AvailableOn=Baseline.AddDays(2)};
        var rows=TimePhasedMaterialPegging.Allocate(
            [Need(line,Baseline.AddDays(1),10)], [Balance(9)], [receipt]);
        Assert.Equal(5m,Assert.Single(rows.Allocations).Quantity);
        Assert.Equal(5m,Assert.Single(rows.Shortages).Quantity);
    }
    [Fact]
    public void Supply_receipt_is_attributed_and_not_double_allocated()
    {
        var one=Guid.NewGuid();var two=Guid.NewGuid();
        var receipt=new PlanningExpectedReceipt{Id=Guid.NewGuid(),ItemId=Item,
            Quantity=6,AvailableOn=Baseline.AddDays(1)};
        var report=TimePhasedMaterialPegging.Allocate(
            [Need(one,Baseline.AddDays(1),8),Need(two,Baseline.AddDays(2),8)],
            [Balance(9)],[receipt]);
        Assert.Equal(11m,report.Allocations.Sum(x=>x.Quantity));
        Assert.Equal(5m,report.Shortages.Sum(x=>x.Quantity));
        Assert.Equal(6m,report.Allocations.Where(x=>x.SourceType=="ExpectedReceipt").Sum(x=>x.Quantity));
        Assert.All(report.Allocations,x=>Assert.True(x.AvailableOn<=x.NeededOn));
    }
    [Fact]
    public void Invalid_or_missing_planning_baseline_fails_closed()
    {
        var line=Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(()=>
            TimePhasedMaterialPegging.Allocate([Need(line,Baseline,1)],[],[]));
        var staleReceipt=new PlanningExpectedReceipt{ItemId=Item,Quantity=5,AvailableOn=Baseline};
        Assert.Throws<InvalidOperationException>(()=>
            TimePhasedMaterialPegging.Allocate([Need(line,Baseline,1)],[Balance(10)],[staleReceipt]));
    }
}
