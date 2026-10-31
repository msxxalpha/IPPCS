using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Stage17: transparent, reversible planning revisions kept within IPPCS.
// Audit is stored as an algorithm-tagged, immutable planning snapshot in
// existing PlanningCalculations: no new SQL table or ERP integration.
public record FactoryMpsControlledRevisionInput(string Code,string Reason);
public record FactoryMpsSnapshotLine(Guid ItemId,DateOnly NeededOn,decimal Quantity,string Reference);
public record FactoryMpsRevisionEvidence(Guid SourceVersionId,Guid DraftVersionId,
    string SourceCode,string DraftCode,string Reason,string Actor,DateTime ChangedUtc,
    IReadOnlyList<FactoryMpsSnapshotLine> OriginalLines);
public record FactoryMpsChangeLine(Guid ItemId,DateOnly NeededOn,
    decimal BeforeQuantity,decimal AfterQuantity,decimal Difference);
public record FactoryMpsCapacityImpact(Guid WorkCenterId,DateOnly NeededOn,
    decimal BeforeMinutes,decimal AfterMinutes,decimal DeltaMinutes,
    decimal BeforeOverloadMinutes,decimal AfterOverloadMinutes);
public record FactoryMpsMaterialImpact(Guid ItemId,DateOnly NeededOn,
    decimal BeforeGross,decimal AfterGross,decimal GrossDifference,
    decimal BeforeSuggested,decimal AfterSuggested,decimal SuggestedDifference);
public record FactoryMpsChangeImpact(Guid SourceId,Guid CandidateId,
    PlanningApproval SourceState,PlanningApproval CandidateState,
    decimal BeforeQuantity,decimal AfterQuantity,decimal DeltaQuantity,
    IReadOnlyList<FactoryMpsChangeLine> Demand,
    IReadOnlyList<FactoryMpsCapacityImpact> Capacity,
    IReadOnlyList<FactoryMpsMaterialImpact> Materials,
    string Disclaimer);

public static class FactoryMpsChangeControl
{
    public const string RevisionAlgorithm="MPS-CONTROLLED-REVISION-1";
    public const string ApprovalAlgorithm="MPS-APPROVAL-AUDIT-1";
    private static readonly JsonSerializerOptions AuditJsonOptions=new(){
        PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};

    internal static async Task LockMps(ProductionDb db,CancellationToken ct)
    {
        const string resource="IPPCS:MPS:ControlledChanges";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DECLARE @rc int;
               EXEC @rc=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',
               @LockOwner='Transaction',@LockTimeout=10000;
               IF @rc < 0 THROW 51061, 'MPS change lock unavailable', 1;",ct);
    }

    public static IReadOnlyList<FactoryMpsChangeLine> CompareDemand(
        IReadOnlyList<MpsDemandLine> baseline,IReadOnlyList<MpsDemandLine> candidate)
    {
        var before=baseline.GroupBy(x=>(x.ItemId,x.NeededOn))
            .ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Quantity));
        var after=candidate.GroupBy(x=>(x.ItemId,x.NeededOn))
            .ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Quantity));
        return before.Keys.Concat(after.Keys).Distinct()
            .Select(x=>new FactoryMpsChangeLine(x.ItemId,x.NeededOn,
                before.GetValueOrDefault(x),after.GetValueOrDefault(x),
                after.GetValueOrDefault(x)-before.GetValueOrDefault(x)))
            .Where(x=>x.Difference!=0).OrderBy(x=>x.NeededOn)
            .ThenBy(x=>x.ItemId).ToArray();
    }

    public static FactoryMpsChangeImpact Compare(
        MpsVersion source,MpsVersion candidate,
        IReadOnlyList<CrpRow> beforeCrp,IReadOnlyList<CrpRow> afterCrp,
        IReadOnlyList<MrpRow> beforeMrp,IReadOnlyList<MrpRow> afterMrp)
    {
        var demand=CompareDemand(source.Lines,candidate.Lines);
        var crpA=beforeCrp.ToDictionary(x=>(x.WorkCenterId,x.NeededOn));
        var crpB=afterCrp.ToDictionary(x=>(x.WorkCenterId,x.NeededOn));
        var capacity=crpA.Keys.Concat(crpB.Keys).Distinct().Select(key=>{
            crpA.TryGetValue(key,out var old);crpB.TryGetValue(key,out var next);
            return new FactoryMpsCapacityImpact(key.WorkCenterId,key.NeededOn,
                old?.RequiredMinutes??0,next?.RequiredMinutes??0,
                (next?.RequiredMinutes??0)-(old?.RequiredMinutes??0),
                old?.OverloadMinutes??0,next?.OverloadMinutes??0);
        }).Where(x=>x.DeltaMinutes!=0||x.BeforeOverloadMinutes!=x.AfterOverloadMinutes)
            .OrderBy(x=>x.NeededOn).ThenBy(x=>x.WorkCenterId).ToArray();
        var mrpA=beforeMrp.ToDictionary(x=>(x.ItemId,x.NeededOn));
        var mrpB=afterMrp.ToDictionary(x=>(x.ItemId,x.NeededOn));
        var material=mrpA.Keys.Concat(mrpB.Keys).Distinct().Select(key=>{
            mrpA.TryGetValue(key,out var old);mrpB.TryGetValue(key,out var next);
            return new FactoryMpsMaterialImpact(key.ItemId,key.NeededOn,
                old?.GrossRequired??0,next?.GrossRequired??0,
                (next?.GrossRequired??0)-(old?.GrossRequired??0),
                old?.SuggestedQuantity??0,next?.SuggestedQuantity??0,
                (next?.SuggestedQuantity??0)-(old?.SuggestedQuantity??0));
        }).Where(x=>x.GrossDifference!=0||x.SuggestedDifference!=0)
            .OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId).ToArray();
        return new(source.Id,candidate.Id,source.State,candidate.State,
            source.Lines.Sum(x=>x.Quantity),candidate.Lines.Sum(x=>x.Quantity),
            candidate.Lines.Sum(x=>x.Quantity)-source.Lines.Sum(x=>x.Quantity),
            demand,capacity,material,
            "گزارش اثر تغییر بر اساس BOM، مسیر، شیفت، مانده‌های فرضی و دریافت‌های ثبت‌شده فعلی محاسبه می‌شود. این خروجی خودِ تغییر برنامه را تأیید نمی‌کند و موجودی واقعی انبار یا تعهد خرید نیست.");
    }

    public const string ImpactApprovalAlgorithm="MPS-REVISION-IMPACT-APPROVAL-1";
    internal static FactoryMpsRevisionEvidence? ParseOrigin(string json)=>
        JsonSerializer.Deserialize<FactoryMpsRevisionEvidence>(json,AuditJsonOptions);

    internal static async Task<FactoryMpsChangeImpact> CalculateActualImpact(
        ProductionDb db,MpsVersion source,MpsVersion candidate,CancellationToken ct)
    {
        var routings=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
            .ToListAsync(ct);
        var productCapacityRates=await WorkCenterProductCapacityRules.LoadActiveRates(db,ct);
        var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
        var boms=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
            .ToListAsync(ct);
        var balances=await db.PlanningBalances.AsNoTracking().ToListAsync(ct);
        var receipts=await db.PlanningExpectedReceipts.AsNoTracking().ToListAsync(ct);
        var types=await db.Items.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Type,ct);
        if(balances.Count>0&&balances.Any(x=>x.SnapshotDate!=balances[0].SnapshotDate))
            throw new InvalidOperationException("مبناهای مانده فرضی برنامه‌ریزی یکسان نیستند.");
        return Compare(source,candidate,
            PlanningCalculators.RunRoughCutCrp(source.Lines,routings,shifts,productCapacityRates),
            PlanningCalculators.RunRoughCutCrp(candidate.Lines,routings,shifts,productCapacityRates),
            PlanningCalculators.RunMrp(source.Lines,boms,balances,receipts,types),
            PlanningCalculators.RunMrp(candidate.Lines,boms,balances,receipts,types));
    }

    public static IEndpointRouteBuilder MapMpsChangeControl(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/mps/{id:guid}/change-evidence",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
                var version=await db.MpsVersions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(version is null)return Results.NotFound();
                var events=await db.PlanningCalculations.AsNoTracking()
                    .Where(x=>x.MpsVersionId==id&&
                        (x.AlgorithmVersion==RevisionAlgorithm||
                         x.AlgorithmVersion==ApprovalAlgorithm||
                         x.AlgorithmVersion==ImpactApprovalAlgorithm))
                    .OrderBy(x=>x.CalculatedUtc).ToListAsync(ct);
                return Results.Ok(new{version.Id,version.Code,version.State,
                    events=events.Select(x=>new{x.Id,x.CalculatedUtc,x.AlgorithmVersion,
                        original=JsonSerializer.Deserialize<JsonElement>(x.InputSnapshotJson),
                        resulting=JsonSerializer.Deserialize<JsonElement>(x.ResultSnapshotJson)})});
            });
        api.MapPost("/mps/{id:guid}/controlled-revision",
            async(Guid id,FactoryMpsControlledRevisionInput input,ProductionDb db,
                HttpContext ctx,CancellationToken ct)=>{
                if(string.IsNullOrWhiteSpace(input.Code)||input.Code.Trim().Length>64||
                    string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Trim().Length>500)
                    return Results.BadRequest(new{message="کد و دلیل مستند بازنگری MPS الزامی و محدود است."});
                var actor=ctx.User.Identity?.Name??"unknown";
                var code=input.Code.Trim();
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{await LockMps(db,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل بازنگری MPS در دسترس نیست."});}
                var original=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(original is null)return Results.NotFound();
                if(original.State!=PlanningApproval.Approved||original.Lines.Count==0)
                    return Results.Conflict(new{message="فقط یک برنامه مصوب دارای ردیف را می‌توان با تاریخچه بازنگری کرد."});
                if(await db.MpsVersions.AsNoTracking().AnyAsync(x=>x.Code==code,ct))
                    return Results.Conflict(new{message="کد نسخه جدید قبلاً وجود دارد."});
                if(original.Lines.Count>5000)
                    return Results.Conflict(new{message="تعداد ردیف‌های MPS بیشتر از سقف قابل کپی و ممیزی است."});
                // Only one pending revision from this approved baseline. Resolve
                // its state via the saved original-ID evidence, never text code.
                var revisions=await db.PlanningCalculations.AsNoTracking()
                    .Where(x=>x.AlgorithmVersion==RevisionAlgorithm)
                    .Select(x=>new{x.MpsVersionId,x.InputSnapshotJson}).ToListAsync(ct);
                foreach(var evidence in revisions)
                {
                    var e=JsonSerializer.Deserialize<FactoryMpsRevisionEvidence>(evidence.InputSnapshotJson,AuditJsonOptions);
                    if(e?.SourceVersionId==id&&await db.MpsVersions.AsNoTracking()
                        .AnyAsync(x=>x.Id==evidence.MpsVersionId&&x.State==PlanningApproval.Draft,ct))
                        return Results.Conflict(new{message="بازنگری پیش‌نویس باز از همین نسخه وجود دارد؛ ابتدا آن را تعیین تکلیف کنید."});
                }
                var draft=new MpsVersion{Code=code,State=PlanningApproval.Draft};
                db.MpsVersions.Add(draft);
                foreach(var line in original.Lines)
                    db.MpsDemandLines.Add(new MpsDemandLine{
                        MpsVersionId=draft.Id,ItemId=line.ItemId,NeededOn=line.NeededOn,
                        Quantity=line.Quantity,Reference=line.Reference});
                var immutable=new FactoryMpsRevisionEvidence(original.Id,draft.Id,
                    original.Code,code,input.Reason.Trim(),actor,DateTime.UtcNow,
                    original.Lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId).Select(x=>
                        new FactoryMpsSnapshotLine(x.ItemId,x.NeededOn,x.Quantity,x.Reference)).ToArray());
                db.PlanningCalculations.Add(new PlanningCalculation{
                    MpsVersionId=draft.Id,AlgorithmVersion=RevisionAlgorithm,
                    InputSnapshotJson=JsonSerializer.Serialize(immutable,AuditJsonOptions),
                    ResultSnapshotJson=JsonSerializer.Serialize(new{
                        status="Draft",lineCount=original.Lines.Count,
                        important="نسخه مصوب اصلی تغییری نکرده است."})});
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Created($"/api/v1/mps/{draft.Id}/change-evidence",
                    new{draft.Id,draft.Code,sourceVersionId=original.Id,
                        clonedLines=original.Lines.Count,reason=input.Reason.Trim()});
            }).RequireAuthorization("IpPlanningWrite");

        api.MapGet("/mps/compare-change-impact",
            async(Guid sourceId,Guid candidateId,ProductionDb db,CancellationToken ct)=>{
                var versions=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                    .Where(x=>x.Id==sourceId||x.Id==candidateId).ToListAsync(ct);
                var source=versions.SingleOrDefault(x=>x.Id==sourceId);
                var candidate=versions.SingleOrDefault(x=>x.Id==candidateId);
                if(source is null||candidate is null)return Results.NotFound();
                if(sourceId==candidateId||source.State!=PlanningApproval.Approved||
                    source.Lines.Count>5000||candidate.Lines.Count>5000)
                    return Results.Conflict(new{message="مقایسه نیازمند دو نسخه مجزا، مبنای مصوب و تعداد ردیف محدود است."});
                var revisions=await db.PlanningCalculations.AsNoTracking()
                    .Where(x=>x.MpsVersionId==candidateId&&x.AlgorithmVersion==RevisionAlgorithm)
                    .ToListAsync(ct);
                if(!revisions.Any(x=>JsonSerializer.Deserialize<FactoryMpsRevisionEvidence>(
                    x.InputSnapshotJson,AuditJsonOptions)?.SourceVersionId==sourceId))
                    return Results.Conflict(new{message="نسخه انتخابی از بازنگری مستند این MPS منشأ نگرفته است."});
                try{return Results.Ok(await CalculateActualImpact(db,source,candidate,ct));}
                catch(Exception ex)when(ex is InvalidOperationException or OverflowException or ArgumentOutOfRangeException)
                    {return Results.UnprocessableEntity(new{message=ex.Message});}
            }).RequireAuthorization("IpPlanningWrite");
        return routes;
    }
}
