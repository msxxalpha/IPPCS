using Xunit;

public sealed class ManufacturingOrderPeggingTests
{
    private static readonly DateOnly Due=new(2026,11,10);
    [Fact]
    public void Approved_mps_matching_item_quantity_and_due_date_is_linkable()
    {
        var item=Guid.NewGuid();
        var order=new ManufacturingOrder{ItemId=item,Status=ManufacturingOrderStatus.Released,
            PlannedQuantity=100,DueOn=Due};
        var line=new MpsDemandLine{ItemId=item,Quantity=120,NeededOn=Due};
        Assert.True(ManufacturingOrderPegging.IsEligible(order,line,60m,20m,30m,out var error),error);
    }
    [Fact]
    public void Cannot_overbook_order_or_mps_line()
    {
        var item=Guid.NewGuid();
        var order=new ManufacturingOrder{ItemId=item,Status=ManufacturingOrderStatus.Draft,
            PlannedQuantity=100,DueOn=Due};
        var line=new MpsDemandLine{ItemId=item,Quantity=80,NeededOn=Due};
        Assert.False(ManufacturingOrderPegging.IsEligible(order,line,70,40,0,out _));
        Assert.False(ManufacturingOrderPegging.IsEligible(order,line,70,0,20,out _));
    }
    [Fact]
    public void Item_mismatch_late_order_and_cancelled_order_are_blocked()
    {
        var order=new ManufacturingOrder{ItemId=Guid.NewGuid(),Status=ManufacturingOrderStatus.Released,
            PlannedQuantity=50,DueOn=Due.AddDays(1)};
        var line=new MpsDemandLine{ItemId=Guid.NewGuid(),Quantity=50,NeededOn=Due};
        Assert.False(ManufacturingOrderPegging.IsEligible(order,line,10,0,0,out _));
        line.ItemId=order.ItemId;
        Assert.False(ManufacturingOrderPegging.IsEligible(order,line,10,0,0,out _));
        order.DueOn=Due;order.Status=ManufacturingOrderStatus.Cancelled;
        Assert.False(ManufacturingOrderPegging.IsEligible(order,line,10,0,0,out _));
    }
}
