using Xunit;
public sealed class FactoryStageNineteenTests
{
    [Fact]
    public void Timed_stop_pairs_exactly_once_and_prevents_center_overlap()
    {
        var order=Guid.NewGuid();var center=Guid.NewGuid();var op=Guid.NewGuid();
        var start=new DateTime(2026,10,27,8,0,0,DateTimeKind.Utc);
        ManufacturingOrderAudit Ev(string action,DateTime at,Guid operation,string? reason=null)=>
            new(){OrderId=order,Action=action,AtUtc=at,
                Details=action==FactoryTimedDowntime.StartAction?
                    FactoryTimedDowntime.StartDetail(operation,center,reason??"Maintenance","operator"):
                    FactoryTimedDowntime.EndDetail(operation,center,"operator")};
        var events=new[]{
            Ev(FactoryTimedDowntime.StartAction,start,op),
            Ev(FactoryTimedDowntime.StopAction,start.AddMinutes(30),op)
        };
        var session=Assert.Single(FactoryTimedDowntime.Read(events));
        Assert.Equal(start,session.StartedUtc);
        Assert.Equal(start.AddMinutes(30),session.StoppedUtc);
        Assert.Equal("Maintenance",session.Reason);
        Assert.Throws<InvalidOperationException>(()=>FactoryTimedDowntime.Read([
            events[0],Ev(FactoryTimedDowntime.StartAction,start.AddMinutes(1),Guid.NewGuid())
        ]));
        Assert.Throws<InvalidOperationException>(()=>FactoryTimedDowntime.Read([
            events[0],events[1],events[1]
        ]));
        Assert.Throws<InvalidOperationException>(()=>FactoryTimedDowntime.Read([
            Ev(FactoryTimedDowntime.StopAction,start.AddMinutes(1),op)
        ]));
    }

    [Fact]
    public void Oee_downtime_reconciliation_does_not_double_subtract_and_rejects_legacy_and_overlap()
    {
        var order=Guid.NewGuid();var center=Guid.NewGuid();var op=Guid.NewGuid();
        var at=new DateTime(2026,10,27,8,0,0,DateTimeKind.Utc);
        var day=new DateOnly(2026,10,27);
        var running=new FactoryClockSession(order,op,center,at,at.AddMinutes(60));
        var stop=new FactoryTimedStop(order,op,center,at.AddMinutes(60),
            at.AddMinutes(90),"setup adjustment","operator");
        var result=FactoryTimedDowntime.Reconcile(day,center,120,[running],[stop],0);
        Assert.True(result.IsTimeComparable);
        Assert.Equal(60,result.ClosedRunningMinutes);
        Assert.Equal(30,result.TimedStopMinutes);
        Assert.Equal(30,result.UnclassifiedMinutes);
        Assert.Equal("TimeComparable",result.Status);
        var old=FactoryTimedDowntime.Reconcile(day,center,120,[running],[stop],10);
        Assert.False(old.IsTimeComparable);
        Assert.Null(old.UnclassifiedMinutes);
        Assert.Equal("UnlocatedLegacyStop",old.Status);
        var overlapping=stop with{StartedUtc=at.AddMinutes(45)};
        result=FactoryTimedDowntime.Reconcile(day,center,120,[running],[overlapping],0);
        Assert.False(result.IsTimeComparable);
        Assert.Equal("TimeOverlap",result.Status);
        var opened=new FactoryClockSession(order,op,center,at,null);
        result=FactoryTimedDowntime.Reconcile(day,center,120,[opened],[stop],0);
        Assert.False(result.IsTimeComparable);
        Assert.Equal("OpenSession",result.Status);
    }

    [Fact]
    public void Stop_spanning_iran_midnight_is_split_into_correct_daily_minutes()
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        var local=new DateTime(2026,10,27,23,45,0);
        var utc=TimeZoneInfo.ConvertTimeToUtc(local,zone);
        var stop=new FactoryTimedStop(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            utc,utc.AddMinutes(30),"Change tool","lead");
        var d=new DateOnly(2026,10,27);
        Assert.Equal(15,FactoryTimedDowntime.Minutes(d,stop.WorkCenterId,[stop],out var open1));
        Assert.False(open1);
        Assert.Equal(15,FactoryTimedDowntime.Minutes(d.AddDays(1),stop.WorkCenterId,[stop],out var open2));
        Assert.False(open2);
    }
}
