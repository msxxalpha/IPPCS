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
        decimal capacityMultiplier=1m,
        IReadOnlyList<ManufacturingOrder>? activeOrders=null,
        IReadOnlyList<FactoryClockSession>? clockSessions=null,
        IReadOnlyList<FactoryCapacityReservation>? reservations=null)
    {
        if(horizonDays is <1 or >90 || capacityMultiplier<=0m || capacityMultiplier>2m)
            throw new ArgumentException("افق ۱ تا ۹۰ روز و ضریب ظرفیت مثبت تا ۲ لازم است.");
        var end=baseline.AddDays(horizonDays);
        var tasks=new List<ApsTask>();
        var earliest=new Dictionary<Guid,DateOnly>();
        var initial=new List<MrpLotCapacityRow>();
        var lotKeys=new HashSet<string>(StringComparer.Ordinal);
        var manufacturing=graph.PlannedLots.Where(x=>x.ProposalType=="Manufacturing")
            .ToDictionary(x=>x.LotKey,StringComparer.Ordinal);
        var demands=graph.Demands.ToDictionary(x=>x.DemandKey,StringComparer.Ordinal);
        var dependentIds=new Dictionary<Guid,HashSet<Guid>>();
        foreach(var allocation in graph.Allocations.Where(x=>
            x.SourceType=="ProposedManufacturingReceipt" && x.Quantity>0))
        {
            if(!demands.TryGetValue(allocation.DemandKey,out var demand))
                throw new InvalidOperationException("شناسه منشأ نیاز پیشنهادی یافت نشد.");
            if(demand.ParentLotKey is null)continue; // A direct MPS demand has no parent manufacturing lot.
            if(!manufacturing.ContainsKey(demand.ParentLotKey)||
                !manufacturing.ContainsKey(allocation.SourceKey))
                throw new InvalidOperationException("بچ ساخت منبع یا مصرف‌کننده در گراف MRP یافت نشد.");
            var parentId=TaskId(demand.ParentLotKey);
            var childId=TaskId(allocation.SourceKey);
            if(parentId==childId)
                throw new InvalidOperationException("بچ ساخت نمی‌تواند مصرف‌کننده خودش باشد.");
            if(!dependentIds.TryGetValue(parentId,out var set))
                dependentIds[parentId]=set=new HashSet<Guid>();
            set.Add(childId);
        }
        // In the absence of actual machine execution intervals, every remaining
        // operation of an active work order conservatively reserves its center
        // from its planned start through the end of the simulated horizon.
        var reservedDays=new HashSet<(Guid WorkCenterId,DateOnly Date)>();
        var clocks=clockSessions??[];
        var measured=FactoryOperationClock.BusyIntervals(clocks,
            end.ToDateTime(TimeOnly.MinValue));
        var activeIds=(activeOrders??[]).Where(x=>x.Status is
            ManufacturingOrderStatus.Released or ManufacturingOrderStatus.InProgress
            or ManufacturingOrderStatus.OnHold).Select(x=>x.Id).ToHashSet();
        var booked=(reservations??[]).Where(x=>activeIds.Contains(x.OrderId) &&
            activeOrders!.Any(order=>order.Id==x.OrderId&&
                order.Operations.Any(op=>op.Id==x.OperationId&&!op.IsComplete &&
                    op.WorkCenterId==x.WorkCenterId))).ToArray();
        var occupied=measured.Concat(FactoryCapacityReservations.Occupied(booked)).ToArray();
        foreach(var order in activeOrders??[])
        {
            if(order.Status is not (ManufacturingOrderStatus.Released or
                ManufacturingOrderStatus.InProgress or ManufacturingOrderStatus.OnHold))
                continue;
            if(order.PlannedStart>order.DueOn || order.Operations.Count==0)
                throw new InvalidOperationException("دستور فعال بدون مراحل یا بازه برنامه معتبر است؛ ظرفیت قابل اعتماد نیست.");
            var blockStart=order.PlannedStart>baseline?order.PlannedStart:baseline;
            foreach(var operation in order.Operations.Where(x=>!x.IsComplete))
            {
                if(operation.WorkCenterId==Guid.Empty)
                    throw new InvalidOperationException("دستور فعال دارای مرکز کاری نامعتبر است.");
                // A running clock supplies a measured start. Its as-yet unknown stop
                // is conservatively treated as horizon end. Missing clock: full-day guard.
                if(clocks.Any(x=>x.OrderId==order.Id&&x.OperationId==operation.Id
                    &&x.WorkCenterId==operation.WorkCenterId&&x.StoppedUtc is null) ||
                   booked.Any(x=>x.OrderId==order.Id&&x.OperationId==operation.Id))
                    // Planned slot is explicit; do not double-block its whole day.
                    // The slot is not proof the operation will actually finish on time.
                    continue;
                for(var date=blockStart;date<end;date=date.AddDays(1))
                    reservedDays.Add((operation.WorkCenterId,date));
            }
        }
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
                        // Match CRP/APS: an upstream operation processes enough
                        // units to cover this operation and all downstream yield losses.
                        var quantities=new decimal[ops.Length];
                        var durations=new decimal[ops.Length];
                        for(var i=0;i<ops.Length;i++)
                        {
                            decimal cumulativeYield=1m;
                            for(var j=i;j<ops.Length;j++)
                                cumulativeYield*=ops[j].YieldPercent/100m;
                            if(cumulativeYield<=0m)
                                throw new InvalidOperationException("بازده تجمعی عملیات بچ نامعتبر است.");
                            quantities[i]=lot.Quantity/cumulativeYield;
                            durations[i]=ops[i].SetupMinutes+quantities[i]*ops[i].RunMinutesPerUnit;
                        }
                        if(durations.Any(x=>x<=0||x>20000) ||
                            quantities.Any(x=>x<=0||x>100_000_000m))
                            problem="زمان یا مقدار عبوری یکی از عملیات بچ خارج از محدوده زمان‌بندی است.";
                        else
                        {
                            var taskId=TaskId(lot.LotKey);
                            if(earliest.ContainsKey(taskId))
                                throw new InvalidOperationException("شناسه محاسباتی دو بچ متفاوت یکسان شده است.");
                            earliest.Add(taskId,lot.ReleaseOn);
                            for(var i=0;i<ops.Length;i++)
                                tasks.Add(new ApsTask(taskId,lot.ItemId,lot.ReceiptOn,quantities[i],
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
        var prereqs=dependentIds.ToDictionary(x=>x.Key,
            x=>(IReadOnlyCollection<Guid>)x.Value.ToArray());
        var planned=FiniteFactoryScheduler.Schedule(tasks,shifts,baseline,horizonDays,
            capacityMultiplier,unavailableDays:reservedDays,
            earliestStartDates:earliest,prerequisites:prereqs,
            occupiedIntervals:occupied);
        var finished=initial.Select(lot=>{
            if(lot.Status=="Blocked")return lot;
            var allocations=planned.Allocations.Where(x=>x.DemandLineId==TaskId(lot.LotKey)).ToArray();
            if(allocations.Any(x=>x.StartDate<lot.ProposedRelease))
                throw new InvalidOperationException("زمان‌بند عملیاتی را پیش از تاریخ آزادسازی بچ قرار داده است.");
            var done=allocations.Length==lot.OperationsRequired;
            var late=allocations.Any(x=>x.Late);
            var status=!done?"Unscheduled":late?"Late":"OnTime";
            return lot with{OperationsScheduled=allocations.Length,Status=status,
                Late=late,Reason=done?null:planned.Unscheduled
                    .FirstOrDefault(x=>x.DemandLineId==TaskId(lot.LotKey))?.Reason
                    ??"یک یا چند عملیات به دلیل محدودیت شیفت یا ظرفیت در افق انتخابی قابل تخصیص نیستند."};
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
            var productCapacityRates=await WorkCenterProductCapacityRules.LoadActiveRates(db,ct);
            WorkCenterProductCapacityRules.ApplyToRoutes(routings,productCapacityRates);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            var auditClocks=await db.ManufacturingOrderAudits.AsNoTracking()
                .Where(x=>x.Action==FactoryOperationClock.Started ||
                    x.Action==FactoryOperationClock.Stopped ||
                    x.Action==FactoryCapacityReservations.Reserved ||
                    x.Action==FactoryCapacityReservations.Cancelled).ToListAsync(ct);
            var existing=await db.ManufacturingOrders.AsNoTracking()
                .Include(x=>x.Operations)
                .Where(x=>x.Status==ManufacturingOrderStatus.Released ||
                    x.Status==ManufacturingOrderStatus.InProgress ||
                    x.Status==ManufacturingOrderStatus.OnHold).ToListAsync(ct);
            try
            {
                var mrp=PlanningCalculators.RunMrp(mps.Lines,boms,balances,receipts,types);
                var graph=NetMrpDemandPegging.Build(mps.Lines,boms,balances,receipts,types,mrp);
                var sessions=FactoryOperationClock.Read(auditClocks);
                var reservations=FactoryCapacityReservations.Read(auditClocks);
                var report=Evaluate(graph,routings,shifts,start,days,factor,
                    existing,sessions,reservations);
                return Results.Ok(new{
                    kind="UnconfirmedNetMrpBatchCapacityPreview",algorithm="MRP-CRP-PREVIEW-4",
                    baselineJalali=PersianPlanningDates.Format(start),horizonDays=days,
                    capacityMultiplier=factor,report.OnTimeLots,report.LateLots,report.BlockedLots,
                    disclaimer="پیش‌نمایش ظرفیت: رزروهای قطعی داخلی بازه‌های زمانی مرکز کاری، زمان‌های واقعی ثبت‌شده و ساعت‌های باز لحاظ می‌شوند. برای عملیات بدون رزرو و ساعت باز، ظرفیت به‌طور محافظه‌کارانه بسته است. رزرو به معنای تکمیل واقعی یا موجودی قطعی نیست؛ نتیجه مجوز آزادسازی نیست.",
                    lots=report.Lots.Select(x=>new{x.LotKey,x.ItemId,x.Quantity,
                        proposedReleaseJalali=PersianPlanningDates.Format(x.ProposedRelease),
                        receiptDueJalali=PersianPlanningDates.Format(x.ReceiptDue),
                        x.RoutingRevisionId,x.OperationsRequired,x.OperationsScheduled,
                        x.Status,x.Reason,x.Late}),
                    allocations=report.Allocations.Select(x=>new{x.ItemId,x.WorkCenterId,
                        x.Sequence,x.Quantity,x.OperationName,x.DurationMinutes,x.Late,
                        lotKey=report.Lots.Single(l=>TaskId(l.LotKey)==x.DemandLineId).LotKey,
                        startJalali=PersianPlanningDates.Format(x.StartDate),
                        startTime=x.StartsAt.ToString("HH:mm"),
                        finishJalali=PersianPlanningDates.Format(x.FinishDate),
                        finishTime=x.FinishesAt.ToString("HH:mm")}),
                    report.Unscheduled,
                    activeCapacityReservations=reservations.Count(x=>existing.Any(o=>
                        o.Id==x.OrderId&&o.Operations.Any(op=>op.Id==x.OperationId&&!op.IsComplete))),
                    recordedOperationClockSessions=sessions.Count,
                    runningOperationClockSessions=sessions.Count(x=>x.StoppedUtc is null),
                    conservativelyBlockedActiveOrders=existing.Count(x=>
                        x.Operations.Any(op=>!op.IsComplete))
                });
            }
            catch(Exception ex)when(ex is InvalidOperationException or OverflowException or
                ArgumentOutOfRangeException or ArgumentException)
            {return Results.UnprocessableEntity(new{message=ex.Message});}
        });
        return routes;
    }
}
