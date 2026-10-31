using Xunit;

public sealed class FactoryStageThirtyNineEolCertificateTests
{
    private static ManufacturingConfirmation Lot(decimal good=2m) => new()
    {
        Id=Guid.Parse("10000000-0000-0000-0000-000000000001"),
        OperationId=Guid.Parse("20000000-0000-0000-0000-000000000002"),
        LotCode="FG-SHOCK-01",GoodQuantity=good,
        ScrapQuantity=1m,ReworkQuantity=0m,
        OccurredUtc=new DateTime(2026,10,28,12,0,0,DateTimeKind.Utc)
    };

    [Fact]
    public void New_certificate_seals_identity_lot_code_and_each_quantity()
    {
        var lot=Lot();
        var signature=FactoryEndOfLineAcceptance.CreateCertificate("quality-reviewer",[lot]);
        Assert.Contains("SHA256=",signature);
        Assert.True(FactoryEndOfLineAcceptance.CertificateMatches(signature,[lot]));
        Assert.True(FactoryEndOfLineAcceptance.TryReadCertificate(
            signature,out var actor,out var count,out var good));
        Assert.Equal("quality-reviewer",actor);
        Assert.Equal(1,count);
        Assert.Equal(2m,good);

        // Same total good and lot count is NOT enough to pass the new certificate.
        var wrongCode=Lot();wrongCode.LotCode="FG-SHOCK-ALTERED";
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(signature,[wrongCode]));
        var wrongScrap=Lot();wrongScrap.ScrapQuantity=5m;
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(signature,[wrongScrap]));
        var wrongId=Lot();wrongId.Id=Guid.NewGuid();
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(signature,[wrongId]));
        var wrongTime=Lot();wrongTime.OccurredUtc=wrongTime.OccurredUtc.AddMinutes(1);
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(signature,[wrongTime]));
    }

    [Fact]
    public void Digest_is_deterministic_regardless_of_database_row_order()
    {
        var a=Lot();
        var b=Lot(3);b.Id=Guid.Parse("10000000-0000-0000-0000-000000000002");
        var signed=FactoryEndOfLineAcceptance.CreateCertificate("quality-reviewer",[b,a]);
        Assert.True(FactoryEndOfLineAcceptance.CertificateMatches(signed,[a,b]));
        b.GoodQuantity=2m;
        a.GoodQuantity=3m;
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(signed,[a,b]));
    }

    [Theory]
    [InlineData("By=qa;Lots=1;Good=2;SHA256=not-hex")]
    [InlineData("By=qa;Lots=1;Good=2;SHA256=")]
    [InlineData("By=qa;Lots=1;Good=2;SHA256=FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF;Extra=fake")]
    public void Malformed_digest_or_extra_fields_are_rejected(string details)
    {
        Assert.False(FactoryEndOfLineAcceptance.CertificateMatches(details,[Lot()]));
    }

    [Fact]
    public void Existing_legacy_certificates_are_readable_but_new_approvals_are_v2()
    {
        Assert.True(FactoryEndOfLineAcceptance.CertificateMatches(
            "By=legacy-qa;Lots=1;Good=2",[Lot()]));
        Assert.Contains(";SHA256=",FactoryEndOfLineAcceptance.CreateCertificate("new-qa",[Lot()]));
        Assert.Throws<ArgumentException>(()=>
            FactoryEndOfLineAcceptance.CreateCertificate("invalid;actor",[Lot()]));
    }
}
