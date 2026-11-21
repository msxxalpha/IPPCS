using Xunit;

public sealed class FactoryStageThirtySevenSerialQualityTests
{
    [Fact]
    public void Serial_journal_must_be_after_the_internal_end_of_line_approval()
    {
        var order=Guid.NewGuid();
        var approved=new ManufacturingOrderAudit{
            OrderId=order,Action=FactoryEndOfLineAcceptance.ApprovedAction,
            AtUtc=new DateTime(2026,10,27,9,0,0,DateTimeKind.Utc)
        };
        var entry=new ManufacturingOrderAudit{
            OrderId=order,Action=FactorySerialGenealogy.AssignmentAction,
            AtUtc=approved.AtUtc.AddMinutes(1)
        };
        Assert.True(FactorySerialGenealogy.CertificateChronologyValid([entry],[approved]));
        entry.AtUtc=approved.AtUtc.AddTicks(-1);
        Assert.False(FactorySerialGenealogy.CertificateChronologyValid([entry],[approved]));
        Assert.False(FactorySerialGenealogy.CertificateChronologyValid([entry],[]));
    }

    [Fact]
    public void Duplicate_internal_eol_signatures_cannot_legitimize_serial_trace()
    {
        var id=Guid.NewGuid();
        var approved=new ManufacturingOrderAudit{
            OrderId=id,Action=FactoryEndOfLineAcceptance.ApprovedAction,
            AtUtc=new DateTime(2026,10,27,9,0,0,DateTimeKind.Utc)
        };
        var second=new ManufacturingOrderAudit{
            OrderId=id,Action=FactoryEndOfLineAcceptance.ApprovedAction,
            AtUtc=approved.AtUtc.AddMinutes(1)
        };
        var serial=new ManufacturingOrderAudit{
            OrderId=id,Action=FactorySerialGenealogy.AssignmentAction,
            AtUtc=second.AtUtc.AddMinutes(1)
        };
        Assert.False(FactorySerialGenealogy.CertificateChronologyValid(
            [serial],[approved,second]));
    }
}
