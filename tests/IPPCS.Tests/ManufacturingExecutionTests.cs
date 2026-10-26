using Xunit;

public sealed class ManufacturingExecutionTests
{
    [Fact]
    public void Operations_must_be_completed_in_route_order()
    {
        var order=Order(100m);
        var first=order.Operations[0];var second=order.Operations[1];
        var blocked=ManufacturingExecutionEndpoints.TryApplyConfirmation(order,second.Id,new(60,0,0,null,null),out var error);
        Assert.False(blocked);Assert.Contains("عملیات قبلی",error);
        Assert.Equal(ManufacturingOrderStatus.Released,order.Status);
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,new(70,20,10,"LOT-A",null),out error),error);
        Assert.True(first.IsComplete);
        Assert.Equal(ManufacturingOrderStatus.InProgress,order.Status);
        Assert.False(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,second.Id,new(71,0,0,null,null),out error));
        Assert.Contains("بیشتر",error);
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,second.Id,new(65,4,1,null,null),out error),error);
        Assert.True(second.IsComplete);
        Assert.Equal(ManufacturingOrderStatus.Completed,order.Status);
        Assert.NotNull(order.CompletedUtc);
        Assert.Equal(65m,second.GoodQuantity);
    }
    [Fact]
    public void Partial_confirmations_accumulate_without_double_counting()
    {
        var order=Order(50m);var first=order.Operations[0];
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,new(15,0,0,null,null),out _));
        Assert.False(first.IsComplete);
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,new(30,5,0,null,null),out _));
        Assert.Equal(45m,first.GoodQuantity);
        Assert.Equal(5m,first.ScrapQuantity);
        Assert.True(first.IsComplete);
        Assert.False(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,new(1,0,0,null,null),out _));
    }
    [Fact]
    public void Fully_scrapped_upstream_order_closes_zero_input_downstream()
    {
        var order=Order(20m);
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,order.Operations[0].Id,
            new(0,20,0,null,null),out var error),error);
        Assert.True(order.Operations[1].IsComplete);
        Assert.Equal(ManufacturingOrderStatus.Completed,order.Status);
        Assert.Equal(0m,order.Operations[1].GoodQuantity);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(20, -1, 0)]
    public void Zero_or_negative_quantities_rejected(decimal good, decimal scrap, decimal rework)
    {
        var order=Order(100m);
        Assert.False(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,order.Operations[0].Id,
            new(good,scrap,rework,null,null),out _));
        Assert.Equal(ManufacturingOrderStatus.Released,order.Status);
    }
    [Fact]
    public void Suspended_order_does_not_accept_production_confirmation()
    {
        var order=Order(50m);order.Status=ManufacturingOrderStatus.OnHold;
        Assert.False(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,order.Operations[0].Id,
            new(5,0,0,null,null),out _));
    }
    private static ManufacturingOrder Order(decimal quantity)
    {
        return new ManufacturingOrder{PlannedQuantity=quantity,Status=ManufacturingOrderStatus.Released,
            Operations=[
                new ManufacturingOrderOperation{Sequence=10,Name="Semi-finished build"},
                new ManufacturingOrderOperation{Sequence=20,Name="Final assembly"}
            ]};
    }
}
