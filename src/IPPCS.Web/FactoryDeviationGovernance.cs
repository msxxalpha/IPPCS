using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

public record FactoryDeviationEscalateInput(string Reason,string? ReviewByJalali);
public record FactoryDeviationReviseInput(string RootCause,string CorrectiveAction,
    string Responsible,string ActionDueJalali,string ChangeReason);
public record FactoryDeviationFollowUp(Guid Id,ProductionDeviationStatus Status,
    bool HasAssignedAction,bool IsOverdue,int EscalationCount,
    string? LastEscalationReason,DateOnly? NextReviewOn,int EventCount,
    string Disclaimer);
public static class FactoryDeviationGovernance
{
    public const string EscalationPrefix="ESCALATED:";
    public static FactoryDeviationFollowUp FollowUp(ProductionDeviation item,DateOnly asOf)
    {
        var events=item.Events.Where(x=>x.Details.StartsWith(EscalationPrefix,
            StringComparison.Ordinal)).OrderBy(x=>x.AtUtc).ThenBy(x=>x.Id).ToArray();
        string? reason=null;DateOnly? review=null;
        if(events.Length>0)
        {
            var latest=events[^1].Details[EscalationPrefix.Length..];
            var split=latest.IndexOf('|');
            var date=split>=0?latest[..split]:"";
            reason=split>=0?latest[(split+1)..]:latest;
            if(DateOnly.TryParseExact(date,"yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,out var parsed))review=parsed;
        }
        return new(item.Id,item.Status,
            item.Status!=ProductionDeviationStatus.Open,
            item.Status!=ProductionDeviationStatus.Closed&&
                item.ActionDueOn.HasValue&&item.ActionDueOn.Value<asOf,
            events.Length,reason,review,item.Events.Count,
            "پیگیری و تشدید انحراف فقط سوابق مستقل IPPCS است؛ اعلان، تغییر منابع انسانی یا ارسال خودکار صورت نمی‌گیرد.");
    }

    public static IReadOnlyList<ProductionDeviationEvent> RevisionEvidence(
        ProductionDeviation previous,FactoryDeviationReviseInput input,string actor)
    {
        // Preserve complete previous fields (each <=1000 database chars) in
        // separate immutable journal rows. Row is changed only in same transaction.
        var rows=new List<ProductionDeviationEvent>();
        void Save(string type,string value)
        {
            // Bounded chunks avoid truncation; typed header remains in every piece.
            const int chunk=900;
            if(value.Length==0)value="—";
            for(var i=0;i<value.Length;i+=chunk)
                rows.Add(new ProductionDeviationEvent{
                    DeviationId=previous.Id,Status=ProductionDeviationStatus.ActionAssigned,
                    Details="REVISION:"+type+":"+(i/chunk+1)+":"+value.Substring(i,Math.Min(chunk,value.Length-i)),
                    Username=actor});
        }
        Save("OldRoot",previous.RootCause??"");
        Save("OldAction",previous.CorrectiveAction??"");
        Save("OldResponsible",previous.Responsible??"");
        Save("OldDue",previous.ActionDueOn?.ToString("yyyy-MM-dd")??"");
        Save("Reason",input.ChangeReason.Trim());
        return rows;
    }

    internal static async Task LockDeviation(ProductionDb db,Guid id,CancellationToken ct)
    {
        var key="IPPCS:Deviation:"+id.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',
                @LockOwner='Transaction',@LockTimeout=10000;
            IF @r < 0 THROW 51040, 'Deviation lock unavailable', 1;",ct);
    }
    public static IEndpointRouteBuilder MapDeviationGovernance(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/production-deviations/{id:guid}/follow-up",
            async(Guid id,string asOfJalali,ProductionDb db,CancellationToken ct)=>{
                if(!PersianPlanningDates.TryParse(asOfJalali,out var asOf))
                    return Results.BadRequest(new{message="تاریخ شمسی معتبر لازم است."});
                var item=await db.ProductionDeviations.AsNoTracking().Include(x=>x.Events)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                return item is null?Results.NotFound():Results.Ok(FollowUp(item,asOf));
            });
        api.MapPost("/production-deviations/{id:guid}/escalate",
            async(Guid id,FactoryDeviationEscalateInput input,ProductionDb db,
                HttpContext ctx,CancellationToken ct)=>{
                if(string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Trim().Length>600)
                    return Results.BadRequest(new{message="شرح علت تشدید لازم است (حداکثر ۶۰۰ نویسه)."});
                DateOnly? review=null;
                if(!string.IsNullOrWhiteSpace(input.ReviewByJalali)){
                    if(!PersianPlanningDates.TryParse(input.ReviewByJalali,out var parsed))
                        return Results.BadRequest(new{message="تاریخ شمسی بازبینی معتبر نیست."});
                    review=parsed;
                }
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{await LockDeviation(db,id,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل پرونده انحراف در دسترس نیست."});}
                var item=await db.ProductionDeviations.Include(x=>x.Events)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(item is null)return Results.NotFound();
                if(item.Status==ProductionDeviationStatus.Closed)
                    return Results.Conflict(new{message="پرونده مختومه قابل تشدید نیست."});
                var details=EscalationPrefix+(review?.ToString("yyyy-MM-dd")??"")+
                    "|"+input.Reason.Trim();
                if(item.Events.Any(x=>x.Details==details))
                    return Results.Conflict(new{message="این تشدید قبلاً برای پرونده ثبت شده است."});
                // Preassigned Guid child attached through a tracked collection can be
                // interpreted as an existing row by EF; explicitly mark the event Added.
                var eventRow=new ProductionDeviationEvent{DeviationId=id,
                    Status=item.Status,Details=details,
                    Username=ctx.User.Identity?.Name??"unknown"};
                db.ProductionDeviationEvents.Add(eventRow);
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Ok(new{id,item.Status,escalationCount=1+item.Events.Count(x=>
                    x.Details.StartsWith(EscalationPrefix,StringComparison.Ordinal))});
            }).RequireAuthorization("IpPlanningWrite");
        api.MapPost("/production-deviations/{id:guid}/revise-action",
            async(Guid id,FactoryDeviationReviseInput input,ProductionDb db,
                HttpContext ctx,CancellationToken ct)=>{
                if(string.IsNullOrWhiteSpace(input.RootCause)||input.RootCause.Trim().Length>1000||
                    string.IsNullOrWhiteSpace(input.CorrectiveAction)||input.CorrectiveAction.Trim().Length>1000||
                    string.IsNullOrWhiteSpace(input.Responsible)||input.Responsible.Trim().Length>200||
                    string.IsNullOrWhiteSpace(input.ChangeReason)||input.ChangeReason.Trim().Length>600||
                    !PersianPlanningDates.TryParse(input.ActionDueJalali,out var due))
                    return Results.BadRequest(new{message="علت، اقدام، مسئول، موعد و دلیل تغییر معتبر لازم است."});
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{await LockDeviation(db,id,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل پرونده انحراف در دسترس نیست."});}
                var item=await db.ProductionDeviations.SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(item is null)return Results.NotFound();
                if(item.Status!=ProductionDeviationStatus.ActionAssigned)
                    return Results.Conflict(new{message="فقط اقدام تخصیص‌یافته و مختومه‌نشده با دلیل ثبت‌شده قابل بازنگری است."});
                var actor=ctx.User.Identity?.Name??"unknown";
                db.ProductionDeviationEvents.AddRange(RevisionEvidence(item,input,actor));
                item.RootCause=input.RootCause.Trim();item.CorrectiveAction=input.CorrectiveAction.Trim();
                item.Responsible=input.Responsible.Trim();item.ActionDueOn=due;
                db.ProductionDeviationEvents.Add(new ProductionDeviationEvent{
                    DeviationId=id,Status=item.Status,Username=actor,
                    Details="REVISION:Updated:1:اطلاعات اقدام اصلاحی با ثبت سوابق قبلی به‌روز شد."});
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Ok(new{item.Id,item.Status,
                    actionDueJalali=PersianPlanningDates.Format(due)});
            }).RequireAuthorization("IpPlanningWrite");
        return routes;
    }
}
