using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

// Finite routing/shift check for unconfirmed *internal MRP proposals* only.
// Never creates manufacturing orders, reservations, warehouse movements or firm dates.
public record MrpLotCapacityRow(string LotKey, Guid ItemId, decimal Quantity,
    DateOnly ProposedRelease, DateOnly ReceiptDue, Guid? RoutingRevisionId,
    int OperationsRequired, int OperationsScheduled, string Status, string? Reason,
    bool Late);
public record MrpLotCapacityResult(IReadOnlyList<MrpLotCapacityRow> Lots,
    IReadOnlyList<ApsAllocation> Allocations, IReadOnlyList<ApsUnscheduled> Unscheduled,
    int OnTimeLots, int LateLots, int BlockedLots);

public static class MrpLotCapacityPreview
{
    private static Guid TaskId(string lotKey)
    {
        var digest=SHA256.HashData(Encoding.UTF8.GetBytes(lotKey));
        return new Guid(digest.AsSpan(0,16));
    }

    public static MrpLotCapacityResult Evaluate(
        NetPeggingReport graph,
        IReadOnlyList<RoutingRevision> routings,
        IReadOnlyList<WorkShift> shifts,
        DateOnly baseline,
        int horizonDays=30,
        decimal capacityMultiplier=1m)
    {
        if(horizonDays is <1 or >90 || capacityMultiplier<=0m || capacityMultiplier>2m)
            throw new ArgumentException("افق ۱ تا ۹۰ روز و ضریب ظرفیت مثبت تا ۲ لازم است.");
        var end=baseline.AddDays(horizonDays);
        var tasks=new List<ApsTask>();
        var earliest=new Dictionary<Guid,DateOnly>();
        var initial=new List<MrpLotCapacityRow>();
        var lotKeys=new HashSet<string>(StringComparer.Ordinal);
        var approved=routings.Where(x=>x.State==RoutingState.Approved).ToArray();
        foreach(var lot in graph.PlannedLots.Where(x=>x.ProposalType=="Manufacturing")
            .OrderBy(x=>x.ReceiptOn).ThenBy(x=>x.LotKey,StringComparer.Ordinal))
        {
            if(!lotKeys.Add(lot.LotKey))
                throw new InvalidOperationException("شناسه بچ پیشنهادی تکراری است.");
            var problem="";
            RoutingRevision? route=null;
            if(lot.Quantity<=0 || lot.ReleaseOn>lot.ReceiptOn || lot.ReleaseOn<baseline)
                problem="مقدار، تاریخ آزادسازی یا تقدم زمانی بچ نامعتبر است.";
            else if(lot.ReleaseOn>=end || lot.ReceiptOn>=end)
                problem="تاریخ شروع یا موعد بچ از افق پیش‌نمایش بیرون است.";
            else
            {
                var matches=approved.Where(x=>x.ItemId==lot.ItemId &&
                    x.EffectiveFrom<=lot.ReleaseOn &&
                    (x.EffectiveTo is null || lot.ReleaseOn<=x.EffectiveTo.Value)).ToList();
                if(matches.Count!=1 || matches[0].Operations.Count==0)
                    problem="برای تاریخ آزادسازی بچ دقیقاً یک مسیر ساخت مصوب دارای عملیات لازم است.";
                else
                {
                    route=matches[0];
                    var ops=route.Operations.OrderBy(x=>x.Sequence).ToArray();
                    if(ops.Any(x=>x.WorkCenterId==Guid.Empty||x.SetupMinutes<0||
                        x.RunMinutesPerUnit<=0||x.YieldPercent<=0||x.YieldPercent>100) ||
                        ops.GroupBy(x=>x.Sequence).Any(x=>x.Key<=0||x.Count()>1))
                        problem="مراحل، مرکز کاری، زمان استاندارد یا بازده مسیر ساخت نامعتبر است.";
                    else
                    {
                        var durations=ops.Select(x=>x.SetupMinutes+
                            lot.Quantity*x.RunMinutesPerUnit*100m/x.YieldPercent).ToArray();
                        if(durations.Any(x=>x<=0||x>20000))
                            problem="زمان موردنیاز یکی از عملیات بچ خارج از محدوده زمان‌بندی است.";
                        else
                        {
                            var taskId=TaskId(lot.LotKey);
                            if(earliest.ContainsKey(taskId))
                                throw new InvalidOperationException("شناسه محاسباتی دو بچ متفاوت یکسان شده است.");
                            earliest.Add(taskId,lot.ReleaseOn);
                            for(var i=0;i<ops.Length;i++)
                                tasks.Add(new ApsTask(taskId,lot.ItemId,lot.ReceiptOn,lot.Quantity,
                                    route.Id,ops[i].Sequence,ops[i].WorkCenterId,durations[i],ops[i].Name));
                        }
                    }
                }
            }
            initial.Add(new MrpLotCapacityRow(lot.LotKey,lot.ItemId,lot.Quantity,lot.ReleaseOn,
                lot.ReceiptOn,route?.Id,route?.Operations.Count??0,0,
                string.IsNullOrEmpty(problem)?"Pending":"Blocked",
                string.IsNullOrEmpty(problem)?null:problem,false));
        }
        var planned=FiniteFactoryScheduler.Schedule(tasks,shifts,baseline,horizonDays,
            capacityMultiplier,earliestStartDates:earliest);
        var finished=initial.Select(lot=>{
            if(lot.Status=="Blocked")return lot;
            var allocations=planned.Allocations.Where(x=>x.ItemId==lot.ItemId &&
                x.DueOn==lot.ReceiptDue).ToArray();
            if(allocations.Any(x=>x.StartDate<lot.ProposedRelease))
                throw new InvalidOperationException("زمان‌بند عملیاتی را پیش از تاریخ آزادسازی بچ قرار داده است.");
            var done=allocations.Length==lot.OperationsRequired;
            var late=allocations.Any(x=>x.Late);
            var status=!done?"Unscheduled":late?"Late":"OnTime";
            return lot with{OperationsScheduled=allocations.Length,Status=status,
                Late=late,Reason=done?null:
                    "یک یا چند عملیات به دلیل محدودیت شیفت یا ظرفیت در افق انتخابی قابل تخصیص نیستند."};
        }).ToArray();
        return new MrpLotCapacityResult(finished,planned.Allocations,planned.Unscheduled,
            finished.Count(x=>x.Status=="OnTime"),finished.Count(x=>x.Status=="Late"),
            finished.Count(x=>x.Status is "Blocked" or "Unscheduled"));
    }

    public static IEndpointRouteBuilder MapMrpLotCapacityPreview(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/mrp/{id:guid}/lot-capacity-preview",async(Guid id,
            int? horizonDays,decimal? capacityMultiplier,ProductionDb db,CancellationToken ct)=>{
            var days=horizonDays??30;
            var factor=capacityMultiplier??1m;
            if(days is <1 or >90 ||factor<=0m||factor>2m)
                return Results.BadRequest(new{message="افق ۱ تا ۹۰ روز و ضریب ظرفیت ۰ تا ۲ معتبر لازم است."});
            var mps=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(mps is null)return Results.NotFound();
            if(mps.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="نسخه MPS باید مصوب باشد."});
            var balances=await db.PlanningBalances.AsNoTracking().ToListAsync(ct);
            if(balances.Count==0)
                return Results.UnprocessableEntity(new{message="مبنای موجودی برنامه‌ریزی تعریف نشده است."});
            var start=balances[0].SnapshotDate;
            if(balances.Any(x=>x.SnapshotDate!=start))
                return Results.Conflict(new{message="تاریخ مبنای موجودی‌ها یکسان نیست."});
            if(mps.Lines.Any(x=>x.NeededOn<start))
                return Results.Conflict(new{message="موعد MPS پیش از مبنای موجودی است."});
            var boms=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines).ToListAsync(ct);
            var receipts=await db.PlanningExpectedReceipts.AsNoTracking().ToListAsync(ct);
            if(receipts.Any(x=>x.AvailableOn<=start))
                return Results.Conflict(new{message="دریافت برنامه‌ای تکراری نسبت به تاریخ مبنا موجود است."});
            var types=await db.Items.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Type,ct);
            var routings=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>x.State==RoutingState.Approved).ToListAsync(ct);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            try
            {
                var mrp=PlanningCalculators.RunMrp(mps.Lines,boms,balances,receipts,types);
                var graph=NetMrpDemandPegging.Build(mps.Lines,boms,balances,receipts,types,mrp);
                var report=Evaluate(graph,routings,shifts,start,days,factor);
                return Results.Ok(new{
                    kind="UnconfirmedNetMrpBatchCapacityPreview",algorithm="MRP-CRP-PREVIEW-1",
                    baselineJalali=PersianPlanningDates.Format(start),horizonDays=days,
                    capacityMultiplier=factor,report.OnTimeLots,report.LateLots,report.BlockedLots,
                    disclaimer="پیش‌نمایش محدودیت ظرفیت برای بچ‌های پیشنهادی MRP: تاریخ شروع بچ، تقدم مراحل و عدم تداخل مرکز کاری کنترل می‌شود. دریافت فرضی مواد قطعی نیست؛ ترتیب مصرف بین بچ‌های ساخت و دستورات تولید فعال، ابزار، تعمیرات و اپراتور لحاظ نشده است. هیچ دستور ساخت یا رزروی ایجاد نمی‌شود.",
                    lots=report.Lots.Select(x=>new{x.LotKey,x.ItemId,x.Quantity,
                        proposedReleaseJalali=PersianPlanningDates.Format(x.ProposedRelease),
                        receiptDueJalali=PersianPlanningDates.Format(x.ReceiptDue),
                        x.RoutingRevisionId,x.OperationsRequired,x.OperationsScheduled,
                        x.Status,x.Reason,x.Late}),
                    allocations=report.Allocations.Select(x=>new{x.ItemId,x.WorkCenterId,
                        x.Sequence,x.Quantity,x.OperationName,x.DurationMinutes,x.Late,
                        lotKey=report.Lots.Single(l=>l.ItemId==x.ItemId&&l.ReceiptDue==x.DueOn).LotKey,
                        startJalali=PersianPlanningDates.Format(x.StartDate),
                        startTime=x.StartsAt.ToString("HH:mm"),
                        finishJalali=PersianPlanningDates.Format(x.FinishDate),
                        finishTime=x.FinishesAt.ToString("HH:mm")}),
                    report.Unscheduled
                });
            }
            catch(Exception ex)when(ex is InvalidOperationException or OverflowException or
                ArgumentOutOfRangeException or ArgumentException)
            {return Results.UnprocessableEntity(new{message=ex.Message});}
        });
        return routes;
    }
}
