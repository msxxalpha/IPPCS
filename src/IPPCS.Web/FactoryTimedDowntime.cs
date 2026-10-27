using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Separate, immutable timestamps for clock-safe production center downtime.
// These are manual observations only, not PLC or maintenance system data.
public record FactoryStopStartInput(Guid OperationId,string Reason);
public record FactoryTimedStop(Guid OrderId,Guid OperationId,Guid WorkCenterId,
    DateTime StartedUtc,DateTime? StoppedUtc,string Reason,string RecordedBy);
public record FactoryDowntimeBalance(Guid WorkCenterId,DateOnly ProductionDate,
    decimal? PlannedMinutes,decimal ClosedRunningMinutes,decimal? TimedStopMinutes,
    decimal LegacyUntimedMinutes,decimal? UnclassifiedMinutes,
    bool HasOpenClock,bool HasOpenStop,bool IsTimeComparable,string Status,string Warning);

public static class FactoryTimedDowntime
{
    public const string StartAction="TimedDowntimeStart";
    public const string StopAction="TimedDowntimeStop";
    public const int MaxEvents=20000;
    private static readonly TimeZoneInfo Tehran=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public static string StartDetail(Guid operationId,Guid centerId,string reason,string actor)=>
        operationId.ToString("N")+"|"+centerId.ToString("N")+"|"+actor+"|"+reason;
    public static string EndDetail(Guid operationId,Guid centerId,string actor)=>
        operationId.ToString("N")+"|"+centerId.ToString("N")+"|"+actor;

    public static IReadOnlyList<FactoryTimedStop> Read(
        IEnumerable<ManufacturingOrderAudit> history)
    {
        var stops=new List<FactoryTimedStop>();
        var open=new Dictionary<Guid,FactoryTimedStop>();
        foreach(var e in history.Where(x=>x.Action is StartAction or StopAction)
            .OrderBy(x=>x.AtUtc).ThenBy(x=>x.Action==StopAction?0:1).ThenBy(x=>x.Id))
        {
            var parts=(e.Details??"").Split('|');
            if(parts.Length!=(e.Action==StartAction?4:3)||
                !Guid.TryParseExact(parts[0],"N",out var op)||
                !Guid.TryParseExact(parts[1],"N",out var center)||
                op==Guid.Empty||center==Guid.Empty||
                string.IsNullOrWhiteSpace(parts[2])||
                (e.Action==StartAction&&string.IsNullOrWhiteSpace(parts[3]))||
                e.AtUtc.Kind==DateTimeKind.Local)
                throw new InvalidOperationException("سند شروع/پایان توقف دارای قالب نامعتبر است.");
            var stamp=DateTime.SpecifyKind(e.AtUtc,DateTimeKind.Utc);
            if(e.Action==StartAction)
            {
                if(open.ContainsKey(op)||open.Values.Any(x=>x.WorkCenterId==center))
                    throw new InvalidOperationException("توقف هم‌پوشان یا تکراری در یک مرکز کاری ثبت شده است.");
                var row=new FactoryTimedStop(e.OrderId,op,center,stamp,null,parts[3],parts[2]);
                open.Add(op,row);stops.Add(row);
            }
            else
            {
                if(!open.TryGetValue(op,out var start)||start.WorkCenterId!=center||
                    start.OrderId!=e.OrderId||stamp<=start.StartedUtc)
                    throw new InvalidOperationException("توقف پایان‌یافته فاقد شروع معتبر است.");
                var idx=stops.FindLastIndex(x=>x.OperationId==op&&x.StoppedUtc is null);
                if(idx<0)throw new InvalidOperationException("سابقه توقف باز پیدا نشد.");
                stops[idx]=start with{StoppedUtc=stamp};open.Remove(op);
            }
        }
        foreach(var group in stops.GroupBy(x=>x.WorkCenterId))
        {
            var items=group.OrderBy(x=>x.StartedUtc).ToArray();
            for(var i=1;i<items.Length;i++)
                if(items[i-1].StoppedUtc is not { } previousStop||
                   previousStop>items[i].StartedUtc)
                    throw new InvalidOperationException("تداخل زمان توقف در مرکز کاری شناسایی شد.");
        }
        return stops;
    }

    public static decimal? Minutes(DateOnly day,Guid center,
        IReadOnlyList<FactoryTimedStop> stops,out bool isOpen)
    {
        var start=TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue),Tehran);
        var end=TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue),Tehran);
        isOpen=false;decimal total=0;
        foreach(var stop in stops.Where(x=>x.WorkCenterId==center))
        {
            if(stop.StartedUtc>=end||(stop.StoppedUtc.HasValue&&stop.StoppedUtc<=start))continue;
            if(stop.StoppedUtc is null){isOpen=true;continue;}
            var a=stop.StartedUtc>start?stop.StartedUtc:start;
            var b=stop.StoppedUtc.Value<end?stop.StoppedUtc.Value:end;
            if(b>a)total+=(decimal)(b-a).TotalMinutes;
        }
        return isOpen?null:Math.Round(total,2);
    }

    public static FactoryDowntimeBalance Reconcile(DateOnly day,Guid center,decimal? planned,
        IReadOnlyList<FactoryClockSession> clocks,IReadOnlyList<FactoryTimedStop> stops,
        decimal legacyMinutes)
    {
        if(legacyMinutes<0)throw new ArgumentOutOfRangeException(nameof(legacyMinutes));
        var measured=FactoryProductionIntelligence.EvaluateClockOee(center,day,planned,
            clocks,0,0,0,0);
        var timed=Minutes(day,center,stops,out var openStop);
        var start=TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue),Tehran);
        var end=TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue),Tehran);
        // A stopped machine interval cannot overlap a manually marked running
        // operation. Closed stop + open clock is also a conflict.
        var overlap=stops.Any(s=>s.WorkCenterId==center&&
            clocks.Any(c=>c.WorkCenterId==center &&
                s.StartedUtc<(c.StoppedUtc??end) &&
                c.StartedUtc<(s.StoppedUtc??end) &&
                s.StartedUtc<end && (s.StoppedUtc??end)>start));
        var valid=planned.HasValue&&planned>0&&!measured.HasOpenClock&&!openStop&&
            timed.HasValue&&legacyMinutes==0&&!overlap&&
            measured.ActualMinutes+timed.Value<=planned.Value+0.01m;
        var residual=valid?Math.Round(planned!.Value-measured.ActualMinutes-timed!.Value,2):(decimal?)null;
        var status=!planned.HasValue?"NoDailyPlan":measured.HasOpenClock||openStop?
            "OpenSession":overlap?"TimeOverlap":legacyMinutes>0?"UnlocatedLegacyStop":
            measured.ActualMinutes+timed.GetValueOrDefault()>planned?"ExceedsPlanned":
            valid?"TimeComparable":"Unverified";
        return new(center,day,planned,measured.ActualMinutes,timed,
            legacyMinutes,residual,measured.HasOpenClock,openStop,valid,status,
            valid?"زمان کار و توقف‌های زمان‌دار بدون تداخل ثبت شده‌اند؛ باقی‌مانده الزاماً توقف نیست و ممکن است زمان تنظیم، انتظار یا خارج از برنامه باشد.":
            "OEE مبتنی بر توقف هنوز قابل تطبیق قطعی نیست: برنامه روزانه، ساعت باز، توقف قدیمی بدون بازه، تداخل یا ثبت بیش از برنامه را بررسی کنید.");
    }

    private static async Task<(IReadOnlyList<FactoryTimedStop> Rows,string? Error)>
        ReadJournal(ProductionDb db,CancellationToken ct)
    {
        var events=await db.ManufacturingOrderAudits.AsNoTracking()
            .Where(x=>x.Action==StartAction||x.Action==StopAction)
            .OrderBy(x=>x.AtUtc).Take(MaxEvents+1).ToListAsync(ct);
        if(events.Count>MaxEvents)
            return ([], "حجم توقفات بیش از سقف خواندن دقیق است؛ گزارش ناقص ارائه نمی‌شود.");
        try{return (Read(events),null);}
        catch(InvalidOperationException ex){return ([],ex.Message);}
    }

    public static IEndpointRouteBuilder MapTimedDowntime(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/manufacturing-orders/{id:guid}/timed-downtime",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
                if(!await db.ManufacturingOrders.AsNoTracking().AnyAsync(x=>x.Id==id,ct))
                    return Results.NotFound();
                var (rows,error)=await ReadJournal(db,ct);
                return error is null?Results.Ok(new{orderId=id,
                    rows=rows.Where(x=>x.OrderId==id).ToArray(),
                    warning="زمان‌های توقف فقط مشاهدات دستی ثبت‌شده IPPCS هستند؛ سیستم به PLC یا تعمیرات متصل نیست."}):
                    Results.Conflict(new{message=error});
            });
        api.MapPost("/manufacturing-orders/{id:guid}/timed-downtime/start",
            async(Guid id,FactoryStopStartInput input,ProductionDb db,
                HttpContext ctx,CancellationToken ct)=>{
                if(input.OperationId==Guid.Empty||string.IsNullOrWhiteSpace(input.Reason)||
                    input.Reason.Trim().Length>150||input.Reason.Contains('|'))
                    return Results.BadRequest(new{message="عملیات و علت توقف (بدون |) الزامی است."});
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{await FactoryCapacityReservations.LockOrder(db,id,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل دستور در دسترس نیست."});}
                var order=await db.ManufacturingOrders.AsNoTracking()
                    .Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(order is null)return Results.NotFound();
                if(!ManufacturingExecutionEndpoints.IsActive(order.Status))
                    return Results.Conflict(new{message="دستور برای ثبت شروع توقف فعال نیست."});
                var op=order.Operations.SingleOrDefault(x=>x.Id==input.OperationId);
                if(op is null)return Results.NotFound();
                try{await FactoryCapacityReservations.LockCenter(db,op.WorkCenterId,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل مرکز کاری در دسترس نیست."});}
                var (stops,error)=await ReadJournal(db,ct);
                if(error is not null)return Results.Conflict(new{message=error});
                if(stops.Any(x=>x.StoppedUtc is null &&
                    (x.WorkCenterId==op.WorkCenterId||x.OrderId==id)))
                    return Results.Conflict(new{message="توقف باز دیگری برای مرکز یا دستور ثبت شده است."});
                var clocks=await db.ManufacturingOrderAudits.AsNoTracking()
                    .Where(x=>x.Action==FactoryOperationClock.Started||
                              x.Action==FactoryOperationClock.Stopped).ToListAsync(ct);
                try{
                    if(FactoryOperationClock.Read(clocks).Any(x=>x.StoppedUtc is null&&
                        (x.WorkCenterId==op.WorkCenterId||x.OrderId==id)))
                        return Results.Conflict(new{message="ساعت کار باز است؛ ابتدا ساعت عملیات را پایان دهید."});
                }catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
                var actor=(ctx.User.Identity?.Name??"unknown").Replace("|","_");
                if(actor.Length>90)actor=actor[..90];
                var now=DateTime.UtcNow;
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                    OrderId=id,Action=StartAction,AtUtc=now,
                    Details=StartDetail(op.Id,op.WorkCenterId,input.Reason.Trim(),actor)});
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Ok(new{orderId=id,operationId=op.Id,startedUtc=now,isOpen=true});
            }).RequireAuthorization("IpShopFloorWrite");
        api.MapPost("/manufacturing-orders/{id:guid}/timed-downtime/{operationId:guid}/stop",
            async(Guid id,Guid operationId,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{await FactoryCapacityReservations.LockOrder(db,id,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل دستور در دسترس نیست."});}
                var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(order is null)return Results.NotFound();
                var op=order.Operations.SingleOrDefault(x=>x.Id==operationId);
                if(op is null)return Results.NotFound();
                try{await FactoryCapacityReservations.LockCenter(db,op.WorkCenterId,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل مرکز کاری در دسترس نیست."});}
                var (stops,error)=await ReadJournal(db,ct);
                if(error is not null)return Results.Conflict(new{message=error});
                var active=stops.SingleOrDefault(x=>x.OrderId==id&&
                    x.OperationId==operationId&&x.StoppedUtc is null);
                if(active is null)
                    return Results.Conflict(new{message="برای این عملیات توقف باز معتبر ثبت نشده است."});
                var now=DateTime.UtcNow;
                if(now<=active.StartedUtc)
                    return Results.Conflict(new{message="پایان توقف باید بعد از شروع آن باشد."});
                var actor=(ctx.User.Identity?.Name??"unknown").Replace("|","_");
                if(actor.Length>90)actor=actor[..90];
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                    OrderId=id,Action=StopAction,AtUtc=now,
                    Details=EndDetail(op.Id,op.WorkCenterId,actor)});
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Ok(new{orderId=id,operationId,startedUtc=active.StartedUtc,
                    stoppedUtc=now,durationMinutes=Math.Round((now-active.StartedUtc).TotalMinutes,2)});
            }).RequireAuthorization("IpShopFloorWrite");
        api.MapGet("/oee/downtime-reconciliation",
            async(string dateJalali,ProductionDb db,CancellationToken ct)=>{
                if(!PersianPlanningDates.TryParse(dateJalali,out var day))
                    return Results.BadRequest(new{message="تاریخ شمسی معتبر نیست."});
                var centers=await db.WorkCenters.AsNoTracking().ToListAsync(ct);
                var plans=await db.ManufacturingOeePlans.AsNoTracking()
                    .Where(x=>x.ProductionDate==day).ToListAsync(ct);
                var (stops,error)=await ReadJournal(db,ct);
                if(error is not null)return Results.Conflict(new{message=error});
                var events=await db.ManufacturingOrderAudits.AsNoTracking()
                    .Where(x=>x.Action==FactoryOperationClock.Started||
                        x.Action==FactoryOperationClock.Stopped).ToListAsync(ct);
                IReadOnlyList<FactoryClockSession> clocks;
                try{clocks=FactoryOperationClock.Read(events);}
                catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
                var from=TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue),Tehran);
                var until=TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue),Tehran);
                var legacy=await db.ManufacturingDowntimes.AsNoTracking()
                    .Where(x=>x.OccurredUtc>=from&&x.OccurredUtc<until)
                    .Join(db.ManufacturingOrderOperations.AsNoTracking(),x=>x.OperationId,x=>x.Id,
                        (x,op)=>new{op.WorkCenterId,x.DurationMinutes}).ToListAsync(ct);
                return Results.Ok(new{
                    dateJalali=PersianPlanningDates.Format(day),
                    rows=centers.Select(x=>Reconcile(day,x.Id,
                        plans.FirstOrDefault(p=>p.WorkCenterId==x.Id)?.PlannedMinutes,
                        clocks,stops,legacy.Where(y=>y.WorkCenterId==x.Id)
                            .Sum(y=>y.DurationMinutes))).ToArray(),
                    disclaimer="ثبت توقف زمان‌دار در IPPCS به ساعت کار وصل می‌شود اما نبود تداخل زمانی به تنهایی اثبات صحت فیزیکی رخداد یا کامل بودن شیفت نیست. توقف قدیمی فاقد بازه عمداً قابل تطبیق قطعی محسوب نمی‌شود."
                });
            });
        return routes;
    }
}
