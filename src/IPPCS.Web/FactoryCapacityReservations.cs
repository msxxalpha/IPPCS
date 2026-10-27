using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Stage 9.8: planned dispatch slots stored as append-only IPPCS audit events,
// never a warehouse / HR / purchasing / sales transaction.
public record FactoryReservationInput(Guid OperationId,string DateJalali,
    string StartTime,string EndTime,string? Note);
public record FactoryCapacityReservation(Guid Id,Guid OrderId,Guid OperationId,
    Guid WorkCenterId,DateTime StartLocal,DateTime EndLocal,DateTime RecordedUtc,
    string RecordedBy,string Note);
public record FactoryReservationComparison(FactoryCapacityReservation Reservation,
    DateTime? ActualFirstStartLocal,DateTime? ActualLastStopLocal,
    decimal? ActualMinutes,decimal PlannedMinutes,decimal? DurationVarianceMinutes,
    decimal? StartVarianceMinutes);

public static class FactoryCapacityReservations
{
    public const string Reserved="CenterSlotReserved";
    public const string Cancelled="CenterSlotCancelled";
    private static readonly TimeZoneInfo Tehran=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    private const string StampFormat="yyyyMMddHHmm";

    public static bool Overlap(DateTime aStart,DateTime aEnd,DateTime bStart,DateTime bEnd) =>
        aStart<bEnd&&bStart<aEnd; // half-open slots allow exact back-to-back shifts.

    public static bool TryWindow(FactoryReservationInput input,out DateTime start,out DateTime end)
    {
        start=end=default;
        if(!PersianPlanningDates.TryParse(input.DateJalali,out var date))return false;
        var textStart=PersianPlanningDates.NormalizeDigits(input.StartTime??"");
        var textEnd=PersianPlanningDates.NormalizeDigits(input.EndTime??"");
        if(!TimeOnly.TryParseExact(textStart,"HH:mm",CultureInfo.InvariantCulture,
                DateTimeStyles.None,out var from)||
           !TimeOnly.TryParseExact(textEnd,"HH:mm",CultureInfo.InvariantCulture,
                DateTimeStyles.None,out var until)||until<=from)return false;
        start=date.ToDateTime(from);
        end=date.ToDateTime(until);
        return true;
    }
    public static bool WithinShift(Guid center,DateTime start,DateTime end,
        IReadOnlyList<WorkShift> shifts)
    {
        if(start.Date!=end.Date)return false;
        foreach(var shift in shifts.Where(x=>x.WorkCenterId==center &&
            x.DayOfWeek==(int)start.DayOfWeek))
        {
            if(shift.EndTime<=shift.StartTime||shift.EffectiveCapacityPercent<=0||
                shift.EffectiveCapacityPercent>100)continue;
            var shiftStart=shift.StartTime.ToTimeSpan().TotalMinutes;
            var shiftEnd=shift.EndTime.ToTimeSpan().TotalMinutes;
            var effective=Math.Floor((decimal)(shiftEnd-shiftStart)*
                shift.EffectiveCapacityPercent/100m);
            if(start.TimeOfDay.TotalMinutes>=shiftStart &&
               end.TimeOfDay.TotalMinutes<=shiftStart+(double)effective)return true;
        }
        return false;
    }
    public static string Encode(FactoryCapacityReservation x)
    {
        // The Details column is limited to 500 characters; no user data can overflow it.
        var note=(x.Note??"").Replace("|"," ").Replace("\n"," ").Replace("\r"," ").Trim();
        if(note.Length>130)note=note[..130];
        return string.Join('|',x.Id.ToString("N"),x.OperationId.ToString("N"),
            x.WorkCenterId.ToString("N"),x.StartLocal.ToString(StampFormat,CultureInfo.InvariantCulture),
            x.EndLocal.ToString(StampFormat,CultureInfo.InvariantCulture),note);
    }
    public static IReadOnlyList<FactoryCapacityReservation> Read(
        IEnumerable<ManufacturingOrderAudit> events)
    {
        var active=new Dictionary<Guid,FactoryCapacityReservation>();
        foreach(var e in events.Where(x=>x.Action==Reserved||x.Action==Cancelled)
            .OrderBy(x=>x.AtUtc).ThenBy(x=>x.Action==Reserved?0:1).ThenBy(x=>x.Id))
        {
            if(e.Action==Reserved)
            {
                var parts=(e.Details??"").Split('|');
                if(parts.Length!=6 || !Guid.TryParseExact(parts[0],"N",out var id)||
                   !Guid.TryParseExact(parts[1],"N",out var op)||
                   !Guid.TryParseExact(parts[2],"N",out var center)||
                   id==Guid.Empty||op==Guid.Empty||center==Guid.Empty||
                   !DateTime.TryParseExact(parts[3],StampFormat,CultureInfo.InvariantCulture,
                       DateTimeStyles.None,out var start)||
                   !DateTime.TryParseExact(parts[4],StampFormat,CultureInfo.InvariantCulture,
                       DateTimeStyles.None,out var end)||end<=start||start.Date!=end.Date||
                   active.ContainsKey(id))
                    throw new InvalidOperationException("رویداد رزرو ظرفیت نامعتبر یا تکراری است.");
                active.Add(id,new(id,e.OrderId,op,center,
                    DateTime.SpecifyKind(start,DateTimeKind.Unspecified),
                    DateTime.SpecifyKind(end,DateTimeKind.Unspecified),
                    DateTime.SpecifyKind(e.AtUtc,DateTimeKind.Utc),"",parts[5]));
            }
            else
            {
                if(!Guid.TryParseExact(e.Details??"","N",out var id)||
                    !active.TryGetValue(id,out var existing)||
                    existing.OrderId!=e.OrderId)
                    throw new InvalidOperationException("لغو رزرو فاقد رویداد معتبر قبلی است.");
                active.Remove(id);
            }
        }
        var result=active.Values.OrderBy(x=>x.StartLocal).ThenBy(x=>x.Id).ToArray();
        // Detect journal tampering independently of the write-time lock.
        for(int i=0;i<result.Length;i++)
            for(int j=i+1;j<result.Length;j++)
                if(result[i].WorkCenterId==result[j].WorkCenterId &&
                    Overlap(result[i].StartLocal,result[i].EndLocal,
                        result[j].StartLocal,result[j].EndLocal))
                    throw new InvalidOperationException("هم‌پوشانی رزروهای مراکز کاری در تاریخچه شناسایی شد.");
        return result;
    }

    public static FactoryReservationComparison Compare(FactoryCapacityReservation planned,
        IReadOnlyList<FactoryClockSession> actual)
    {
        var sessions=actual.Where(x=>x.OperationId==planned.OperationId &&
            x.OrderId==planned.OrderId).OrderBy(x=>x.StartedUtc).ToArray();
        var closed=sessions.Where(x=>x.StoppedUtc.HasValue).ToArray();
        // Stop/start events are stamped in UTC; never interpret server-local timezone.
        DateTime? start=sessions.Length>0
            ?TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(sessions[0].StartedUtc,DateTimeKind.Utc),Tehran):null;
        DateTime? stop=sessions.Length>0&&sessions.All(x=>x.StoppedUtc.HasValue)
            ?TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(
                sessions[^1].StoppedUtc!.Value,DateTimeKind.Utc),Tehran):null;
        decimal? actualMinutes=sessions.Length>0&&closed.Length==sessions.Length
            ?closed.Sum(x=>(decimal)(x.StoppedUtc!.Value-x.StartedUtc).TotalMinutes):null;
        var plannedMinutes=(decimal)(planned.EndLocal-planned.StartLocal).TotalMinutes;
        return new(planned,start,stop,actualMinutes,plannedMinutes,
            actualMinutes.HasValue?actualMinutes-plannedMinutes:null,
            start.HasValue?(decimal)(start.Value-planned.StartLocal).TotalMinutes:null);
    }

    public static IReadOnlyList<WorkCenterOccupiedInterval> Occupied(
        IReadOnlyList<FactoryCapacityReservation> entries) =>
        entries.Select(x=>new WorkCenterOccupiedInterval(
            x.WorkCenterId,x.StartLocal,x.EndLocal)).ToArray();

    internal static async Task LockOrder(ProductionDb db,Guid orderId,CancellationToken ct)
    {
        var resource="IPPCS:OrderSlot:"+orderId.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DECLARE @res int;
               EXEC @res = sys.sp_getapplock @Resource={resource},
                    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
               IF @res < 0 THROW 51002, 'Work order lock unavailable', 1;",ct);
    }

    /// <summary>
    /// Called while holding the order lock in the same DB transaction. Released
    /// operations cannot leave phantom future slots behind. Every cancellation
    /// is an appended journal entry, never a physical delete.
    /// </summary>
    public static IReadOnlyList<FactoryCapacityReservation> PendingLifecycleRelease(
        IEnumerable<ManufacturingOrderAudit> orderEvents,Guid orderId,
        IReadOnlySet<Guid> operationIds) =>
        Read(orderEvents).Where(x=>x.OrderId==orderId&&
            operationIds.Contains(x.OperationId)).ToArray();

    internal static async Task<int> ReleaseOperations(
        ProductionDb db,Guid orderId,IReadOnlySet<Guid> completedOperationIds,
        string reason,CancellationToken ct)
    {
        if(completedOperationIds.Count==0)return 0;
        var history=await db.ManufacturingOrderAudits.AsNoTracking()
            .Where(x=>x.OrderId==orderId&&(x.Action==Reserved||x.Action==Cancelled))
            .ToListAsync(ct);
        var affected=PendingLifecycleRelease(history,orderId,completedOperationIds);
        foreach(var center in affected.Select(x=>x.WorkCenterId).Distinct().OrderBy(x=>x))
            await LockCenter(db,center,ct);
        foreach(var slot in affected)
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=orderId,Action=Cancelled,Details=slot.Id.ToString("N")
            });
        if(affected.Length>0)
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=orderId,Action="CapacityLifecycle",
                Details=reason+";Released="+affected.Length
            });
        return affected.Length;
    }

    internal static async Task LockCenter(ProductionDb db,Guid center,CancellationToken ct)
    {
        // This lock is transaction-owned and common to *all* writers on the center.
        // Prevents read-then-insert phantom reservations across separate web hosts.
        var resource="IPPCS:CenterSlot:"+center.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DECLARE @res int;
               EXEC @res = sys.sp_getapplock @Resource={resource},
                    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
               IF @res < 0 THROW 51001, 'Work center lock unavailable', 1;",ct);
    }

    public static IEndpointRouteBuilder MapFactoryCapacityReservations(
        this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/manufacturing-orders/{id:guid}/capacity-reservations",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
            if(!await db.ManufacturingOrders.AsNoTracking().AnyAsync(x=>x.Id==id,ct))
                return Results.NotFound();
            var events=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.Action==Reserved||x.Action==Cancelled||
                    x.Action==FactoryOperationClock.Started||x.Action==FactoryOperationClock.Stopped)
                .ToListAsync(ct);
            try
            {
                var reservations=Read(events.Where(x=>x.Action==Reserved||x.Action==Cancelled))
                    .Where(x=>x.OrderId==id).ToArray();
                var sessions=FactoryOperationClock.Read(events);
                return Results.Ok(new{rows=reservations.Select(x=>{
                    var comparison=Compare(x,sessions);
                    return new{x.Id,x.OrderId,x.OperationId,x.WorkCenterId,x.Note,
                        dayJalali=PersianPlanningDates.Format(DateOnly.FromDateTime(x.StartLocal)),
                        startTime=x.StartLocal.ToString("HH:mm",CultureInfo.InvariantCulture),
                        endTime=x.EndLocal.ToString("HH:mm",CultureInfo.InvariantCulture),
                        comparison.PlannedMinutes,comparison.ActualMinutes,
                        comparison.DurationVarianceMinutes,comparison.StartVarianceMinutes,
                        actualStartJalali=comparison.ActualFirstStartLocal is null?null:
                            PersianPlanningDates.Format(DateOnly.FromDateTime(comparison.ActualFirstStartLocal.Value)),
                        actualStartTime=comparison.ActualFirstStartLocal?.ToString("HH:mm",CultureInfo.InvariantCulture),
                        actualEndTime=comparison.ActualLastStopLocal?.ToString("HH:mm",CultureInfo.InvariantCulture)
                    };
                })});
            }
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
        });

        api.MapPost("/manufacturing-orders/{id:guid}/capacity-reservations",
            async(Guid id,FactoryReservationInput input,ProductionDb db,HttpContext context,CancellationToken ct)=>{
            if(input.OperationId==Guid.Empty||!TryWindow(input,out var start,out var end)||
                (input.Note?.Length??0)>130)
                return Results.BadRequest(new{message="عملیات، تاریخ شمسی و ساعات رزرو معتبر نیستند."});
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            try{await LockOrder(db,id,ct);}
            catch(SqlException){return Results.Conflict(new{message="قفل دستور تولید در دسترس نیست."});}
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(!ManufacturingExecutionEndpoints.IsActive(order.Status))
                return Results.Conflict(new{message="رزرو فقط برای دستور آزادشده یا در حال اجرا مجاز است."});
            var operations=order.Operations.OrderBy(x=>x.Sequence).ToArray();
            var index=Array.FindIndex(operations,x=>x.Id==input.OperationId);
            if(index<0)return Results.NotFound();
            var op=operations[index];
            if(op.IsComplete||op.WorkCenterId==Guid.Empty||
                DateOnly.FromDateTime(start)<order.PlannedStart)
                return Results.Conflict(new{message="عملیات تکمیل‌شده یا تاریخ پیش از شروع دستور قابل رزرو نیست."});
            var shifts=await db.WorkShifts.AsNoTracking()
                .Where(x=>x.WorkCenterId==op.WorkCenterId).ToListAsync(ct);
            if(!WithinShift(op.WorkCenterId,start,end,shifts))
                return Results.Conflict(new{message="رزرو باید داخل شیفت مؤثر مرکز کاری قرار گیرد."});
            if((end-start).TotalMinutes <
                Math.Ceiling((double)(op.SetupMinutes+op.RunMinutesPerUnit*order.PlannedQuantity)))
                return Results.Conflict(new{message="زمان رزرو از زمان استاندارد ساخت این عملیات کمتر است."});
            try{await LockCenter(db,op.WorkCenterId,ct);}
            catch(SqlException){return Results.Conflict(new{message="قفل ظرفیت مرکز در دسترس نیست؛ دوباره تلاش کنید."});}
            var events=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.Action==Reserved||x.Action==Cancelled||
                    x.Action==FactoryOperationClock.Started||x.Action==FactoryOperationClock.Stopped)
                .ToListAsync(ct);
            IReadOnlyList<FactoryCapacityReservation> existing;
            IReadOnlyList<FactoryClockSession> actual;
            try{
                existing=Read(events);
                actual=FactoryOperationClock.Read(events);
            }
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            if(existing.Any(x=>x.OperationId==op.Id))
                return Results.Conflict(new{message="این عملیات از قبل یک رزرو فعال دارد؛ ابتدا لغو کنید."});
            if(existing.Any(x=>x.WorkCenterId==op.WorkCenterId&&
                Overlap(start,end,x.StartLocal,x.EndLocal))||
                FactoryOperationClock.BusyIntervals(actual,end)
                    .Any(x=>x.WorkCenterId==op.WorkCenterId&&Overlap(start,end,x.StartLocal,x.EndLocal)))
                return Results.Conflict(new{message="مرکز کاری در این بازه رزرو یا در حال فعالیت است."});
            if(index>0&&!operations[index-1].IsComplete)
            {
                var previous=existing.SingleOrDefault(x=>x.OperationId==operations[index-1].Id);
                if(previous is null||previous.EndLocal>start)
                    return Results.Conflict(new{message="رزرو عملیات پیشین باید قبل از این مرحله پایان یافته باشد."});
            }
            var reservation=new FactoryCapacityReservation(Guid.NewGuid(),id,op.Id,
                op.WorkCenterId,start,end,DateTime.UtcNow,context.User.Identity?.Name??"",input.Note??"");
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=id,Action=Reserved,Details=Encode(reservation),AtUtc=reservation.RecordedUtc});
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/manufacturing-orders/{id}/capacity-reservations",
                new{reservation.Id,reservation.OperationId,reservation.WorkCenterId,
                    dayJalali=PersianPlanningDates.Format(DateOnly.FromDateTime(start)),
                    startTime=input.StartTime,endTime=input.EndTime});
        }).RequireAuthorization("IpPlanningWrite");

        api.MapPost("/manufacturing-orders/{id:guid}/capacity-reservations/{reservationId:guid}/cancel",
            async(Guid id,Guid reservationId,ProductionDb db,CancellationToken ct)=>{
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            try{await LockOrder(db,id,ct);}
            catch(SqlException){return Results.Conflict(new{message="قفل دستور تولید در دسترس نیست."});}
            var candidate=await db.ManufacturingOrderAudits.AsNoTracking()
                .FirstOrDefaultAsync(x=>x.OrderId==id&&x.Action==Reserved&&
                    x.Details!=null&&x.Details.StartsWith(reservationId.ToString("N")+"|"),ct);
            if(candidate is null)return Results.NotFound();
            var originalParts=candidate.Details!.Split('|');
            if(originalParts.Length!=6||!Guid.TryParseExact(originalParts[2],"N",out var center))
                return Results.Conflict(new{message="سابقه رزرو نامعتبر است."});
            try{await LockCenter(db,center,ct);}
            catch(SqlException){return Results.Conflict(new{message="قفل ظرفیت مرکز در دسترس نیست."});}
            var events=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.Action==Reserved||x.Action==Cancelled).ToListAsync(ct);
            IReadOnlyList<FactoryCapacityReservation> active;
            try{active=Read(events);}
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            var target=active.SingleOrDefault(x=>x.Id==reservationId&&x.OrderId==id);
            if(target is null)
                return Results.Conflict(new{message="رزرو قبلاً لغو شده است."});
            var parent=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(parent is null)return Results.NotFound();
            var targetOp=parent.Operations.SingleOrDefault(x=>x.Id==target.OperationId);
            if(targetOp is null)return Results.Conflict(new{message="عملیات مربوط به رزرو وجود ندارد."});
            if(!targetOp.IsComplete &&
                active.Any(x=>x.OrderId==id&&x.Id!=target.Id &&
                    parent.Operations.Any(op=>op.Id==x.OperationId&&
                        op.Sequence>targetOp.Sequence&&!op.IsComplete)))
                return Results.Conflict(new{message="ابتدا رزرو عملیات بعدی را لغو کنید تا تقدم ساخت حفظ شود."});
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=id,Action=Cancelled,Details=reservationId.ToString("N")});
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new{reservationId,cancelled=true});
        }).RequireAuthorization("IpPlanningWrite");
        return routes;
    }
}
