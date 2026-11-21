using Xunit;

public sealed class FactoryApsMrpBridgeTests
{
    private static NetPegPlannedLot Lot(string key,Guid item,DateOnly receipt,DateOnly release,
        string type="Manufacturing",decimal quantity=10m) =>
        new(key,item,receipt,release,quantity,quantity,0,type,null,false,0);

    [Fact]
    public void APS_uses_net_manufacturing_lots_instead_of_gross_MPS_demand()
    {
        var finished=Guid.NewGuid();var raw=Guid.NewGuid();
        var day=new DateOnly(2026,10,30);
        var report=new NetPeggingReport(
            [new NetPegDemand("MPS:ROOT",finished,day,10,Guid.NewGuid(),null,null,"ROOT",0)],
            [
                new NetPegAllocation("MPS:ROOT",finished,day,"PlanningOpeningBalance","BASE",day,6,true),
                new NetPegAllocation("MPS:ROOT",finished,day,"ProposedManufacturingReceipt","PLAN:FG",day,4,false)
            ],
            [Lot("PLAN:FG",finished,day,day.AddDays(-2),quantity:4m)]);

        var bridge=FactoryApsMrpBridge.Build(report,day.AddDays(-1));

        var line=Assert.Single(bridge.DemandLines);
        Assert.Equal(4,line.Quantity);
        Assert.Equal(day,line.NeededOn);
        Assert.Equal("PLAN:FG",line.Reference);
        Assert.Equal(day.AddDays(-1),bridge.EarliestStartDates[line.Id]);
        Assert.Empty(bridge.MaterialShortages);
    }

    [Fact]
    public void APS_does_not_create_operations_when_existing_finished_supply_covers_MPS()
    {
        var finished=Guid.NewGuid();var day=new DateOnly(2026,10,30);
        var report=new NetPeggingReport(
            [new NetPegDemand("MPS:ROOT",finished,day,10,Guid.NewGuid(),null,null,"ROOT",0)],
            [new NetPegAllocation("MPS:ROOT",finished,day,"PlanningOpeningBalance","BASE",day,10,true)],
            []);

        var bridge=FactoryApsMrpBridge.Build(report,day);

        Assert.Empty(bridge.DemandLines);
        Assert.Empty(bridge.Lots);
        Assert.Empty(bridge.Prerequisites);
    }

    [Fact]
    public void Manufactured_component_lot_becomes_precedence_of_its_parent_lot()
    {
        var finished=Guid.NewGuid();var semi=Guid.NewGuid();
        var parentKey="PLAN:FG";var componentKey="PLAN:SEMI";
        var day=new DateOnly(2026,10,30);
        var report=new NetPeggingReport(
            [
                new NetPegDemand("MPS:ROOT",finished,day,10,Guid.NewGuid(),null,null,"ROOT",0),
                new NetPegDemand("COMP:FG:SEMI",semi,day.AddDays(-2),20,null,parentKey,Guid.NewGuid(),"FG/SEMI",1)
            ],
            [
                new NetPegAllocation("MPS:ROOT",finished,day,"ProposedManufacturingReceipt",parentKey,day,10,false),
                new NetPegAllocation("COMP:FG:SEMI",semi,day.AddDays(-2),"ProposedManufacturingReceipt",componentKey,day.AddDays(-2),20,false)
            ],
            [
                Lot(parentKey,finished,day,day.AddDays(-3)),
                Lot(componentKey,semi,day.AddDays(-2),day.AddDays(-4))
            ]);

        var bridge=FactoryApsMrpBridge.Build(report,day.AddDays(-5));

        var parent=bridge.DemandLines.Single(x=>x.Reference==parentKey);
        var component=bridge.DemandLines.Single(x=>x.Reference==componentKey);
        Assert.Contains(component.Id,bridge.Prerequisites[parent.Id]);
        Assert.Equal(day.AddDays(-4),bridge.EarliestStartDates[component.Id]);
    }

    [Fact]
    public void Proposed_external_component_receipt_is_reported_as_material_shortage()
    {
        var finished=Guid.NewGuid();var raw=Guid.NewGuid();
        var parentKey="PLAN:FG";var day=new DateOnly(2026,10,30);
        var report=new NetPeggingReport(
            [new NetPegDemand("COMP:FG:RAW",raw,day.AddDays(-2),7,null,parentKey,Guid.NewGuid(),"FG/RAW",1)],
            [new NetPegAllocation("COMP:FG:RAW",raw,day.AddDays(-2),
                "ProposedExternalReceipt","PLAN:RAW",day.AddDays(-2),7,false)],
            [Lot(parentKey,finished,day,day.AddDays(-3)),
             Lot("PLAN:RAW",raw,day.AddDays(-2),day.AddDays(-4),"ExternalPlanningOnly",7m)]);

        var bridge=FactoryApsMrpBridge.Build(report,day.AddDays(-5));

        var shortage=Assert.Single(bridge.MaterialShortages);
        Assert.Equal(raw,shortage.ItemId);
        Assert.Equal(7,shortage.ProposedExternalQuantity);
        Assert.Equal(parentKey,shortage.ParentLotKey);
        Assert.Single(bridge.DemandLines);
        Assert.DoesNotContain(bridge.DemandLines,x=>x.ItemId==raw);
    }
}
