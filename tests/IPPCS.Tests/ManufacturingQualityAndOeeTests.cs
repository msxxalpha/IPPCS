using Xunit;

public sealed class ManufacturingQualityAndOeeTests
{
    private static ManufacturingOrder ActiveOrder(decimal qty=100m)=>new(){
        PlannedQuantity=qty,Status=ManufacturingOrderStatus.Released,
        Operations=[
            new ManufacturingOrderOperation{Sequence=10,Name="Tube welding"},
            new ManufacturingOrderOperation{Sequence=20,Name="Damper assembly"}
        ]
    };

    [Fact]
    public void Quality_containment_is_accepted_after_operator_suspends_an_active_order()
    {
        var order=ActiveOrder(10m);
        var first=order.Operations[0];
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,
            new(3m,0m,0m,"HOLD-LOT",null),out _));
        order.ResumeStatus=order.Status;
        order.Status=ManufacturingOrderStatus.OnHold;
        Assert.True(ManufacturingPerformance.CanHold(order,first.Id,2m,out var reason),reason);
        // Still cannot record production while an operator has stopped the line.
        Assert.False(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,
            new(1m,0m,0m,"HOLD-LOT",null),out _));
    }

    [Fact]
    public void Quality_containment_on_invalid_suspension_or_finished_order_is_rejected()
    {
        var order=ActiveOrder(10m);
        var first=order.Operations[0];
        first.GoodQuantity=5;
        order.Status=ManufacturingOrderStatus.OnHold;
        order.ResumeStatus=ManufacturingOrderStatus.Draft;
        Assert.False(ManufacturingPerformance.CanHold(order,first.Id,1m,out _));
        order.Status=ManufacturingOrderStatus.Cancelled;
        order.ResumeStatus=ManufacturingOrderStatus.InProgress;
        Assert.False(ManufacturingPerformance.CanHold(order,first.Id,1m,out _));
        order.Status=ManufacturingOrderStatus.Completed;
        Assert.False(ManufacturingPerformance.CanHold(order,first.Id,1m,out _));
    }

    [Fact]
    public void Quality_hold_blocks_downstream_until_release()
    {
        var order=ActiveOrder();
        var first=order.Operations[0];
        var second=order.Operations[1];
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,
            new(90m,10m,0m,"L-100",null),out var error),error);
        Assert.True(ManufacturingPerformance.CanHold(order,first.Id,20m,out error),error);
        first.QualityPendingQuantity+=20m;
        var hold=new ManufacturingQualityHold{OrderId=order.Id,OperationId=first.Id,Quantity=20m};
        Assert.False(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,second.Id,
            new(70m,0m,0m,null,null),out error));
        Assert.Contains("تعیین تکلیف",error);
        Assert.True(ManufacturingPerformance.TryResolveQuality(order,hold,ManufacturingQualityStatus.Released,out error),error);
        Assert.Equal(ManufacturingQualityStatus.Released,hold.Status);
        Assert.Equal(0m,first.QualityPendingQuantity);
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,second.Id,
            new(90m,0m,0m,null,null),out error),error);
        Assert.Equal(ManufacturingOrderStatus.Completed,order.Status);
    }

    [Fact]
    public void Quality_scrap_reduces_next_operation_allowed_quantity()
    {
        var order=ActiveOrder(40m);
        var first=order.Operations[0];
        var next=order.Operations[1];
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,new(40,0,0,null,null),out _));
        Assert.True(ManufacturingPerformance.CanHold(order,first.Id,6,out _));
        first.QualityPendingQuantity=6m;
        var hold=new ManufacturingQualityHold{OrderId=order.Id,OperationId=first.Id,Quantity=6m};
        Assert.True(ManufacturingPerformance.TryResolveQuality(order,hold,ManufacturingQualityStatus.Scrapped,out _));
        Assert.Equal(6m,first.QualityRejectedQuantity);
        Assert.False(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,next.Id,new(35,0,0,null,null),out _));
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,next.Id,new(34,0,0,null,null),out _));
        Assert.True(next.IsComplete);
        Assert.Equal(ManufacturingOrderStatus.Completed,order.Status);
    }

    [Fact]
    public void Quality_hold_rejected_after_downstream_activity()
    {
        var order=ActiveOrder(20m);var first=order.Operations[0];var next=order.Operations[1];
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,new(20,0,0,null,null),out _));
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,next.Id,new(5,0,0,null,null),out _));
        Assert.False(ManufacturingPerformance.CanHold(order,first.Id,3,out _));
    }

    [Fact]
    public void Scrapping_all_held_good_parts_closes_zero_input_operation()
    {
        var order=ActiveOrder(10m);var first=order.Operations[0];
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,new(10,0,0,null,null),out _));
        Assert.True(ManufacturingPerformance.CanHold(order,first.Id,10,out _));
        first.QualityPendingQuantity=10m;
        var hold=new ManufacturingQualityHold{OrderId=order.Id,OperationId=first.Id,Quantity=10};
        Assert.True(ManufacturingPerformance.TryResolveQuality(order,hold,ManufacturingQualityStatus.Scrapped,out _));
        Assert.True(order.Operations[1].IsComplete);
        Assert.Equal(ManufacturingOrderStatus.Completed,order.Status);
    }

    [Fact]
    public void Wip_is_work_center_flow_not_warehouse_stock()
    {
        var order=ActiveOrder(20m);
        order.Operations[0].GoodQuantity=16;
        order.Operations[0].ScrapQuantity=4;
        order.Operations[0].QualityRejectedQuantity=2;
        order.Operations[1].GoodQuantity=6;
        var rows=ManufacturingPerformance.CalculateWip(order);
        Assert.Equal(14m,rows[0].ForwardAvailable);
        Assert.Equal(6m,rows[0].NextStageReported);
        Assert.Equal(8m,rows[0].AwaitingNextOperation);
    }

    [Fact]
    public void Oee_correctly_multiplies_availability_performance_and_quality()
    {
        var oee=ManufacturingPerformance.CalculateOee(480m,60m,336m,90m,10m,0m);
        Assert.Equal(87.5m,oee.AvailabilityPercent);
        Assert.Equal(80m,oee.PerformancePercent);
        Assert.Equal(90m,oee.QualityPercent);
        Assert.Equal(63m,oee.OeePercent);
        Assert.Null(oee.Warning);
    }

    [Fact]
    public void Oee_does_not_fabricate_zero_for_missing_planned_time()
    {
        var missing=ManufacturingPerformance.CalculateOee(null,0,20,10,0,0);
        Assert.Null(missing.OeePercent);
        Assert.NotNull(missing.Warning);
        var bad=ManufacturingPerformance.CalculateOee(60,90,20,10,0,0);
        Assert.Null(bad.OeePercent);
        var suspicious=ManufacturingPerformance.CalculateOee(60,0,80,10,0,0);
        Assert.NotNull(suspicious.Warning);
    }
}
