using Xunit;

public sealed class FactoryStageThirtyNineEcoReviewTests
{
    private static FactoryEcoApproval Request() => new()
    {
        Status=FactoryEcoApprovalStatus.Submitted,
        ItemId=Guid.NewGuid(),
        CandidateDigest=new string('A',64),
        CutoverOn=new DateOnly(2026,11,5)
    };
    [Fact]
    public void Outdated_or_changed_eco_can_be_rejected_with_independent_evidence()
    {
        var request=Request();
        var changed=new FactoryEcoPreflight(request.ItemId,"UNRELATED-DIGEST",[]);
        Assert.False(FactoryEcoApprovalWorkflow.ReviewDecisionAllowed(
            true,request,changed,new DateOnly(2026,11,1)));
        Assert.True(FactoryEcoApprovalWorkflow.ReviewDecisionAllowed(
            false,request,changed,new DateOnly(2026,11,1)));
        Assert.True(FactoryEcoApprovalWorkflow.ReviewDecisionAllowed(
            false,request,null,new DateOnly(2026,11,10)));
        Assert.False(FactoryEcoApprovalWorkflow.ReviewDecisionAllowed(
            true,request,null,new DateOnly(2026,11,10)));
    }
    [Fact]
    public void Positive_eco_approval_requires_current_issue_free_evidence()
    {
        var request=Request();
        var exact=new FactoryEcoPreflight(request.ItemId,request.CandidateDigest,[]);
        Assert.True(FactoryEcoApprovalWorkflow.ReviewDecisionAllowed(
            true,request,exact,new DateOnly(2026,11,5)));
        Assert.False(FactoryEcoApprovalWorkflow.ReviewDecisionAllowed(
            true,request,exact,new DateOnly(2026,11,6)));
        Assert.False(FactoryEcoApprovalWorkflow.ReviewDecisionAllowed(
            true,request,new FactoryEcoPreflight(Guid.NewGuid(),request.CandidateDigest,[]),
            new DateOnly(2026,11,5)));
        Assert.False(FactoryEcoApprovalWorkflow.ReviewDecisionAllowed(
            true,request,new FactoryEcoPreflight(request.ItemId,request.CandidateDigest,
                [new FactoryRevisionIssue("Pending","Missing signoff")]),
            new DateOnly(2026,11,5)));
        request.Status=FactoryEcoApprovalStatus.Rejected;
        Assert.False(FactoryEcoApprovalWorkflow.ReviewDecisionAllowed(
            false,request,exact,new DateOnly(2026,11,5)));
    }
}
