using Xunit;

public sealed class FactoryStageThirtyNinePriorityQueueTests
{
    private static ManufacturingConfirmation Lot() => new()
    {
        Id=Guid.NewGuid(),OperationId=Guid.NewGuid(),LotCode="FIN-01",
        GoodQuantity=2,OccurredUtc=new DateTime(2026,10,28,10,0,0,DateTimeKind.Utc)
    };

    [Fact]
    public void Certificate_triage_distinguishes_absent_duplicate_invalid_legacy_and_snapshot()
    {
        var lot=Lot();
        var signed=FactoryEndOfLineAcceptance.CreateCertificate("quality-1",[lot]);
        var valid=new ManufacturingOrderAudit{
            Action=FactoryEndOfLineAcceptance.ApprovedAction,Details=signed
        };
        Assert.Equal("Missing",FactoryReleasePriorityQueue.EolCertificateState([], [lot]));
        Assert.Equal("SnapshotV2",
            FactoryReleasePriorityQueue.EolCertificateState([valid],[lot]));
        var legacy=new ManufacturingOrderAudit{
            Action=FactoryEndOfLineAcceptance.ApprovedAction,
            Details="By=legacy-qa;Lots=1;Good=2"
        };
        Assert.Equal("LegacyTotalsOnly",
            FactoryReleasePriorityQueue.EolCertificateState([legacy],[lot]));
        Assert.Equal("Duplicate",
            FactoryReleasePriorityQueue.EolCertificateState([valid,legacy],[lot]));
        lot.LotCode="CHANGED-AFTER-APPROVAL";
        Assert.Equal("Invalid",
            FactoryReleasePriorityQueue.EolCertificateState([valid],[lot]));
    }
}
