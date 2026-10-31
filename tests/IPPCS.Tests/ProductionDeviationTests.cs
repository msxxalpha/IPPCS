using Xunit;

public sealed class ProductionDeviationTests
{
    [Fact]
    public void Mps_pegging_prevents_wrong_item_or_overallocation()
    {
        var item=Guid.NewGuid();
        var order=new ManufacturingOrder{ItemId=item,PlannedQuantity=10,
            DueOn=new DateOnly(2026,11,3)};
        var line=new MpsDemandLine{ItemId=item,Quantity=10,NeededOn=new DateOnly(2026,11,3)};
        Assert.True(ManufacturingOrderPegging.IsEligible(order,line,5,0,0,out _));
        Assert.False(ManufacturingOrderPegging.IsEligible(order,line,6,5,0,out _));
        Assert.False(ManufacturingOrderPegging.IsEligible(order,line,6,0,5,out _));
    }
    [Fact]
    public void Production_deviation_status_has_distinct_lifecycle_states()
    {
        Assert.NotEqual(ProductionDeviationStatus.Open,ProductionDeviationStatus.ActionAssigned);
        Assert.NotEqual(ProductionDeviationStatus.Closed,ProductionDeviationStatus.ActionAssigned);
    }
}
