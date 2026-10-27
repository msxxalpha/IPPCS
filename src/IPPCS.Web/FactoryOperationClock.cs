using System.Data;
using Microsoft.EntityFrameworkCore;

// Stage 9.7: append-only actual operation clock observations, persisted in the
// existing manufacturing-order audit journal; no edits to business history.
// No PLC/HR/warehouse integration. Future capacity beyond an open clock is uncertain.
public record FactoryClockSession(Guid OrderId,Guid OperationId,Guid WorkCenterId,
    DateTime StartedUtc,DateTime? StoppedUtc);
public record WorkCenterOccupiedInterval(Guid WorkCenterId,DateTime StartLocal,DateTime EndLocal);

public static class FactoryOperationClock
{
    public const string Started="OperationClockStart";
    public const string Stopped="OperationClockStop";
    private static readonly TimeZoneInfo FactoryTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public static string Detail(Guid operationId,Guid centerId,string? actor=null)
    {
        var who=(actor??"unknown").Replace("|","").Trim();
        if(who.Length>90)who=who[..90];
        return operationId.ToString("N")+"|"+centerId.ToString("N")+"|"+who;
    }
    public static IReadOnlyList<FactoryClockSession> Read(IEnumerable<ManufacturingOrderAudit> history)
    {
        var sessions=new List<FactoryClockSession>();
        var open=new Dictionary<Guid,FactoryClockSession>();
        foreach(var e in history.Where(x=>x.Action is Started or Stopped)
            .OrderBy(x=>x.AtUtc)
            // At a shared timestamp, a closed [start,end) interval frees the center
            // before another operation starts; random event IDs must not reverse that.
            .ThenBy(x=>x.Action==Stopped?0:1).ThenBy(x=>x.Id))
        {
            var parts=(e.Details??"").Split('|');
            if(parts.Length!=3 ||
                !Guid.TryParseExact(parts[0],"N",out var operationId) ||
                !Guid.TryParseExact(parts[1],"N",out var centerId) ||
                operationId==Guid.Empty||centerId==Guid.Empty||
                e.AtUtc.Kind==DateTimeKind.Local)
                throw new InvalidOperationException("رویداد ساعت عملیات دارای اطلاعات نامعتبر است.");
            var stamp=DateTime.SpecifyKind(e.AtUtc,DateTimeKind.Utc);
            if(e.Action==Started)
            {
                if(open.ContainsKey(operationId) ||
                    open.Values.Any(x=>x.WorkCenterId==centerId) ||
                    open.Values.Any(x=>x.OrderId==e.OrderId))
                    throw new InvalidOperationException("شروع هم‌پوشان یا تکراری عملیات ثبت شده است.");
                var session=new FactoryClockSession(e.OrderId,operationId,centerId,stamp,null);
                open.Add(operationId,session);
                sessions.Add(session);
            }
            else
            {
                if(!open.TryGetValue(operationId,out var prior)||
                    prior.WorkCenterId!=centerId||prior.OrderId!=e.OrderId ||
                    stamp<=prior.StartedUtc)
                    throw new InvalidOperationException("پایان عملیات بدون شروع معتبر یا با ترتیب زمانی اشتباه است.");
                var index=sessions.FindLastIndex(x=>x.OperationId==operationId&&x.StoppedUtc is null);
                if(index<0)throw new InvalidOperationException("سابقه شروع عملیات پیدا نشد.");
                sessions[index]=prior with{StoppedUtc=stamp};
                open.Remove(operationId);
            }
        }
        // A corrupt overlapping historical center interval must never be considered free.
        foreach(var group in sessions.GroupBy(x=>x.WorkCenterId))
        {
            var ordered=group.OrderBy(x=>x.StartedUtc).ToArray();
            for(int i=1;i<ordered.Length;i++)
                if(ordered[i-1].StoppedUtc is null ||
                    ordered[i-1].StoppedUtc.Value>ordered[i].StartedUtc)
                    throw new InvalidOperationException("در تاریخچه زمان کارکرد مرکز، تداخل ثبت شده است.");
        }
        return sessions;
    }

    public static IReadOnlyList<WorkCenterOccupiedInterval> BusyIntervals(
        IReadOnlyList<FactoryClockSession> sessions,DateTime horizonEndLocal)
    {
        if(horizonEndLocal.Kind!=DateTimeKind.Unspecified)
            throw new ArgumentException("بازه محلی کارخانه باید بدون منطقه زمانی باشد.");
        var windows=new List<WorkCenterOccupiedInterval>();
        foreach(var entry in sessions)
        {
            var start=TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(entry.StartedUtc,DateTimeKind.Utc),FactoryTimeZone);
            var finish=entry.StoppedUtc.HasValue
                ?TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(entry.StoppedUtc.Value,DateTimeKind.Utc),FactoryTimeZone)
                :horizonEndLocal;
            if(finish>start) windows.Add(new(entry.WorkCenterId,start,finish));
        }
        return windows;
    }

    public static IEndpointRouteBuilder MapFactoryOperationClock(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/manufacturing-orders/{id:guid}/operation-clock",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
            if(!await db.ManufacturingOrders.AnyAsync(x=>x.Id==id,ct))return Results.NotFound();
            var audits=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.OrderId==id&&(x.Action==Started||x.Action==Stopped)).ToListAsync(ct);
            try
            {
                var sessions=Read(audits);
                return Results.Ok(new{sessions=sessions.Select(x=>new{
                    x.OrderId,x.OperationId,x.WorkCenterId,x.StartedUtc,x.StoppedUtc,
                    startedJalali=PersianPlanningDates.Format(DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTimeFromUtc(x.StartedUtc,FactoryTimeZone))),
                    stoppedJalali=x.StoppedUtc.HasValue?PersianPlanningDates.Format(DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTimeFromUtc(x.StoppedUtc.Value,FactoryTimeZone))):null,
                    isRunning=!x.StoppedUtc.HasValue
                })});
            }
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
        });

        api.MapPost("/manufacturing-orders/{id:guid}/operations/{operationId:guid}/clock-start",
            async(Guid id,Guid operationId,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var order=await db.ManufacturingOrders.Include(x=>x.Operations)
                .ThenInclude(x=>x.InspectionRequirements)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(!ManufacturingExecutionEndpoints.IsActive(order.Status))
                return Results.Conflict(new{message="دستور برای شروع ساعت عملیات فعال نیست."});
            var operations=order.Operations.OrderBy(x=>x.Sequence).ToArray();
            var index=Array.FindIndex(operations,x=>x.Id==operationId);
            if(index<0)return Results.NotFound();
            var op=operations[index];
            if(op.IsComplete||op.WorkCenterId==Guid.Empty ||
                (index>0&&(!operations[index-1].IsComplete ||
                    operations[index-1].QualityPendingQuantity>0 ||
                    !ManufacturingInspectionRules.GatePassed(operations[index-1]))))
                return Results.Conflict(new{message="عملیات قابل شروع نیست یا پیش‌نیاز آن تکمیل نشده است."});
            try{await FactoryCapacityReservations.LockCenter(db,op.WorkCenterId,ct);}
            catch(Microsoft.Data.SqlClient.SqlException)
            {return Results.Conflict(new{message="قفل مرکز کاری در دسترس نیست."});}
            var history=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.Action==Started||x.Action==Stopped||
                    x.Action==FactoryCapacityReservations.Reserved ||
                    x.Action==FactoryCapacityReservations.Cancelled).ToListAsync(ct);
            IReadOnlyList<FactoryClockSession> sessions;
            try{sessions=Read(history);}
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            if(sessions.Any(x=>x.StoppedUtc is null &&
                (x.WorkCenterId==op.WorkCenterId||x.OrderId==id)))
                return Results.Conflict(new{message="این مرکز کاری یا دستور، عملیات زمان‌دار باز دارد."});
            IReadOnlyList<FactoryCapacityReservation> booked;
            try{booked=FactoryCapacityReservations.Read(history);}
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            var startedAt=DateTime.UtcNow;
            var local=TimeZoneInfo.ConvertTimeFromUtc(startedAt,FactoryTimeZone);
            if(booked.Any(x=>x.WorkCenterId==op.WorkCenterId &&
                x.OperationId!=op.Id && x.StartLocal<=local && local<x.EndLocal))
                return Results.Conflict(new{message="مرکز کاری در این لحظه برای عملیات دیگری رزرو شده است."});
            var at=DateTime.UtcNow;
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=id,Action=Started,AtUtc=at,
                Details=Detail(op.Id,op.WorkCenterId,ctx.User.Identity?.Name)});
            if(order.Status==ManufacturingOrderStatus.Released)
                order.Status=ManufacturingOrderStatus.InProgress;
            order.UpdatedUtc=at;
            try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="شروع هم‌زمان؛ دوباره تلاش کنید."});}
            return Results.Ok(new{orderId=id,operationId,startedUtc=at,isRunning=true});
        }).RequireAuthorization("IpShopFloorWrite");

        api.MapPost("/manufacturing-orders/{id:guid}/operations/{operationId:guid}/clock-stop",
            async(Guid id,Guid operationId,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var order=await db.ManufacturingOrders.Include(x=>x.Operations)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            var op=order.Operations.SingleOrDefault(x=>x.Id==operationId);
            if(op is null)return Results.NotFound();
            if(!ManufacturingExecutionEndpoints.IsActive(order.Status) &&
                order.Status!=ManufacturingOrderStatus.OnHold)
                return Results.Conflict(new{message="ثبت پایان برای این وضعیت دستور مجاز نیست."});
            var history=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.Action==Started||x.Action==Stopped).ToListAsync(ct);
            IReadOnlyList<FactoryClockSession> sessions;
            try{sessions=Read(history);}
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            var open=sessions.SingleOrDefault(x=>x.OperationId==operationId&&x.StoppedUtc is null);
            if(open is null||open.OrderId!=id||open.WorkCenterId!=op.WorkCenterId)
                return Results.Conflict(new{message="شروع باز و معتبر برای این عملیات یافت نشد."});
            var at=DateTime.UtcNow;
            if(at<=open.StartedUtc)
                return Results.Conflict(new{message="پایان کارکرد باید پس از شروع باشد."});
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=id,Action=Stopped,AtUtc=at,
                Details=Detail(op.Id,op.WorkCenterId,ctx.User.Identity?.Name)});
            order.UpdatedUtc=at;
            try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
            catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="پایان هم‌زمان؛ دوباره تلاش کنید."});}
            return Results.Ok(new{orderId=id,operationId,startedUtc=open.StartedUtc,stoppedUtc=at,isRunning=false});
        }).RequireAuthorization("IpShopFloorWrite");
        return routes;
    }
}
