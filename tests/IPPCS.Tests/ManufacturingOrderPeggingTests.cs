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
    public void Partial_final_operation_good_output_is_attributed_before_order_completion()
    {
        var order=new ManufacturingOrder
        {
            PlannedQuantity=100,
            Status=ManufacturingOrderStatus.Released,
            Operations=
            [
                new ManufacturingOrderOperation{Sequence=10,GoodQuantity=70,IsComplete=true},
                new ManufacturingOrderOperation{Sequence=20,GoodQuantity=35,IsComplete=false}
            ]
        };

        Assert.Equal(35m,ManufacturingOrderPegging.AllocatedGoodQuantity(order,100m));
    }

    [Fact]
    public void Final_good_output_is_proportionally_split_across_explicit_mps_links()
    {
        var order=new ManufacturingOrder
        {
            PlannedQuantity=100,
            Operations=
            [
                new ManufacturingOrderOperation{Sequence=10,GoodQuantity=80},
                new ManufacturingOrderOperation{Sequence=20,GoodQuantity=60}
            ]
        };

        Assert.Equal(24m,ManufacturingOrderPegging.AllocatedGoodQuantity(order,40m));
        Assert.Equal(36m,ManufacturingOrderPegging.AllocatedGoodQuantity(order,60m));
    }

    [Fact]
    public void Final_good_output_is_capped_by_planned_and_linked_quantities()
    {
        var order=new ManufacturingOrder
        {
            PlannedQuantity=100,
            Operations=[new ManufacturingOrderOperation{Sequence=10,GoodQuantity=140}]
        };

        Assert.Equal(40m,ManufacturingOrderPegging.AllocatedGoodQuantity(order,40m));
        Assert.Equal(100m,ManufacturingOrderPegging.AllocatedGoodQuantity(order,150m));
    }

    [Fact]
    public void Missing_final_operation_and_nonpositive_quantities_produce_zero_good_output()
    {
        var order=new ManufacturingOrder{PlannedQuantity=100};
        Assert.Equal(0m,ManufacturingOrderPegging.AllocatedGoodQuantity(order,50m));
        order.Operations=[new ManufacturingOrderOperation{Sequence=10,GoodQuantity=-5}];
        Assert.Equal(0m,ManufacturingOrderPegging.AllocatedGoodQuantity(order,50m));
        Assert.Equal(0m,ManufacturingOrderPegging.AllocatedGoodQuantity(order,0m));
        order.PlannedQuantity=0;
        Assert.Equal(0m,ManufacturingOrderPegging.AllocatedGoodQuantity(order,50m));
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
