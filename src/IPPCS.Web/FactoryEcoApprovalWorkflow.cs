using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Independent authenticated engineering request and quality attestation.
// These are TWO named application-account decisions, not PKI digital signatures,
// management-of-change authorization outside IPPCS, or warehouse approval.
public enum FactoryEcoApprovalStatus { Submitted, QualityApproved, Rejected, Executed }

public sealed class FactoryEcoApproval
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string Kind { get; set; }="";
    public Guid ItemId { get; set; }
    public Guid BaselineId { get; set; }
    public Guid CandidateId { get; set; }
    public DateOnly CutoverOn { get; set; }
    public string ApprovalReference { get; set; }="";
    public string Reason { get; set; }="";
    public string CandidateDigest { get; set; }="";
    public FactoryEcoApprovalStatus Status { get; set; }
    public string RequestedBy { get; set; }="";
    public DateTime RequestedUtc { get; set; }=DateTime.UtcNow;
    public string? QualityReviewedBy { get; set; }
    public DateTime? QualityReviewedUtc { get; set; }
    public string? QualityEvidenceReference { get; set; }
    public string? QualityObservation { get; set; }
    public DateTime? ExecutedUtc { get; set; }
}

public record FactoryEcoSubmissionInput(string Kind,Guid BaselineId,Guid CandidateId,
    string CutoverOnJalali,string ApprovalReference,string Reason);
public record FactoryEcoReviewInput(bool Approve,string EvidenceReference,string Observation);
public record FactoryEcoPreflight(Guid ItemId,string Digest,
    IReadOnlyList<FactoryRevisionIssue> Issues);

public static class FactoryEcoApprovalWorkflow
{
    private static readonly TimeZoneInfo Tehran=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    public static DateOnly Today()=>DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,Tehran));

    private static string Fingerprint(object obj)=>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(obj))));

    public static string RoutingDigest(RoutingRevision source,RoutingRevision candidate)
    {
        object Snapshot(RoutingRevision r)=>new{
            r.Id,r.ItemId,r.RevisionCode,r.State,r.EffectiveFrom,r.EffectiveTo,
            operations=r.Operations.OrderBy(x=>x.Sequence).ThenBy(x=>x.Id)
                .Select(x=>new{x.Id,x.Sequence,x.Name,x.WorkCenterId,
                    x.SetupMinutes,x.RunMinutesPerUnit,x.YieldPercent}).ToArray()
        };
        return Fingerprint(new{kind="Routing",baseline=Snapshot(source),
            proposal=Snapshot(candidate)});
    }
    public static string BomDigest(ManufacturingBomRevision source,
        ManufacturingBomRevision candidate)
    {
        object Snapshot(ManufacturingBomRevision r)=>new{
            r.Id,r.ParentItemId,r.Revision,r.State,r.ValidFrom,r.ValidUntil,
            lines=r.Lines.OrderBy(x=>x.Sequence).ThenBy(x=>x.Id)
                .Select(x=>new{x.Id,x.ComponentItemId,x.Sequence,
                    x.QuantityPer,x.ScrapPercent}).ToArray()
        };
        return Fingerprint(new{kind="BOM",baseline=Snapshot(source),
            proposal=Snapshot(candidate)});
    }

    public static bool CanExecute(FactoryEcoApproval? approval,string kind,
        Guid baselineId,Guid candidateId,DateOnly cutover,string reference,
        string reason,string digest)=>
        approval is not null &&
        approval.Status==FactoryEcoApprovalStatus.QualityApproved &&
        approval.Kind==kind &&approval.BaselineId==baselineId &&
        approval.CandidateId==candidateId &&approval.CutoverOn==cutover &&
        string.Equals(approval.ApprovalReference,reference,StringComparison.Ordinal) &&
        string.Equals(approval.Reason,reason,StringComparison.Ordinal) &&
        string.Equals(approval.CandidateDigest,digest,StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(approval.RequestedBy) &&
        !string.IsNullOrWhiteSpace(approval.QualityReviewedBy) &&
        !approval.RequestedBy.Equals(approval.QualityReviewedBy,
            StringComparison.OrdinalIgnoreCase) &&
        approval.QualityReviewedUtc.HasValue &&
        !string.IsNullOrWhiteSpace(approval.QualityEvidenceReference);

    public static async Task LockGlobal(ProductionDb db,CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            "DECLARE @rc int; EXEC @rc=sys.sp_getapplock " +
            "@Resource=N'IPPCS:ECO:ApprovalReferences', " +
            "@LockMode=N'Exclusive', @LockOwner=N'Transaction', " +
            "@LockTimeout=10000; " +
            "IF @rc < 0 THROW 51083, 'ECO approval reference lock unavailable', 1;",ct);
    }
    public static async Task<FactoryEcoPreflight?> Preflight(
        ProductionDb db,string kind,Guid baselineId,Guid candidateId,
        DateOnly cutover,CancellationToken ct)
    {
        if(kind=="Routing")
        {
            var source=await db.RoutingRevisions.AsNoTracking()
                .Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==baselineId,ct);
            var candidate=await db.RoutingRevisions.AsNoTracking()
                .Include(x=>x.Operations).SingleOrDefaultAsync(x=>x.Id==candidateId,ct);
            if(source is null||candidate is null)return null;
            var approved=await db.RoutingRevisions.AsNoTracking()
                .Where(x=>x.ItemId==source.ItemId&&x.State==RoutingState.Approved)
                .ToListAsync(ct);
            return new(source.ItemId,RoutingDigest(source,candidate),
                FactoryEngineeringCutover.CheckRouting(source,candidate,cutover,approved));
        }
        if(kind=="BOM")
        {
            var source=await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==baselineId,ct);
            var candidate=await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==candidateId,ct);
            if(source is null||candidate is null)return null;
            var approved=await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x=>x.Lines)
                .Where(x=>x.State==ManufacturingRevisionState.Approved).ToListAsync(ct);
            return new(source.ParentItemId,BomDigest(source,candidate),
                FactoryEngineeringCutover.CheckBom(source,candidate,cutover,approved));
        }
        return null;
    }

    private static bool SafeText(string? value,int min,int max)=>
        !string.IsNullOrWhiteSpace(value)&&value.Trim().Length>=min&&
        value.Trim().Length<=max&&
        !value.Any(x=>char.IsControl(x)||x is ';' or '|');

    public static IEndpointRouteBuilder MapFactoryEcoApprovals(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-engineering/eco-approvals",
            async(Guid itemId,ProductionDb db,CancellationToken ct)=>{
                if(itemId==Guid.Empty)return Results.BadRequest(new{message="شناسه محصول معتبر نیست."});
                var rows=await db.FactoryEcoApprovals.AsNoTracking()
                    .Where(x=>x.ItemId==itemId).OrderByDescending(x=>x.RequestedUtc)
                    .Take(201).ToListAsync(ct);
                if(rows.Count>200)return Results.Conflict(new{
                    message="بیش از ۲۰۰ درخواست ECO ثبت شده است؛ فهرست ناقص ارائه نمی‌شود."});
                return Results.Ok(rows.Select(x=>new{
                    x.Id,x.ItemId,x.Kind,x.BaselineId,x.CandidateId,
                    cutoverOnJalali=PersianPlanningDates.Format(x.CutoverOn),
                    x.ApprovalReference,x.Reason,x.CandidateDigest,x.Status,
                    x.RequestedBy,x.RequestedUtc,x.QualityReviewedBy,x.QualityReviewedUtc,
                    x.QualityEvidenceReference,x.QualityObservation,x.ExecutedUtc
                }).ToArray());
            });

        api.MapPost("/factory-engineering/eco-approvals",
            async(FactoryEcoSubmissionInput input,ProductionDb db,HttpContext ctx,
                CancellationToken ct)=>{
                var kind=input.Kind?.Trim();var reference=input.ApprovalReference?.Trim();
                var reason=input.Reason?.Trim();
                if(kind is not ("BOM" or "Routing")||
                    input.BaselineId==Guid.Empty||input.CandidateId==Guid.Empty||
                    input.BaselineId==input.CandidateId||
                    !PersianPlanningDates.TryParse(input.CutoverOnJalali,out var date)||
                    date<Today()||!SafeText(reference,4,100)||!SafeText(reason,8,300))
                    return Results.BadRequest(new{message="نوع، شناسه نسخه‌ها، تاریخ مؤثر شمسی، مرجع و علت معتبر لازم است."});
                var actor=ctx.User.Identity?.Name?.Trim();
                if(!SafeText(actor,1,100))
                    return Results.Forbid();
                var owner=kind=="BOM"
                    ?await db.ManufacturingBomRevisions.AsNoTracking()
                        .Where(x=>x.Id==input.BaselineId).Select(x=>(Guid?)x.ParentItemId)
                        .SingleOrDefaultAsync(ct)
                    :await db.RoutingRevisions.AsNoTracking()
                        .Where(x=>x.Id==input.BaselineId).Select(x=>(Guid?)x.ItemId)
                        .SingleOrDefaultAsync(ct);
                if(owner is null)return Results.NotFound();
                await using var tx=await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,ct);
                try
                {
                    await FactoryRevisionGovernance.LockRevisionItem(db,kind,owner.Value,ct);
                    await LockGlobal(db,ct);
                }
                catch(SqlException)
                {
                    return Results.Conflict(new{message="دریافت قفل تصویب مهندسی امکان‌پذیر نیست."});
                }
                var check=await Preflight(db,kind,input.BaselineId,
                    input.CandidateId,date,ct);
                if(check is null)return Results.NotFound();
                if(check.Issues.Count>0)
                    return Results.Conflict(new{
                        message="نسخه پیشنهادی هنوز شرایط برش را ندارد.",issues=check.Issues});
                if(await db.FactoryEcoApprovals.AnyAsync(x=>
                    x.ApprovalReference==reference||
                    (x.Kind==kind&&x.CandidateId==input.CandidateId&&
                        x.Status!=FactoryEcoApprovalStatus.Rejected),ct)||
                    await db.FactoryEngineeringCutoverEvents.AnyAsync(x=>
                        x.ApprovalReference==reference,ct))
                    return Results.Conflict(new{message="مرجع ECO یا تصمیم فعال برای این نسخه تکراری است."});
                var row=new FactoryEcoApproval{Kind=kind,ItemId=owner.Value,
                    BaselineId=input.BaselineId,CandidateId=input.CandidateId,
                    CutoverOn=date,ApprovalReference=reference!,Reason=reason!,
                    CandidateDigest=check.Digest,RequestedBy=actor!};
                db.FactoryEcoApprovals.Add(row);
                try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
                catch(DbUpdateException){return Results.Conflict(new{
                    message="ثبت درخواست ECO هم‌زمان یا تکراری بود؛ تغییری اعمال نشد."});}
                return Results.Created($"/api/v1/factory-engineering/eco-approvals/{row.Id}",
                    new{row.Id,row.ApprovalReference,row.Status,row.RequestedBy,
                        row.RequestedUtc,message="درخواست و تأیید هویتی متقاضی ثبت شد؛ تا بررسی مستقل کیفیت امکان برش وجود ندارد."});
            }).RequireAuthorization("IpPlanningWrite");

        api.MapPost("/factory-engineering/eco-approvals/{id:guid}/quality-review",
            async(Guid id,FactoryEcoReviewInput input,ProductionDb db,HttpContext ctx,
                CancellationToken ct)=>{
                if(!SafeText(input.EvidenceReference,4,100)||
                   !SafeText(input.Observation,10,400))
                    return Results.BadRequest(new{message="مرجع مدرک و توضیح مستند کیفیت الزامی است."});
                var actor=ctx.User.Identity?.Name?.Trim();
                if(!SafeText(actor,1,100))return Results.Forbid();
                var target=await db.FactoryEcoApprovals.AsNoTracking()
                    .Where(x=>x.Id==id).Select(x=>new{x.Kind,x.ItemId})
                    .SingleOrDefaultAsync(ct);
                if(target is null)return Results.NotFound();
                await using var tx=await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,ct);
                try
                {
                    await FactoryRevisionGovernance.LockRevisionItem(
                        db,target.Kind,target.ItemId,ct);
                    await LockGlobal(db,ct);
                }
                catch(SqlException)
                {
                    return Results.Conflict(new{message="قفل بررسی مستقل ECO در دسترس نیست."});
                }
                var row=await db.FactoryEcoApprovals.SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(row is null)return Results.NotFound();
                if(row.Status!=FactoryEcoApprovalStatus.Submitted)
                    return Results.Conflict(new{message="این درخواست قبلاً بررسی یا اجرا شده است."});
                if(row.RequestedBy.Equals(actor,StringComparison.OrdinalIgnoreCase))
                    return Results.Conflict(new{message="تأییدکننده کیفیت باید فردی مستقل از درخواست‌کننده باشد."});
                var check=await Preflight(db,row.Kind,row.BaselineId,
                    row.CandidateId,row.CutoverOn,ct);
                if(check is null||check.Issues.Count>0||
                    check.ItemId!=row.ItemId||check.Digest!=row.CandidateDigest||
                    row.CutoverOn<Today())
                    return Results.Conflict(new{message="نسخه‌های مهندسی از زمان درخواست تغییر کرده یا اعتبار برش منقضی شده است؛ بررسی مستقل مجاز نیست."});
                row.QualityReviewedBy=actor;
                row.QualityReviewedUtc=DateTime.UtcNow;
                row.QualityEvidenceReference=input.EvidenceReference.Trim();
                row.QualityObservation=input.Observation.Trim();
                row.Status=input.Approve?FactoryEcoApprovalStatus.QualityApproved:
                    FactoryEcoApprovalStatus.Rejected;
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Ok(new{row.Id,row.Status,row.RequestedBy,
                    row.QualityReviewedBy,row.QualityReviewedUtc,
                    approved=input.Approve,
                    message=input.Approve?"امضای هویتی بررسی مستقل کیفیت ثبت شد؛ برش فقط برای همین نسخه و تاریخ مجاز است.":
                    "رد کیفیت ثبت شد؛ درخواست قبلی قابل استفاده مجدد نیست."});
            }).RequireAuthorization("IpQualityApprove");

        return routes;
    }
}
