using Xunit;

public sealed class FactoryEcoApprovalWorkflowTests
{
    private static (RoutingRevision Old,RoutingRevision Next) Routes()
    {
        var item=Guid.NewGuid();var center=Guid.NewGuid();
        return (
            new RoutingRevision{ItemId=item,State=RoutingState.Approved,
                EffectiveFrom=new DateOnly(2026,10,27),
                Operations=[new RoutingOperation{Sequence=10,Name="Damper welding",
                    WorkCenterId=center,RunMinutesPerUnit=2,YieldPercent=100}]},
            new RoutingRevision{ItemId=item,State=RoutingState.Draft,
                EffectiveFrom=new DateOnly(2026,10,28),
                Operations=[new RoutingOperation{Sequence=10,Name="Damper welding B",
                    WorkCenterId=center,RunMinutesPerUnit=1,YieldPercent=100}]}
        );
    }
    [Fact]
    public void ECO_requires_independent_authenticated_quality_evidence_and_exact_snapshot()
    {
        var (old,next)=Routes();
        var date=new DateOnly(2026,10,28);var digest=
            FactoryEcoApprovalWorkflow.RoutingDigest(old,next);
        Assert.Equal(digest,FactoryEcoApprovalWorkflow.RoutingDigest(old,next));
        var approval=new FactoryEcoApproval{Kind="Routing",BaselineId=old.Id,
            CandidateId=next.Id,ApprovalReference="ECO-QA-3101",
            Reason="Improved assembly jig",CutoverOn=date,CandidateDigest=digest,
            RequestedBy="engineering-planner",Status=FactoryEcoApprovalStatus.Submitted};
        bool Allowed()=>FactoryEcoApprovalWorkflow.CanExecute(approval,"Routing",
            old.Id,next.Id,date,"ECO-QA-3101","Improved assembly jig",
            FactoryEcoApprovalWorkflow.RoutingDigest(old,next));
        Assert.False(Allowed());
        approval.Status=FactoryEcoApprovalStatus.QualityApproved;
        approval.QualityReviewedBy=approval.RequestedBy;
        approval.QualityReviewedUtc=DateTime.UtcNow;
        approval.QualityEvidenceReference="QA-CHECK-001";
        Assert.False(Allowed());
        approval.QualityReviewedBy="independent-qa";
        Assert.True(Allowed());
        next.Operations[0].RunMinutesPerUnit=1.01m;
        Assert.False(Allowed()); // mutation after signature never silently applies
        next.Operations[0].RunMinutesPerUnit=1;
        Assert.False(FactoryEcoApprovalWorkflow.CanExecute(approval,"Routing",
            old.Id,next.Id,date,"ECO-QA-3101","Different reason",digest));
        approval.Status=FactoryEcoApprovalStatus.Executed;
        Assert.False(Allowed());
    }

    [Fact]
    public void BOM_hash_changes_when_a_component_coefficient_is_revised()
    {
        var parent=Guid.NewGuid();
        var old=new ManufacturingBomRevision{ParentItemId=parent,
            ValidFrom=new DateOnly(2026,10,27),State=ManufacturingRevisionState.Approved,
            Lines=[new ManufacturingBomLine{Sequence=10,ComponentItemId=Guid.NewGuid(),
                QuantityPer=2,ScrapPercent=0}]};
        var proposed=new ManufacturingBomRevision{ParentItemId=parent,
            ValidFrom=new DateOnly(2026,10,28),State=ManufacturingRevisionState.Draft,
            Lines=[new ManufacturingBomLine{Sequence=10,
                ComponentItemId=old.Lines[0].ComponentItemId,QuantityPer=2}]};
        var signed=FactoryEcoApprovalWorkflow.BomDigest(old,proposed);
        proposed.Lines[0].QuantityPer=2.1m;
        Assert.NotEqual(signed,FactoryEcoApprovalWorkflow.BomDigest(old,proposed));
    }
}
