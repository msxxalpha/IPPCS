using Xunit;

public sealed class FactoryStageSixteenTests
{
    private static (ManufacturingOrder Order,ManufacturingBomRevision Bom,
        ManufacturingConfirmation Lot,ManufacturingLotGenealogy SourceLink,
        Guid SemiId,Guid SemiLotId) Assembly()
    {
        var semi=Guid.NewGuid();var part=Guid.NewGuid();
        var order=new ManufacturingOrder{ItemId=part,Kind=ManufacturingOrderKind.FinalAssembly,
            Number="EOL-TEST",Status=ManufacturingOrderStatus.Completed,
            PlannedQuantity=2};
        var last=new ManufacturingOrderOperation{OrderId=order.Id,Sequence=10,
            IsComplete=true,GoodQuantity=2,InspectionRequirements=[
                new ManufacturingInspectionRequirement{Mandatory=true,Code="DAMPING-FORCE",
                    Minimum=50,Maximum=60,LastPassed=true}]};
        order.Operations.Add(last);
        var bom=new ManufacturingBomRevision{ParentItemId=part,
            State=ManufacturingRevisionState.Approved,
            Lines=[new ManufacturingBomLine{ComponentItemId=semi,QuantityPer=2,
                ScrapPercent=0}]};
        order.BomRevisionId=bom.Id;
        var lot=new ManufacturingConfirmation{OperationId=last.Id,
            LotCode="ASSEMBLY-LOT",GoodQuantity=2};
        var origin=Guid.NewGuid();
        var link=new ManufacturingLotGenealogy{OrderId=order.Id,
            SourceConfirmationId=origin,DestinationConfirmationId=lot.Id,Quantity=4};
        return (order,bom,lot,link,semi,origin);
    }

    [Fact]
    public void Final_signoff_requires_complete_quality_and_real_lot_by_lot_semi_origin()
    {
        var (order,bom,lot,link,semi,source)=Assembly();
        var map=new Dictionary<Guid,Guid>{{source,semi}};
        var qualified=FactoryEndOfLineAcceptance.Evaluate(order,bom,[lot],[link],
            map,new HashSet<Guid>{semi},[],[]);
        Assert.True(qualified.CanApprove);
        Assert.Equal(2,qualified.FinalGoodQuantity);
        Assert.Equal(1,qualified.MandatoryFinalChecks);
        Assert.Equal(0,qualified.UntracedStagePairs);
        Assert.Equal(4,Assert.Single(qualified.Coverage).LinkedQuantity);
        var missing=FactoryEndOfLineAcceptance.Evaluate(order,bom,[lot],[],
            map,new HashSet<Guid>{semi},[],[]);
        Assert.False(missing.CanApprove);
        Assert.Contains(missing.Issues,x=>x.Code=="MissingSemiOrigin");
        Assert.Equal(4,Assert.Single(missing.Coverage).MissingQuantity);
    }

    [Fact]
    public void Final_signoff_fails_closed_on_open_hold_bad_final_inspection_or_duplicate_approval()
    {
        var (order,bom,lot,link,semi,source)=Assembly();
        var map=new Dictionary<Guid,Guid>{{source,semi}};
        var hold=new ManufacturingQualityHold{OrderId=order.Id,OperationId=order.Operations[0].Id,
            Status=ManufacturingQualityStatus.Open,Quantity=1};
        var blocked=FactoryEndOfLineAcceptance.Evaluate(order,bom,[lot],[link],
            map,new HashSet<Guid>{semi},[hold],[]);
        Assert.Contains(blocked.Issues,x=>x.Code=="OpenQualityHold");
        order.Operations[0].InspectionRequirements[0].LastPassed=false;
        blocked=FactoryEndOfLineAcceptance.Evaluate(order,bom,[lot],[link],
            map,new HashSet<Guid>{semi},[],[]);
        Assert.Contains(blocked.Issues,x=>x.Code=="FinalQualityChecks");
        order.Operations[0].InspectionRequirements[0].LastPassed=true;
        var signed=new ManufacturingOrderAudit{OrderId=order.Id,
            Action=FactoryEndOfLineAcceptance.ApprovedAction,
            Details="By=quality-controller;Lots=1;Good=2"};
        blocked=FactoryEndOfLineAcceptance.Evaluate(order,bom,[lot],[link],
            map,new HashSet<Guid>{semi},[],[signed]);
        Assert.True(blocked.AlreadyApproved);
        Assert.False(blocked.CanApprove);
        Assert.Equal("quality-controller",blocked.ApprovedBy);
    }

    [Fact]
    public void Final_certificate_cannot_hide_corrupted_quantity_or_duplicate_eol_signoff()
    {
        var (order,bom,lot,link,semi,origin)=Assembly();
        var source=new Dictionary<Guid,Guid>{{origin,semi}};
        var valid=new ManufacturingOrderAudit{OrderId=order.Id,
            Action=FactoryEndOfLineAcceptance.ApprovedAction,
            Details="By=qa-inspector;Lots=1;Good=2"};
        Assert.True(FactoryEndOfLineAcceptance.CertificateMatches(valid.Details,[lot]));
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(
            "By=qa-inspector;Lots=1;Good=3",[lot]));
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(
            "By=qa-inspector;Lots=2;Good=2",[lot]));
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(
            "By=;Lots=1;Good=2",[lot]));
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(
            "By=qa-inspector;Lots=1;Good=2;Extra=forged",[lot]));
        var damaged=new ManufacturingOrderAudit{OrderId=order.Id,
            Action=FactoryEndOfLineAcceptance.ApprovedAction,
            Details="By=qa-inspector;Lots=1;Good=3"};
        var report=FactoryEndOfLineAcceptance.Evaluate(order,bom,[lot],[link],
            source,new HashSet<Guid>{semi},[],[damaged]);
        Assert.Contains(report.Issues,x=>x.Code=="InvalidFinalCertificate");
        Assert.False(report.CanApprove);
        var duplicate=FactoryEndOfLineAcceptance.Evaluate(order,bom,[lot],[link],
            source,new HashSet<Guid>{semi},[],[valid,valid]);
        Assert.Contains(duplicate.Issues,x=>x.Code=="InvalidFinalCertificate");
    }

    [Fact]
    public void Internal_stage_handoff_preserves_unlinked_wip_and_requires_lot_receipt_evidence()
    {
        var order=new ManufacturingOrder{Status=ManufacturingOrderStatus.Completed,
            Kind=ManufacturingOrderKind.FinalAssembly,Number="TWO-STEPS"};
        var first=new ManufacturingOrderOperation{OrderId=order.Id,Sequence=10,
            GoodQuantity=4,IsComplete=true};
        var second=new ManufacturingOrderOperation{OrderId=order.Id,Sequence=20,
            GoodQuantity=3,IsComplete=true};
        order.Operations.AddRange([first,second]);
        var from=new ManufacturingConfirmation{OperationId=first.Id,LotCode="WIP-01",GoodQuantity=4};
        var to=new ManufacturingConfirmation{OperationId=second.Id,LotCode="ASSEMBLY-01",GoodQuantity=3};
        var link=new ManufacturingLotGenealogy{OrderId=order.Id,
            SourceConfirmationId=from.Id,DestinationConfirmationId=to.Id,Quantity=3};
        var audit=FactoryInProcessHandoffs.Analyze(order,[from,to],[link],[]);
        var row=Assert.Single(audit.Pairs);
        Assert.True(row.QualityGatePassed);
        Assert.Equal(0,audit.TotalUntracedDownstreamGood);
        Assert.Equal(1,row.RemainingUpstreamGood);
        Assert.Equal(3,Assert.Single(row.DestinationLots).DocumentedIncomingGood);
        audit=FactoryInProcessHandoffs.Analyze(order,[from,to],[],[]);
        Assert.Equal(3,audit.TotalUntracedDownstreamGood);
        Assert.Equal(1,audit.PairsWithUntracedOutput);
        var hold=new ManufacturingQualityHold{OrderId=order.Id,OperationId=first.Id,
            Quantity=1,Status=ManufacturingQualityStatus.Open};
        audit=FactoryInProcessHandoffs.Analyze(order,[from,to],[link],[hold]);
        Assert.False(audit.Pairs[0].QualityGatePassed);
    }

    [Fact]
    public void Reconstructed_stage_handoff_exposes_overissued_source_and_destination_batches()
    {
        var order=new ManufacturingOrder{Kind=ManufacturingOrderKind.FinalAssembly,
            Status=ManufacturingOrderStatus.Completed};
        var first=new ManufacturingOrderOperation{OrderId=order.Id,Sequence=10,
            GoodQuantity=3,IsComplete=true};
        var second=new ManufacturingOrderOperation{OrderId=order.Id,Sequence=20,
            GoodQuantity=4,IsComplete=true};
        order.Operations.AddRange([first,second]);
        var from=new ManufacturingConfirmation{OperationId=first.Id,
            LotCode="SOURCE",GoodQuantity=3};
        var to=new ManufacturingConfirmation{OperationId=second.Id,
            LotCode="TARGET",GoodQuantity=4};
        // Both destination coverage and total upstream evidence appear complete
        // to the old reader, but four units cannot originate from a three-unit lot.
        var over=new ManufacturingLotGenealogy{OrderId=order.Id,
            SourceConfirmationId=from.Id,DestinationConfirmationId=to.Id,Quantity=4};
        var audit=FactoryInProcessHandoffs.Analyze(order,[from,to],[over],[]);
        var pair=Assert.Single(audit.Pairs);
        Assert.Equal(1m,pair.OverAllocatedUpstreamGood);
        Assert.Equal(1,audit.PairsWithOverAllocation);
        Assert.False(pair.QualityGatePassed);
        Assert.Equal(0m,pair.DestinationLots[0].OverAllocatedGood);

        to.GoodQuantity=2;
        first.GoodQuantity=6;
        from.GoodQuantity=6;
        var destinationOver=FactoryInProcessHandoffs.Analyze(order,[from,to],[over],[]);
        Assert.Equal(2m,Assert.Single(destinationOver.Pairs[0].DestinationLots).OverAllocatedGood);
        Assert.False(destinationOver.Pairs[0].QualityGatePassed);
    }

    [Fact]
    public void Lot_transfer_cannot_exceed_good_after_quality_rejection_or_pending_quarantine()
    {
        var order=new ManufacturingOrder{Status=ManufacturingOrderStatus.InProgress};
        var first=new ManufacturingOrderOperation{OrderId=order.Id,Sequence=10,GoodQuantity=8,
            QualityRejectedQuantity=2};
        var second=new ManufacturingOrderOperation{OrderId=order.Id,Sequence=20,GoodQuantity=8};
        order.Operations.AddRange([first,second]);
        var source=new ManufacturingConfirmation{OperationId=first.Id,GoodQuantity=8,
            LotCode="FROM",OccurredUtc=new DateTime(2026,10,27,6,0,0,DateTimeKind.Utc)};
        var target=new ManufacturingConfirmation{OperationId=second.Id,GoodQuantity=8,
            LotCode="TO",OccurredUtc=source.OccurredUtc.AddMinutes(20)};
        Assert.False(ManufacturingInspectionRules.TryLinkLot(order,source,target,
            7,0,0,out _));
        Assert.True(ManufacturingInspectionRules.TryLinkLot(order,source,target,
            6,0,0,out _));
        first.QualityPendingQuantity=1;
        Assert.False(ManufacturingInspectionRules.TryLinkLot(order,source,target,
            5,0,0,out _));
    }
}
