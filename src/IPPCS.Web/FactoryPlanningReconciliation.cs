using Microsoft.EntityFrameworkCore;

// Stage 13: read-only decisions from internal planning assumptions. No links to
// warehouse, sales, purchasing, finance or HR and no plan/stock mutations.
public record FactorySopMpsBridge(Guid SopId,Guid MpsId,string FromJalali,string ThroughJalali,
    decimal HistoricalSopDemand,decimal CurrentConfirmedDemand,decimal ApprovedMpsQuantity,
    decimal DifferenceToSop,decimal DifferenceToCurrentConfirmed,int InWindowMpsLines,
    bool DemandSnapshotChanged,string ConfidenceNote);
public record FactoryMpsDemandGap(Guid DemandId,Guid ItemId,string Reference,DateOnly NeededOn,
    decimal ConfirmedQuantity,decimal ExactlyReferencedQuantity,decimal UncoveredQuantity,
    bool ReferenceChanged);
public record FactoryMpsAudit(Guid MpsId,int ApprovedLineCount,
    int ReferencedDemandCount,decimal ConfirmedDemandQuantity,decimal ConfirmedUncoveredQuantity,
    decimal OtherMpsQuantity,IReadOnlyList<FactoryMpsDemandGap> DemandGaps,
    IReadOnlyList<Guid> UnmatchedReferenceLines,string Disclaimer);
public record FactoryMrpSourceRisk(Guid ItemId,decimal GrossRequirement,
    decimal AssumedOpening,decimal AssumedExpectedReceipt,decimal ProposedReceipt,
    decimal UnallocatedProposalQuantity,int PastReleaseLots,
    decimal? AssumedCoveragePercent);
public record FactoryMrpRiskResult(Guid MpsId,int ProposedManufacturingLots,
    int ReleaseBeforeBaselineLots,IReadOnlyList<FactoryMrpSourceRisk> Rows,
    string Disclaimer);
public record FactoryCrpCenterStress(Guid WorkCenterId,decimal RequiredMinutes,
    decimal AvailableMinutes,decimal OverloadMinutes,decimal? UtilizationPercent,
    int OverloadedDays,int DaysWithoutCapacity,DateOnly? FirstOverload);
public record FactoryCrpStress(Guid MpsId,int DayCenterRows,
    IReadOnlyList<FactoryCrpCenterStress> Centers,string Disclaimer);
public record FactoryScenarioSensitivityInput(Guid MpsVersionId,string StartJalali,
    int HorizonDays,decimal BaselineCapacity,decimal ScenarioCapacity,
    List<FactoryApsUnavailableDay>? ScenarioUnavailableDays);
public record FactoryScenarioComparison(int PlannedOperations,int ScheduledOperations,
    int UnscheduledOperations,int LateOperations,decimal AllocatedMinutes);
public record FactoryScenarioSensitivity(Guid MpsId,string StartJalali,int HorizonDays,
    int MaintenanceDays,FactoryScenarioComparison Baseline,
    FactoryScenarioComparison Scenario,int ScheduledChange,int LateChange,
    IReadOnlyList<ApsUnscheduled> ScenarioUnscheduled,string Disclaimer);

public static class FactoryPlanningReconciliation
{
    public static FactorySopMpsBridge ReconcileSop(FactorySopReview sop,MpsVersion approved,
        IReadOnlyList<FactoryDemand> demands)
    {
        if(approved.State!=PlanningApproval.Approved)
            throw new InvalidOperationException("برای بررسی مغایرت فقط MPS مصوب معتبر است.");
        var lines=approved.Lines.Where(x=>x.NeededOn>=sop.From&&x.NeededOn<=sop.Through).ToArray();
        var current=demands.Where(x=>x.Category=="Confirmed"&&x.DueOn>=sop.From&&
            x.DueOn<=sop.Through).Sum(x=>x.Quantity);
        var planned=lines.Sum(x=>x.Quantity);
        return new(sop.Id,approved.Id,PersianPlanningDates.Format(sop.From),
            PersianPlanningDates.Format(sop.Through),sop.DemandQuantity,current,
            planned,planned-sop.DemandQuantity,planned-current,lines.Length,
            current!=sop.DemandQuantity,
            "مقایسه جمع مقادیر است و یکسانی ترکیب اقلام را اثبات نمی‌کند. S&OP تصویر تاریخی است؛ تغییر نیازهای فعلی به عنوان اختلاف مبنا گزارش می‌شود.");
    }

    public static FactoryMpsAudit AuditDemandReferences(MpsVersion version,
        IReadOnlyList<FactoryDemand> confirmed)
    {
        if(version.State!=PlanningApproval.Approved)
            throw new InvalidOperationException("بررسی انطباق فقط برای MPS مصوب است.");
        var ids=confirmed.Select(x=>x.Id).ToHashSet();
        var referenced=version.Lines.Where(x=>x.Reference.StartsWith("DEMAND:",StringComparison.Ordinal))
            .ToArray();
        var unmatched=referenced.Where(x=>!Guid.TryParseExact(
                x.Reference.Substring("DEMAND:".Length),"N",out var id) ||
                !ids.Contains(id)).Select(x=>x.Id).ToArray();
        var gaps=confirmed.Select(demand=>{
            var linked=referenced.Where(x=>x.Reference=="DEMAND:"+demand.Id.ToString("N"))
                .ToArray();
            var exact=linked.Where(x=>x.ItemId==demand.ItemId&&x.NeededOn==demand.DueOn)
                .Sum(x=>x.Quantity);
            return new FactoryMpsDemandGap(demand.Id,demand.ItemId,demand.Reference,
                demand.DueOn,demand.Quantity,exact,Math.Max(0m,demand.Quantity-exact),
                linked.Any(x=>x.ItemId!=demand.ItemId||x.NeededOn!=demand.DueOn||
                    x.Quantity!=demand.Quantity));
        }).OrderBy(x=>x.NeededOn).ThenBy(x=>x.DemandId).ToArray();
        var nonref=version.Lines.Where(x=>!x.Reference.StartsWith("DEMAND:",StringComparison.Ordinal))
            .Sum(x=>x.Quantity);
        return new(version.Id,version.Lines.Count,referenced.Length,
            gaps.Sum(x=>x.ConfirmedQuantity),gaps.Sum(x=>x.UncoveredQuantity),
            nonref,gaps,unmatched,
            "فقط ردیف‌های دارای شناسه DEMAND ثبت‌شده با نیاز قطعی تطبیق داده شده‌اند. ردیف‌های دستی ممکن است مربوط به نیاز مستقل دیگر باشند؛ کسری عددی نشان‌دهنده کسری واقعی سفارش یا موجودی نیست.");
    }

    public static FactoryMrpRiskResult MrpRisk(Guid mpsId,NetPeggingReport graph)
    {
        var rows=graph.Demands.GroupBy(x=>x.ItemId).Select(group=>{
            var ids=group.Select(x=>x.DemandKey).ToHashSet();
            var links=graph.Allocations.Where(x=>ids.Contains(x.DemandKey)).ToArray();
            var opening=links.Where(x=>x.SourceType=="PlanningOpeningBalance").Sum(x=>x.Quantity);
            var receipts=links.Where(x=>x.SourceType=="ExpectedReceipt").Sum(x=>x.Quantity);
            var proposed=links.Where(x=>x.SourceType=="ProposedManufacturingReceipt"||
                x.SourceType=="ProposedSupplyReceipt").Sum(x=>x.Quantity);
            var gross=group.Sum(x=>x.Quantity);
            var lots=graph.PlannedLots.Where(x=>x.ItemId==group.Key).ToArray();
            var firm=opening+receipts;
            return new FactoryMrpSourceRisk(group.Key,gross,opening,receipts,
                proposed,lots.Sum(x=>x.UnallocatedQuantity),
                lots.Count(x=>x.ReleaseBeforeBaseline),
                gross<=0?null:Math.Round(firm/gross*100m,2));
        }).OrderByDescending(x=>x.PastReleaseLots)
          .ThenBy(x=>x.AssumedCoveragePercent??0).ThenBy(x=>x.ItemId).ToArray();
        return new(mpsId,graph.PlannedLots.Count(x=>x.ProposalType=="Manufacturing"),
            graph.PlannedLots.Count(x=>x.ReleaseBeforeBaseline),rows,
            "پوشش مبتنی بر موجودی مبنای دستی و دریافت موردانتظار داخلی IPPCS است، نه موجودی واقعی انبار یا رسید تأییدشده. ساخت/تأمین پیشنهادی قطعی نیست.");
    }

    public static FactoryCrpStress CenterStress(Guid mpsId,IReadOnlyList<CrpRow> entries)
    {
        var centerRows=entries.GroupBy(x=>x.WorkCenterId).Select(group=>{
            var required=group.Sum(x=>x.RequiredMinutes);
            var available=group.Sum(x=>x.CapacityMinutes);
            var overloaded=group.Where(x=>x.RequiredMinutes>x.CapacityMinutes).ToArray();
            return new FactoryCrpCenterStress(group.Key,required,available,
                group.Sum(x=>x.OverloadMinutes),
                available<=0?null:Math.Round(required/available*100m,2),
                overloaded.Length,group.Count(x=>x.CapacityMinutes<=0&&x.RequiredMinutes>0),
                overloaded.Length==0?null:overloaded.Min(x=>x.NeededOn));
        }).OrderByDescending(x=>x.OverloadMinutes)
          .ThenByDescending(x=>x.DaysWithoutCapacity).ToArray();
        return new(mpsId,entries.Count,centerRows,
            "تجمیع نیاز مرکز کاری و ظرفیت شیفت‌های تعریف‌شده است؛ جابه‌جایی روزانه عملیات یا همپوشانی اجرایی فقط در APS کنترل می‌شود.");
    }

    public static FactoryScenarioComparison Summarize(ApsResult schedule,int taskCount)=>
        new(taskCount,schedule.Allocations.Count,schedule.Unscheduled.Count,
            schedule.Allocations.Count(x=>x.Late),schedule.Allocations.Sum(x=>x.DurationMinutes));

    public static FactoryScenarioSensitivity CompareScenario(Guid id,DateOnly start,
        int horizonDays,IReadOnlyList<ApsTask> tasks,IReadOnlyList<WorkShift> shifts,
        decimal baselineCapacity,decimal scenarioCapacity,
        IReadOnlySet<(Guid WorkCenterId,DateOnly Date)> blocked)
    {
        var baseline=FiniteFactoryScheduler.Schedule(tasks,shifts,start,horizonDays,baselineCapacity);
        var scenario=FiniteFactoryScheduler.Schedule(tasks,shifts,start,horizonDays,
            scenarioCapacity,unavailableDays:blocked);
        var baseTotals=Summarize(baseline,tasks.Count);
        var changed=Summarize(scenario,tasks.Count);
        return new(id,PersianPlanningDates.Format(start),horizonDays,blocked.Count,
            baseTotals,changed,changed.ScheduledOperations-baseTotals.ScheduledOperations,
            changed.LateOperations-baseTotals.LateOperations,scenario.Unscheduled,
            "مقایسه فرضی ظرفیت و تعطیلی مرکز است؛ مواد و رزروهای واقعی به این سناریو تخصیص نیافته‌اند و هیچ تغییری در برنامه ثبت نمی‌شود.");
    }

    public static IEndpointRouteBuilder MapPlanningReconciliation(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-sop/{sopId:guid}/reconcile-mps/{mpsId:guid}",
            async(Guid sopId,Guid mpsId,ProductionDb db,CancellationToken ct)=>{
            var sop=await db.FactorySopReviews.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==sopId,ct);
            var mps=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==mpsId,ct);
            if(sop is null||mps is null)return Results.NotFound();
            if(mps.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="نسخه MPS باید مصوب باشد."});
            var demands=await db.FactoryDemands.AsNoTracking()
                .Where(x=>x.DueOn>=sop.From&&x.DueOn<=sop.Through&&x.Category=="Confirmed")
                .ToListAsync(ct);
            return Results.Ok(ReconcileSop(sop,mps,demands));
        });
        api.MapGet("/mps/{mpsId:guid}/demand-audit",
            async(Guid mpsId,ProductionDb db,CancellationToken ct)=>{
            var mps=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==mpsId,ct);
            if(mps is null)return Results.NotFound();
            if(mps.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="نسخه MPS باید مصوب باشد."});
            var demand=await db.FactoryDemands.AsNoTracking()
                .Where(x=>x.Category=="Confirmed").ToListAsync(ct);
            return Results.Ok(AuditDemandReferences(mps,demand));
        });
        api.MapGet("/mrp/{mpsId:guid}/assumption-risks",
            async(Guid mpsId,ProductionDb db,CancellationToken ct)=>{
            var mps=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==mpsId,ct);
            if(mps is null)return Results.NotFound();
            if(mps.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="نسخه MPS باید مصوب باشد."});
            var boms=await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x=>x.Lines).ToListAsync(ct);
            var balances=await db.PlanningBalances.AsNoTracking().ToListAsync(ct);
            var receipts=await db.PlanningExpectedReceipts.AsNoTracking().ToListAsync(ct);
            var types=await db.Items.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Type,ct);
            if(balances.Count>0&&balances.Any(x=>x.SnapshotDate!=balances[0].SnapshotDate))
                return Results.Conflict(new{message="تاریخ مبنای داده‌های برنامه‌ریزی یکسان نیست."});
            try{
                var mrp=PlanningCalculators.RunMrp(mps.Lines,boms,balances,receipts,types);
                var graph=NetMrpDemandPegging.Build(mps.Lines,boms,balances,receipts,types,mrp);
                return Results.Ok(MrpRisk(mpsId,graph));
            }catch(Exception ex) when(ex is InvalidOperationException or OverflowException or ArgumentOutOfRangeException){
                return Results.UnprocessableEntity(new{message=ex.Message});
            }
        });
        api.MapGet("/crp/{mpsId:guid}/center-stress",
            async(Guid mpsId,ProductionDb db,CancellationToken ct)=>{
            var mps=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==mpsId,ct);
            if(mps is null)return Results.NotFound();
            if(mps.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="نسخه MPS باید مصوب باشد."});
            var routes=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                .ToListAsync(ct);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            try{
                return Results.Ok(CenterStress(mpsId,
                    PlanningCalculators.RunRoughCutCrp(mps.Lines,routes,shifts)));
            }catch(Exception ex)when(ex is InvalidOperationException or OverflowException){
                return Results.UnprocessableEntity(new{message=ex.Message});
            }
        });
        api.MapPost("/factory-whatif/capacity-maintenance",
            async(FactoryScenarioSensitivityInput input,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(input.StartJalali,out var start)
                ||input.HorizonDays is <1 or >90 ||input.BaselineCapacity is <=0 or >2
                ||input.ScenarioCapacity is <=0 or >2 ||(input.ScenarioUnavailableDays?.Count??0)>100)
                return Results.BadRequest(new{message="تاریخ شمسی، ظرفیت یا افق سناریو معتبر نیست."});
            var mps=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==input.MpsVersionId,ct);
            if(mps is null)return Results.NotFound();
            if(mps.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="شبیه‌سازی فقط برای MPS مصوب مجاز است."});
            var centers=await db.WorkCenters.AsNoTracking().Select(x=>x.Id).ToListAsync(ct);
            var validCenters=centers.ToHashSet();
            var blocked=new HashSet<(Guid WorkCenterId,DateOnly Date)>();
            foreach(var row in input.ScenarioUnavailableDays??[])
            {
                if(!validCenters.Contains(row.WorkCenterId) ||
                   !PersianPlanningDates.TryParse(row.DateJalali,out var day)||
                   day<start||day>=start.AddDays(input.HorizonDays)||
                   string.IsNullOrWhiteSpace(row.Reason)||row.Reason.Length>180)
                    return Results.BadRequest(new{message="مرکز، تاریخ یا دلیل عدم‌دسترسی معتبر نیست."});
                blocked.Add((row.WorkCenterId,day));
            }
            var routes=await db.RoutingRevisions.AsNoTracking()
                .Include(x=>x.Operations).Where(x=>x.State==RoutingState.Approved).ToListAsync(ct);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            var tasks=new List<ApsTask>();
            foreach(var line in mps.Lines)
            {
                var found=routes.Where(x=>x.ItemId==line.ItemId&&x.EffectiveFrom<=line.NeededOn&&
                    (x.EffectiveTo is null||line.NeededOn<=x.EffectiveTo.Value)).ToArray();
                if(found.Length!=1||found[0].Operations.Count==0||
                    found[0].Operations.Any(x=>x.YieldPercent<=0||x.RunMinutesPerUnit<=0)||
                    line.Quantity<=0)
                    return Results.UnprocessableEntity(new{message="مسیر ساخت مصوب، بازده یا مقدار برنامه نامعتبر است."});
                tasks.AddRange(found[0].Operations.Select(op=>new ApsTask(line.Id,line.ItemId,
                    line.NeededOn,line.Quantity,found[0].Id,op.Sequence,op.WorkCenterId,
                    op.SetupMinutes+op.RunMinutesPerUnit*line.Quantity*100m/op.YieldPercent,op.Name)));
            }
            try{return Results.Ok(CompareScenario(mps.Id,start,input.HorizonDays,tasks,shifts,
                    input.BaselineCapacity,input.ScenarioCapacity,blocked));}
            catch(ArgumentException ex){return Results.BadRequest(new{message=ex.Message});}
        }).RequireAuthorization("IpPlanningWrite");
        return routes;
    }
}
