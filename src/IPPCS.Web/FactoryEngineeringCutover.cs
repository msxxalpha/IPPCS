using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Stage 22: formal, journalled EFFECTIVE-DATE CUTOVER (not a multi-signature ECO).
// Old released work orders retain their original revision pointer and execution
// snapshots. Draft work orders are NOT automatically rewritten: stage 21 rebinding
// must be explicitly preflighted and approved for each selected order.
public sealed class FactoryEngineeringCutoverEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "";
    public Guid ItemId { get; set; }
    public Guid BaselineId { get; set; }
    public Guid CandidateId { get; set; }
    public DateOnly CutoverOn { get; set; }
    public DateOnly? BaselinePreviousEnd { get; set; }
    public DateOnly BaselineNewEnd { get; set; }
    public string ApprovalReference { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public int DraftOrdersToRebind { get; set; }
    public int FrozenOrdersPreserved { get; set; }
}
public sealed record FactoryEngineeringCutoverInput(
    string Kind,Guid BaselineId,Guid CandidateId,string CutoverOnJalali,
    string ApprovalReference,string Reason,bool DryRun = true);

public static class FactoryEngineeringCutover
{
    public static IReadOnlyList<FactoryRevisionIssue> CheckRouting(
        RoutingRevision baseline, RoutingRevision candidate,
        DateOnly cutover, IReadOnlyList<RoutingRevision> approved)
    {
        var issues = new List<FactoryRevisionIssue>();
        if (baseline.Id == candidate.Id || baseline.ItemId != candidate.ItemId ||
            baseline.State != RoutingState.Approved || candidate.State != RoutingState.Draft)
            issues.Add(new("InvalidTransition","فقط انتقال نسخه مصوب قبلی به پیش‌نویس جدید همان محصول مجاز است."));
        if (cutover <= baseline.EffectiveFrom ||
            baseline.EffectiveTo is { } oldEnd && oldEnd < cutover ||
            candidate.EffectiveFrom != cutover)
            issues.Add(new("InvalidCutoverDate","تاریخ برش باید بعد از شروع نسخه قبلی و دقیقاً برابر شروع نسخه پیشنهادی باشد."));
        if (issues.Count > 0) return issues;
        var shortened = new RoutingRevision
        {
            Id=baseline.Id,ItemId=baseline.ItemId,
            State=RoutingState.Approved,RevisionCode=baseline.RevisionCode,
            EffectiveFrom=baseline.EffectiveFrom,EffectiveTo=cutover.AddDays(-1),
            Operations=baseline.Operations
        };
        issues.AddRange(FactoryRevisionGovernance.RoutingIssues(candidate,
            approved.Where(x=>x.Id!=baseline.Id).Append(shortened).ToArray()));
        return issues;
    }

    public static IReadOnlyList<FactoryRevisionIssue> CheckBom(
        ManufacturingBomRevision baseline, ManufacturingBomRevision candidate,
        DateOnly cutover, IReadOnlyList<ManufacturingBomRevision> approved)
    {
        var issues = new List<FactoryRevisionIssue>();
        if (baseline.Id == candidate.Id || baseline.ParentItemId != candidate.ParentItemId ||
            baseline.State != ManufacturingRevisionState.Approved ||
            candidate.State != ManufacturingRevisionState.Draft)
            issues.Add(new("InvalidTransition","BOM مقصد باید پیش‌نویس متفاوت همان محصول و مبدأ مصوب باشد."));
        if (cutover <= baseline.ValidFrom ||
            baseline.ValidUntil is { } oldEnd && oldEnd < cutover ||
            candidate.ValidFrom != cutover)
            issues.Add(new("InvalidCutoverDate","تاریخ برش باید بعد از شروع BOM مصوب و برابر تاریخ شروع BOM پیشنهادی باشد."));
        if (issues.Count > 0) return issues;
        var shortened = new ManufacturingBomRevision
        {
            Id=baseline.Id,ParentItemId=baseline.ParentItemId,Revision=baseline.Revision,
            State=ManufacturingRevisionState.Approved,ValidFrom=baseline.ValidFrom,
            ValidUntil=cutover.AddDays(-1),Lines=baseline.Lines
        };
        issues.AddRange(FactoryRevisionGovernance.BomIssues(candidate,
            approved.Where(x=>x.Id!=baseline.Id).Append(shortened).ToArray()));
        return issues;
    }

    public static IEndpointRouteBuilder MapEngineeringCutover(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpPlanningWrite");
        api.MapGet("/factory-engineering/cutovers",async(Guid itemId,ProductionDb db,CancellationToken ct)=>{
            if(itemId==Guid.Empty)return Results.BadRequest(new{message="کد محصول معتبر نیست."});
            var rows=await db.FactoryEngineeringCutoverEvents.AsNoTracking()
                .Where(x=>x.ItemId==itemId).OrderByDescending(x=>x.AtUtc)
                .Take(100).ToListAsync(ct);
            return Results.Ok(rows.Select(x=>new{
                x.Id,x.Kind,x.ItemId,x.BaselineId,x.CandidateId,
                cutoverOnJalali=PersianPlanningDates.Format(x.CutoverOn),
                baselinePreviousEndJalali=x.BaselinePreviousEnd.HasValue?
                    PersianPlanningDates.Format(x.BaselinePreviousEnd.Value):null,
                baselineNewEndJalali=PersianPlanningDates.Format(x.BaselineNewEnd),
                x.ApprovalReference,x.Reason,x.Actor,x.AtUtc,
                x.DraftOrdersToRebind,x.FrozenOrdersPreserved
            }).ToArray());
        });
        api.MapPost("/factory-engineering/cutover",
            async(FactoryEngineeringCutoverInput input,ProductionDb db,HttpContext context,
                CancellationToken ct)=>{
                var kind=input.Kind?.Trim();
                if(kind is not ("BOM" or "Routing")||
                    input.BaselineId==Guid.Empty||input.CandidateId==Guid.Empty||
                    input.BaselineId==input.CandidateId||
                    !PersianPlanningDates.TryParse(input.CutoverOnJalali,out var cutover)||
                    string.IsNullOrWhiteSpace(input.ApprovalReference)||
                    input.ApprovalReference.Trim().Length is <4 or >100 ||
                    string.IsNullOrWhiteSpace(input.Reason)||
                    input.Reason.Trim().Length>300)
                    return Results.BadRequest(new{message="دو نسخه، تاریخ شمسی برش، مرجع تأیید و علت تغییر معتبر الزامی است."});
                var localToday=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran")));
                if(cutover<localToday)
                    return Results.Conflict(new{message="برش گذشته‌نگر نسخه‌های مهندسی مجاز نیست."});
                Guid? owner=kind=="BOM"
                    ? await db.ManufacturingBomRevisions.AsNoTracking()
                        .Where(x=>x.Id==input.BaselineId)
                        .Select(x=>(Guid?)x.ParentItemId).SingleOrDefaultAsync(ct)
                    : await db.RoutingRevisions.AsNoTracking()
                        .Where(x=>x.Id==input.BaselineId)
                        .Select(x=>(Guid?)x.ItemId).SingleOrDefaultAsync(ct);
                if(owner is null)return Results.NotFound();
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{await FactoryRevisionGovernance.LockRevisionItem(db,kind,owner.Value,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل گراف مهندسی یا مسیر ساخت در دسترس نیست."});}

                // An ECO reference identifies one immutable engineering decision.
                // Reject re-use even across BOM / Routing to prevent audit ambiguity.
                var duplicateReference=await db.FactoryEngineeringCutoverEvents.AsNoTracking()
                    .AnyAsync(x=>x.ApprovalReference==input.ApprovalReference.Trim(),ct);
                if(duplicateReference)
                    return Results.Conflict(new{message="شماره مرجع ECO قبلاً در سابقه برش مهندسی استفاده شده است."});
                var issues=new List<FactoryRevisionIssue>();
                DateOnly? previousEnd;
                if(kind=="Routing")
                {
                    var source=await db.RoutingRevisions.Include(x=>x.Operations)
                        .SingleOrDefaultAsync(x=>x.Id==input.BaselineId,ct);
                    var target=await db.RoutingRevisions.Include(x=>x.Operations)
                        .SingleOrDefaultAsync(x=>x.Id==input.CandidateId,ct);
                    if(source is null||target is null)return Results.NotFound();
                    var approved=await db.RoutingRevisions.AsNoTracking()
                        .Where(x=>x.ItemId==owner&&x.State==RoutingState.Approved)
                        .ToListAsync(ct);
                    issues.AddRange(CheckRouting(source,target,cutover,approved));
                    previousEnd=source.EffectiveTo;
                    if(issues.Count==0&&!input.DryRun)
                    {
                        source.EffectiveTo=cutover.AddDays(-1);
                        target.State=RoutingState.Approved;
                    }
                }
                else
                {
                    var source=await db.ManufacturingBomRevisions.Include(x=>x.Lines)
                        .SingleOrDefaultAsync(x=>x.Id==input.BaselineId,ct);
                    var target=await db.ManufacturingBomRevisions.Include(x=>x.Lines)
                        .SingleOrDefaultAsync(x=>x.Id==input.CandidateId,ct);
                    if(source is null||target is null)return Results.NotFound();
                    var approved=await db.ManufacturingBomRevisions.AsNoTracking()
                        .Include(x=>x.Lines)
                        .Where(x=>x.State==ManufacturingRevisionState.Approved).ToListAsync(ct);
                    issues.AddRange(CheckBom(source,target,cutover,approved));
                    previousEnd=source.ValidUntil;
                    if(issues.Count==0&&!input.DryRun)
                    {
                        source.ValidUntil=cutover.AddDays(-1);
                        target.State=ManufacturingRevisionState.Approved;
                    }
                }
                if(issues.Count>0)return Results.Conflict(new{
                    message="شرایط برش نسخه برقرار نیست؛ هیچ نسخه‌ای تغییر نکرد.",issues});

                // Historical released orders preserve the original revision.
                // Drafts referencing baseline on or after cutover are reported
                // as stale until a separate explicit audited rebind succeeds.
                var affected=kind=="BOM"
                    ? await db.ManufacturingOrders.AsNoTracking()
                        .Where(x=>x.BomRevisionId==input.BaselineId&&
                            x.PlannedStart>=cutover).OrderBy(x=>x.Id)
                        .Take(501).ToListAsync(ct)
                    : await db.ManufacturingOrders.AsNoTracking()
                        .Where(x=>x.RoutingRevisionId==input.BaselineId&&
                            x.PlannedStart>=cutover).OrderBy(x=>x.Id)
                        .Take(501).ToListAsync(ct);
                if(affected.Count>500)return Results.Conflict(new{
                    message="بیش از ۵۰۰ دستور به نسخه قبلی ارجاع دارند؛ برش بدون بررسی جامع مجاز نیست."});
                var drafts=affected.Where(x=>x.Status==ManufacturingOrderStatus.Draft).ToArray();
                var frozen=affected.Count-drafts.Length;
                var impacted=drafts.Select(x=>new{
                    x.Id,x.Number,x.Status,
                    plannedStartJalali=PersianPlanningDates.Format(x.PlannedStart)
                }).ToArray();
                if(input.DryRun)
                    return Results.Ok(new{
                        dryRun=true,kind,input.BaselineId,input.CandidateId,
                        cutoverOnJalali=PersianPlanningDates.Format(cutover),
                        newBaselineEndJalali=PersianPlanningDates.Format(cutover.AddDays(-1)),
                        staleDraftOrders=impacted,protectedFrozenOrders=frozen,
                        disclaimer="بدون تغییر دیتابیس؛ دستورات قدیمی آزادشده محفوظ و پیش‌نویس‌ها نیازمند انتقال جداگانه‌اند."
                    });
                static string Clean(string s)=>s.Replace("\r"," ").Replace("\n"," ")
                    .Replace(";","،").Replace("|"," ").Trim();
                var actor=Clean(context.User.Identity?.Name??"unknown");
                if(actor.Length>100)actor=actor[..100];
                var journal=new FactoryEngineeringCutoverEvent{
                    Kind=kind,ItemId=owner.Value,
                    BaselineId=input.BaselineId,CandidateId=input.CandidateId,
                    CutoverOn=cutover,BaselinePreviousEnd=previousEnd,
                    BaselineNewEnd=cutover.AddDays(-1),
                    ApprovalReference=Clean(input.ApprovalReference),
                    Reason=Clean(input.Reason),Actor=actor,
                    DraftOrdersToRebind=drafts.Length,FrozenOrdersPreserved=frozen
                };
                db.FactoryEngineeringCutoverEvents.Add(journal);
                try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
                catch(DbUpdateConcurrencyException){
                    return Results.Conflict(new{message="تغییر هم‌زمان نسخه مهندسی رخ داد."});
                }
                catch(DbUpdateException){
                    return Results.Conflict(new{message="ثبت یکتای سابقه برش انجام نشد؛ نسخه‌ها تغییر نکردند."});
                }
                return Results.Ok(new{
                    dryRun=false,journalId=journal.Id,
                    kind,input.BaselineId,input.CandidateId,
                    cutoverOnJalali=PersianPlanningDates.Format(cutover),
                    newBaselineEndJalali=PersianPlanningDates.Format(cutover.AddDays(-1)),
                    staleDraftOrders=impacted,protectedFrozenOrders=frozen,
                    disclaimer="تصویب مؤثر از تاریخ برش با سابقه مستقل ثبت شد. مرجع تأیید ارائه‌شده هنوز امضای دیجیتال/دو امضایی ECO نیست."
                });
            });
        return routes;
    }
}
