using Xunit;

public sealed class ControlPlanAndGenealogyTests
{
    private static ManufacturingOrder Order()=>
        new(){PlannedQuantity=20,Status=ManufacturingOrderStatus.Released,
            Operations=[
                new ManufacturingOrderOperation{Sequence=10,Name="Welding"},
                new ManufacturingOrderOperation{Sequence=20,Name="Assembly"}
            ]};
    [Fact]
    public void Mandatory_measurement_blocks_downstream_until_passed()
    {
        var order=Order();
        var source=order.Operations[0];
        var dest=order.Operations[1];
        source.InspectionRequirements.Add(new ManufacturingInspectionRequirement{
            OrderOperationId=source.Id,Code="ROD-DIA",Unit="mm",Minimum=12.00m,Maximum=12.05m,Mandatory=true
        });
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,source.Id,
            new(20,0,0,"LOT-W",null),out var err),err);
        Assert.False(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,dest.Id,
            new(20,0,0,"LOT-A",null),out err));
        Assert.Contains("کنترل‌های اجباری",err);
        source.InspectionRequirements[0].LastPassed=true;
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,dest.Id,
            new(20,0,0,"LOT-A",null),out err),err);
        Assert.Equal(ManufacturingOrderStatus.Completed,order.Status);
    }
    [Fact]
    public void Mandatory_final_inspection_delays_order_completion()
    {
        var order=Order();var first=order.Operations[0];var last=order.Operations[1];
        last.InspectionRequirements.Add(new ManufacturingInspectionRequirement{
            OrderOperationId=last.Id,Code="DAMPING-FORCE",Minimum=60m,Maximum=80m,Mandatory=true
        });
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,first.Id,new(20,0,0,null,null),out _));
        Assert.True(ManufacturingExecutionEndpoints.TryApplyConfirmation(order,last.Id,new(20,0,0,null,null),out _));
        Assert.Equal(ManufacturingOrderStatus.InProgress,order.Status);
        Assert.False(ManufacturingInspectionRules.CanComplete(order));
        last.InspectionRequirements[0].LastPassed=true;
        Assert.True(ManufacturingInspectionRules.CanComplete(order));
    }
    [Fact]
    public void Boundaries_pass_and_out_of_range_fails()
    {
        var r=new ManufacturingInspectionRequirement{Minimum=99.5m,Maximum=100.5m};
        Assert.True(ManufacturingInspectionRules.Passed(r,99.5m));
        Assert.True(ManufacturingInspectionRules.Passed(r,100.5m));
        Assert.False(ManufacturingInspectionRules.Passed(r,99.499m));
        Assert.False(ManufacturingInspectionRules.Passed(r,100.501m));
    }
    [Fact]
    public void Lot_links_require_next_stage_and_non_overallocated_good_units()
    {
        var order=Order();var a=order.Operations[0];var b=order.Operations[1];
        // Persisted confirmation totals belong to frozen operation aggregates.
        // Stage16 quality-gated provenance must not trust only child lot values.
        a.GoodQuantity=12;b.GoodQuantity=10;
        var source=new ManufacturingConfirmation{OperationId=a.Id,LotCode="W-01",GoodQuantity=12,
            OccurredUtc=new DateTime(2026,10,26,10,0,0,DateTimeKind.Utc)};
        var target=new ManufacturingConfirmation{OperationId=b.Id,LotCode="A-01",GoodQuantity=10,
            OccurredUtc=new DateTime(2026,10,26,12,0,0,DateTimeKind.Utc)};
        Assert.True(ManufacturingInspectionRules.TryLinkLot(order,source,target,8,0,0,out var error),error);
        Assert.False(ManufacturingInspectionRules.TryLinkLot(order,source,target,4,9,0,out error));
        Assert.False(ManufacturingInspectionRules.TryLinkLot(order,target,source,1,0,0,out error));
        Assert.False(ManufacturingInspectionRules.TryLinkLot(order,source,target,5,0,9,out error));
    }
    [Fact]
    public void Numeric_inspection_without_any_specification_limit_fails_closed()
    {
        var requirement = new ManufacturingInspectionRequirement();
        Assert.False(ManufacturingInspectionRules.Passed(requirement, 0m));
        requirement.Minimum = 12m;
        Assert.True(ManufacturingInspectionRules.Passed(requirement, 12m));
        Assert.False(ManufacturingInspectionRules.Passed(requirement, 11.999m));
        requirement.Minimum = null;
        requirement.Maximum = 12m;
        Assert.True(ManufacturingInspectionRules.Passed(requirement, 12m));
        Assert.False(ManufacturingInspectionRules.Passed(requirement, 12.001m));
    }

    [Fact]
    public void Control_plan_requires_mandatory_characteristic_on_final_routing_operation()
    {
        var route=new RoutingRevision
        {
            Operations=[
                new RoutingOperation{Sequence=10,Name="Welding"},
                new RoutingOperation{Sequence=20,Name="Final assembly"}
            ]
        };
        var characteristics=new List<ControlPlanCharacteristic>
        {
            new(){OperationSequence=10,Mandatory=true,Code="WELD"},
            new(){OperationSequence=20,Mandatory=false,Code="VISUAL"}
        };
        Assert.False(ManufacturingInspectionRules.HasMandatoryFinalCharacteristic(characteristics,route));
        characteristics.Add(new(){OperationSequence=20,Mandatory=true,Code="FORCE"});
        Assert.True(ManufacturingInspectionRules.HasMandatoryFinalCharacteristic(characteristics,route));
        Assert.False(ManufacturingInspectionRules.HasMandatoryFinalCharacteristic(characteristics,new RoutingRevision()));
    }

    [Theory]
    [InlineData(0,false)]
    [InlineData(1,true)]
    [InlineData(2,false)]
    public void Order_release_requires_exactly_one_applicable_approved_control_plan(
        int count,bool expected)
    {
        Assert.Equal(expected,ManufacturingInspectionRules.ExactlyOneApplicableControlPlan(count));
    }

}
