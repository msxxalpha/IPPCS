using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Stage 14: independent IPPCS manufacturing governance. No integration with
// warehouse, purchasing, sales, HR, stock posting or production dispatch PLC.
public record FactoryDraftReplanInput(decimal PlannedQuantity,string PlannedStartJalali,
    string DueOnJalali,string Reason);
public record FactoryRevisionIssue(string Code,string Description);
public record FactoryRevisionCheck(Guid RevisionId,Guid ItemId,string Type,
    string FromJalali,string? ThroughJalali,bool CanApprove,
    IReadOnlyList<FactoryRevisionIssue> Issues);
public record FactoryOrderReleaseCheck(Guid OrderId,string Number,bool CanRelease,
    Guid RouteId,Guid BomId,int Operations,int PeggedLines,decimal PeggedQuantity,
    IReadOnlyList<FactoryRevisionIssue> Issues);
public record FactoryDispatchRow(Guid OrderId,string OrderNumber,Guid ItemId,
    ManufacturingOrderStatus Status,DateOnly DueOn,Guid OperationId,int Sequence,
    string OperationName,Guid WorkCenterId,bool CanStartByRouteAndQuality,
    bool HasOpenQualityHold,bool HasBookedMachineSlot,bool HasRunningClock,
    decimal WorkInProgress,decimal QualityPending,decimal ReportedGood,string Guidance);
public record FactoryDispatchBoard(string AsOfJalali,int HorizonDays,
    int OrdersConsidered,int RunnableOperations,int MissingReservationCount,
    IReadOnlyList<FactoryDispatchRow> Rows,string Disclaimer);

public static class FactoryRevisionGovernance
{
    public static bool Overlaps(DateOnly from,DateOnly? through,DateOnly nextFrom,DateOnly? nextThrough)
        => from<=(nextThrough??DateOnly.MaxValue)&&nextFrom<=(through??DateOnly.MaxValue);

    public static IReadOnlyList<FactoryRevisionIssue> RoutingIssues(
        RoutingRevision route,IReadOnlyList<RoutingRevision> approved)
    {
        var issues=new List<FactoryRevisionIssue>();
        if(route.State!=RoutingState.Draft)issues.Add(new("NotDraft","فقط نسخه پیش‌نویس قابل تصویب است."));
        if(route.EffectiveTo<route.EffectiveFrom)issues.Add(new("InvalidInterval","بازه اعتبار مسیر ساخت معتبر نیست."));
        if(route.Operations.Count==0)issues.Add(new("EmptyRoute","حداقل یک عملیات ساخت لازم است."));
        if(route.Operations.Any(x=>x.Sequence<=0||string.IsNullOrWhiteSpace(x.Name)||
            x.WorkCenterId==Guid.Empty||x.SetupMinutes<0||x.RunMinutesPerUnit<=0||
            x.YieldPercent<=0||x.YieldPercent>100))
            issues.Add(new("BadOperation","ترتیب، مرکز، زمان یا بازده عملیات نامعتبر است."));
        if(route.Operations.GroupBy(x=>x.Sequence).Any(x=>x.Count()>1))
            issues.Add(new("DuplicateSequence","شماره عملیات مسیر ساخت تکراری است."));
        if(approved.Any(x=>x.Id!=route.Id&&x.ItemId==route.ItemId&&
            x.State==RoutingState.Approved&&Overlaps(route.EffectiveFrom,route.EffectiveTo,
                x.EffectiveFrom,x.EffectiveTo)))
            issues.Add(new("ApprovedOverlap","بازه اعتبار با مسیر مصوب موجود همپوشانی دارد."));
        return issues;
    }

    // A cycle is invalid only when every edge in that cycle can be effective on
    // at least one common date. A union graph across disjoint revision intervals
    // creates false positives, e.g. A->B in January and B->A in July.
    public static bool HasEffectiveBomCycle(IEnumerable<ManufacturingBomRevision> revisions)
    {
        var all=revisions.ToArray();
        foreach(var date in all.Select(x=>x.ValidFrom).Distinct().OrderBy(x=>x))
        {
            var edges=all.Where(x=>x.ValidFrom<=date&&(x.ValidUntil is null||date<=x.ValidUntil.Value))
                .SelectMany(x=>x.Lines.Select(line=>(Parent:x.ParentItemId,Child:line.ComponentItemId)))
                .ToLookup(x=>x.Parent,x=>x.Child);
            var visiting=new HashSet<Guid>();
            var visited=new HashSet<Guid>();
            bool Visit(Guid item)
            {
                if(visiting.Contains(item))return true;
                if(visited.Contains(item))return false;
                visiting.Add(item);
                foreach(var child in edges[item])
                    if(Visit(child))return true;
                visiting.Remove(item);
                visited.Add(item);
                return false;
            }
            if(edges.Select(x=>x.Key).Any(Visit))return true;
        }
        return false;
    }

    public static IReadOnlyList<FactoryRevisionIssue> BomIssues(
        ManufacturingBomRevision revision,IReadOnlyList<ManufacturingBomRevision> approved)
    {
        var issues=new List<FactoryRevisionIssue>();
        if(revision.State!=ManufacturingRevisionState.Draft)
            issues.Add(new("NotDraft","فقط نسخه پیش‌نویس BOM قابل تصویب است."));
        if(revision.ValidUntil<revision.ValidFrom)
            issues.Add(new("InvalidInterval","بازه اعتبار BOM معتبر نیست."));
        if(revision.Lines.Count==0)issues.Add(new("EmptyBom","BOM باید حداقل یک جزء داشته باشد."));
        if(revision.Lines.Any(x=>x.ComponentItemId==revision.ParentItemId||
            x.ComponentItemId==Guid.Empty||x.Sequence<=0||x.QuantityPer<=0||
            x.ScrapPercent<0||x.ScrapPercent>=100||x.AlternativePriority<=0||
            (string.IsNullOrWhiteSpace(x.AlternativeGroupCode)&&x.IsDefaultAlternative)))
            issues.Add(new("InvalidComponent","جزء، ضریب مصرف یا ضایعات BOM نامعتبر است."));
        if(revision.Lines.GroupBy(x=>x.ComponentItemId).Any(x=>x.Count()>1)||
            revision.Lines.GroupBy(x=>x.Sequence).Any(group=>group.Count()>1&&
                (group.Any(x=>string.IsNullOrWhiteSpace(x.AlternativeGroupCode))||
                 group.Select(x=>x.AlternativeGroupCode).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1)))
            issues.Add(new("DuplicateComponent","جزء تکراری یا ترتیب مشترک بین گروه‌های متفاوت BOM وجود دارد."));
        foreach(var group in revision.Lines.Where(x=>!string.IsNullOrWhiteSpace(x.AlternativeGroupCode))
            .GroupBy(x=>x.AlternativeGroupCode!.Trim(),StringComparer.OrdinalIgnoreCase))
        {
            if(group.Count()<2)
                issues.Add(new("IncompleteAlternativeGroup",$"گروه آلترناتیو {group.Key} باید حداقل دو قطعه داشته باشد."));
            if(group.Count(x=>x.IsDefaultAlternative)!=1)
                issues.Add(new("AlternativeDefault",$"گروه آلترناتیو {group.Key} باید دقیقاً یک قطعه پیش‌فرض داشته باشد."));
            if(group.Any(x=>x.Sequence!=group.First().Sequence))
                issues.Add(new("AlternativeSequence",$"ترتیب اعضای گروه آلترناتیو {group.Key} باید یکسان باشد."));
            if(group.GroupBy(x=>x.AlternativePriority).Any(x=>x.Count()>1))
                issues.Add(new("AlternativePriority",$"اولویت قطعات در گروه آلترناتیو {group.Key} باید یکتا باشد."));
        }
        if(approved.Any(x=>x.Id!=revision.Id&&x.ParentItemId==revision.ParentItemId&&
            x.State==ManufacturingRevisionState.Approved&&Overlaps(
                revision.ValidFrom,revision.ValidUntil,x.ValidFrom,x.ValidUntil)))
            issues.Add(new("ApprovedOverlap","نسخه BOM با نسخه مصوب دیگری همپوشانی دارد."));
        if(HasEffectiveBomCycle(approved.Where(x=>x.Id!=revision.Id).Append(revision)))
            issues.Add(new("BomCycle","چرخه وابستگی بین نسخه‌های BOM در یک بازه تاریخی مشترک شناسایی شد."));
        return issues;
    }

    public static IReadOnlyList<FactoryRevisionIssue> ReleaseIssues(
        ManufacturingOrder order,RoutingRevision? route,ManufacturingBomRevision? bom,
        IReadOnlyList<ManufacturingOrderPeg> links,IReadOnlyList<MpsDemandLine> planLines)
    {
        var issues=new List<FactoryRevisionIssue>();
        if(order.Status!=ManufacturingOrderStatus.Draft)
            issues.Add(new("NotDraft","دستور باید پیش‌نویس باشد."));
        if(order.PlannedQuantity<=0||order.DueOn<order.PlannedStart)
            issues.Add(new("InvalidDates","تاریخ شروع، موعد یا مقدار دستور معتبر نیست."));
        if(route is null||route.ItemId!=order.ItemId||route.State!=RoutingState.Approved||
            route.EffectiveFrom>order.PlannedStart||route.EffectiveTo<order.PlannedStart||
            route.Operations.Count==0)
            issues.Add(new("RouteUnavailable","مسیر مصوب معتبر در تاریخ شروع دستور وجود ندارد."));
        if(bom is null||bom.ParentItemId!=order.ItemId||
            bom.State!=ManufacturingRevisionState.Approved||
            bom.ValidFrom>order.PlannedStart||bom.ValidUntil<order.PlannedStart||
            bom.Lines.Count==0)
            issues.Add(new("BomUnavailable","BOM مصوب معتبر در تاریخ شروع دستور وجود ندارد."));
        if(links.Sum(x=>x.LinkedQuantity)>order.PlannedQuantity)
            issues.Add(new("PegOverAllocation","تخصیص MPS از مقدار دستور بیشتر است."));
        var lookup=planLines.ToDictionary(x=>x.Id);
        foreach(var link in links)
            if(!lookup.TryGetValue(link.MpsLineId,out var line)||line.ItemId!=order.ItemId||
                line.NeededOn<order.DueOn||link.LinkedQuantity<=0)
                issues.Add(new("PegMismatch","تاریخ، کالا یا مقدار ارتباط با ردیف MPS معتبر نیست."));
        if(order.Operations.Count>0)
            issues.Add(new("AlreadyMaterialized","عملیات مسیر قبلاً برای این دستور ساخته شده است."));
        return issues;
    }

    internal static async Task LockRevisionItem(ProductionDb db,string category,Guid item,CancellationToken ct)
    {
        // BOM cycles can involve two different parent items. Serialize the entire
        // approved BOM graph, not merely revisions belonging to one item.
        var resource=category=="BOM"?"IPPCS:RevisionApproval:BOM:Graph":
            "IPPCS:RevisionApproval:"+category+":"+item.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DECLARE @res int;
               EXEC @res = sys.sp_getapplock @Resource={resource},
                    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
               IF @res < 0 THROW 51031, 'Revision approval lock unavailable', 1;",ct);
    }
    public static FactoryDispatchBoard DispatchBoard(DateOnly date,int horizon,
        IReadOnlyList<ManufacturingOrder> orders,
        IReadOnlyList<ManufacturingQualityHold> holds,
        IReadOnlyList<FactoryCapacityReservation> reservations,
        IReadOnlyList<FactoryClockSession> clocks)
    {
        if(horizon is <1 or >30)throw new ArgumentOutOfRangeException(nameof(horizon));
        var until=date.AddDays(horizon);
        var rows=new List<FactoryDispatchRow>();
        var active=orders.Where(x=>x.Status is ManufacturingOrderStatus.Released or
            ManufacturingOrderStatus.InProgress or ManufacturingOrderStatus.OnHold);
        foreach(var order in active.Where(x=>x.PlannedStart<until&&x.DueOn>=date))
        {
            if(order.Operations.Count==0)continue;
            var quality=holds.Any(x=>x.OrderId==order.Id&&x.Status==ManufacturingQualityStatus.Open);
            var gates=ManufacturingCrossOrderTrace.Readiness(order);
            foreach(var op in order.Operations.OrderBy(x=>x.Sequence))
            {
                if(op.IsComplete)continue;
                var gate=gates.Single(x=>x.OperationId==op.Id);
                var slot=reservations.Any(x=>x.OrderId==order.Id&&x.OperationId==op.Id&&
                    DateOnly.FromDateTime(x.StartLocal)>=date&&
                    DateOnly.FromDateTime(x.StartLocal)<until);
                var clock=clocks.Any(x=>x.OrderId==order.Id&&x.OperationId==op.Id&&
                    x.StoppedUtc is null);
                // A stopped manufacturing order is never dispatch-ready, even when
                // route, quality quantities and future machine bookings look valid.
                var ready=gate.CanProceed&&!quality&&order.Status!=ManufacturingOrderStatus.OnHold;
                rows.Add(new(order.Id,order.Number,order.ItemId,order.Status,order.DueOn,
                    op.Id,op.Sequence,op.Name,op.WorkCenterId,ready,quality,slot,clock,
                    gate.ForwardAvailable,gate.PendingQuality,gate.Good,
                    quality?"قرنطینه تعیین‌تکلیف‌نشده مانع ادامه عملیات است.":
                    order.Status==ManufacturingOrderStatus.OnHold?"دستور در وضعیت تعلیق است.":
                    !gate.CanProceed?"مرحله قبلی یا کنترل کیفی اجازه شروع عملیات را نمی‌دهد.":
                    !slot?"مرحله قابل شروع است، ولی رزرو ظرفیت برای این افق ثبت نشده است.":
                    "گیت مسیر و کیفیت پاس شده است و برای عملیات باز رزرو زمانی وجود دارد."));
            }
        }
        return new(PersianPlanningDates.Format(date),horizon,
            rows.Select(x=>x.OrderId).Distinct().Count(),
            rows.Count(x=>x.CanStartByRouteAndQuality),
            rows.Count(x=>x.CanStartByRouteAndQuality&&!x.HasBookedMachineSlot),
            rows.OrderBy(x=>x.DueOn).ThenBy(x=>x.OrderNumber).ThenBy(x=>x.Sequence).ToArray(),
            "تابلوی آمادگی تنها بازتاب عملیات و کیفیت ثبت‌شده در IPPCS است؛ رزرو زمانی لزوماً برای همین لحظه معتبر نیست و به معنی مجوز عملیاتی، آماده بودن ابزار/مواد یا آزادبودن موجودی انبار نیست.");
    }

    public static IEndpointRouteBuilder MapFactoryRevisionGovernance(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-revisions/routings/{id:guid}/approval-check",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
                var route=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(route is null)return Results.NotFound();
                var approved=await db.RoutingRevisions.AsNoTracking()
                    .Where(x=>x.ItemId==route.ItemId&&x.State==RoutingState.Approved)
                    .ToListAsync(ct);
                var issues=RoutingIssues(route,approved);
                return Results.Ok(new FactoryRevisionCheck(id,route.ItemId,"Routing",
                    PersianPlanningDates.Format(route.EffectiveFrom),
                    route.EffectiveTo.HasValue?PersianPlanningDates.Format(route.EffectiveTo.Value):null,
                    issues.Count==0,issues));
            });
        api.MapGet("/factory-revisions/bom/{id:guid}/approval-check",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
                var bom=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(bom is null)return Results.NotFound();
                var approved=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                    .Where(x=>x.State==ManufacturingRevisionState.Approved)
                    .ToListAsync(ct);
                var issues=BomIssues(bom,approved);
                return Results.Ok(new FactoryRevisionCheck(id,bom.ParentItemId,"BOM",
                    PersianPlanningDates.Format(bom.ValidFrom),
                    bom.ValidUntil.HasValue?PersianPlanningDates.Format(bom.ValidUntil.Value):null,
                    issues.Count==0,issues));
            });
        api.MapGet("/manufacturing-orders/{id:guid}/release-check",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
                var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(order is null)return Results.NotFound();
                var route=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                    .SingleOrDefaultAsync(x=>x.Id==order.RoutingRevisionId,ct);
                var bom=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                    .SingleOrDefaultAsync(x=>x.Id==order.BomRevisionId,ct);
                var links=await db.ManufacturingOrderPegs.AsNoTracking()
                    .Where(x=>x.OrderId==id).ToListAsync(ct);
                var ids=links.Select(x=>x.MpsLineId).ToArray();
                var lines=await db.MpsDemandLines.AsNoTracking()
                    .Where(x=>ids.Contains(x.Id)).ToListAsync(ct);
                var issues=ReleaseIssues(order,route,bom,links,lines).ToList();
                var controlPlans=await db.ManufacturingControlPlans.AsNoTracking()
                    .Include(x=>x.Characteristics)
                    .Where(x=>x.RoutingRevisionId==order.RoutingRevisionId&&x.State==ControlPlanState.Approved
                        &&x.ValidFrom<=order.PlannedStart
                        &&(x.ValidUntil==null||order.PlannedStart<=x.ValidUntil)).ToListAsync(ct);
                if(!ManufacturingInspectionRules.ExactlyOneApplicableControlPlan(controlPlans.Count))
                    issues.Add(new(controlPlans.Count==0?"ControlPlanUnavailable":"ControlPlanAmbiguous",
                        controlPlans.Count==0
                            ?"برای تاریخ شروع دستور، دقیقاً یک کنترل پلن مصوب و معتبر لازم است."
                            :"بیش از یک کنترل پلن مصوب برای تاریخ شروع دستور معتبر است."));
                else if(route is not null&&
                    !ManufacturingInspectionRules.HasMandatoryFinalCharacteristic(controlPlans[0].Characteristics,route))
                    issues.Add(new("FinalQualityCheckMissing",
                        "کنترل پلن باید حداقل یک مشخصه اجباری برای آخرین عملیات مسیر داشته باشد."));
                return Results.Ok(new FactoryOrderReleaseCheck(id,order.Number,issues.Count==0,
                    order.RoutingRevisionId,order.BomRevisionId,route?.Operations.Count??0,
                    links.Count,links.Sum(x=>x.LinkedQuantity),issues));
            });
        api.MapPost("/manufacturing-orders/{id:guid}/replan-draft",
            async(Guid id,FactoryDraftReplanInput input,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
                if(!PersianPlanningDates.TryParse(input.PlannedStartJalali,out var start)||
                    !PersianPlanningDates.TryParse(input.DueOnJalali,out var due)||
                    due<start||input.PlannedQuantity is <=0 or >1_000_000_000m||
                    string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Trim().Length>150)
                    return Results.BadRequest(new{message="مقدار، تاریخ شمسی یا علت اصلاح معتبر نیست."});
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                try{await FactoryCapacityReservations.LockOrder(db,id,ct);}
                catch(SqlException){return Results.Conflict(new{message="قفل دستور تولید در دسترس نیست."});}
                var order=await db.ManufacturingOrders.Include(x=>x.Operations)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(order is null)return Results.NotFound();
                if(order.Status!=ManufacturingOrderStatus.Draft||order.Operations.Count>0)
                    return Results.Conflict(new{message="اصلاح برنامه فقط برای دستور پیش‌نویس بدون عملیات آزادشده مجاز است."});
                var pegs=await db.ManufacturingOrderPegs.AsNoTracking()
                    .Where(x=>x.OrderId==id).ToListAsync(ct);
                var pegIds=pegs.Select(x=>x.MpsLineId).ToArray();
                var lines=await db.MpsDemandLines.AsNoTracking().Where(x=>pegIds.Contains(x.Id))
                    .ToListAsync(ct);
                if(pegs.Sum(x=>x.LinkedQuantity)>input.PlannedQuantity||
                    pegs.Join(lines,p=>p.MpsLineId,l=>l.Id,(p,l)=>l).Any(x=>x.NeededOn<due))
                    return Results.Conflict(new{message="مقدار یا موعد پیشنهادی با انتساب مصوب MPS سازگار نیست."});
                var routes=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                    .Where(x=>x.ItemId==order.ItemId&&x.State==RoutingState.Approved&&
                        x.EffectiveFrom<=start&&(x.EffectiveTo==null||x.EffectiveTo>=start))
                    .ToListAsync(ct);
                var boms=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                    .Where(x=>x.ParentItemId==order.ItemId&&x.State==ManufacturingRevisionState.Approved&&
                        x.ValidFrom<=start&&(x.ValidUntil==null||x.ValidUntil>=start))
                    .ToListAsync(ct);
                if(routes.Count!=1||routes[0].Operations.Count==0||boms.Count!=1||
                    boms[0].Lines.Count==0)
                    return Results.Conflict(new{message="برای تاریخ اصلاح‌شده BOM و مسیر مصوب یکتا و دارای عملیات/اجزا لازم است."});
                var before=$"Qty={order.PlannedQuantity};Start={PersianPlanningDates.Format(order.PlannedStart)};Due={PersianPlanningDates.Format(order.DueOn)};Route={order.RoutingRevisionId:N};Bom={order.BomRevisionId:N}";
                order.PlannedQuantity=input.PlannedQuantity;order.PlannedStart=start;order.DueOn=due;
                order.RoutingRevisionId=routes[0].Id;order.BomRevisionId=boms[0].Id;
                order.UpdatedUtc=DateTime.UtcNow;
                var after=$"Qty={order.PlannedQuantity};Start={PersianPlanningDates.Format(start)};Due={PersianPlanningDates.Format(due)};Route={order.RoutingRevisionId:N};Bom={order.BomRevisionId:N}";
                var actor=(ctx.User.Identity?.Name??"unknown").Replace(";","_").Replace("|","_");
                if(actor.Length>40)actor=actor[..40];
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                    OrderId=id,Action="DraftReplanned",
                    Details="Before:"+before+";After:"+after+";By="+actor+";Reason="+input.Reason.Trim()
                });
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
                return Results.Ok(new{order.Id,order.Number,order.PlannedQuantity,
                    plannedStartJalali=PersianPlanningDates.Format(start),
                    dueOnJalali=PersianPlanningDates.Format(due),
                    order.RoutingRevisionId,order.BomRevisionId});
            }).RequireAuthorization("IpPlanningWrite");
        api.MapGet("/factory-dispatch/board",
            async(string asOfJalali,int? horizonDays,ProductionDb db,CancellationToken ct)=>{
                if(!PersianPlanningDates.TryParse(asOfJalali,out var date)||
                    (horizonDays??7) is <1 or >30)
                    return Results.BadRequest(new{message="تاریخ شمسی و افق ۱ تا ۳۰ روز معتبر لازم است."});
                var horizon=horizonDays??7;var until=date.AddDays(horizon);
                var orders=await db.ManufacturingOrders.AsNoTracking()
                    .Include(x=>x.Operations).ThenInclude(x=>x.InspectionRequirements)
                    .Where(x=>x.PlannedStart<until&&x.DueOn>=date&&
                        (x.Status==ManufacturingOrderStatus.Released||
                        x.Status==ManufacturingOrderStatus.InProgress||
                        x.Status==ManufacturingOrderStatus.OnHold)).Take(501).ToListAsync(ct);
                if(orders.Count>500)return Results.Conflict(new{message="تعداد دستورها از سقف گزارش بیشتر است؛ گزارش ناقص ارائه نمی‌شود."});
                var orderIds=orders.Select(x=>x.Id).ToArray();
                var holds=await db.ManufacturingQualityHolds.AsNoTracking()
                    .Where(x=>orderIds.Contains(x.OrderId)&&x.Status==ManufacturingQualityStatus.Open)
                    .ToListAsync(ct);
                var events=await db.ManufacturingOrderAudits.AsNoTracking()
                    .Where(x=>x.Action==FactoryCapacityReservations.Reserved||
                        x.Action==FactoryCapacityReservations.Cancelled||
                        x.Action==FactoryOperationClock.Started||
                        x.Action==FactoryOperationClock.Stopped).Take(10001).ToListAsync(ct);
                if(events.Count>10000)return Results.Conflict(new{message="حجم تاریخچه ظرفیت بیش از سقف گزارش است."});
                try{
                    return Results.Ok(DispatchBoard(date,horizon,orders,holds,
                        FactoryCapacityReservations.Read(events.Where(x=>
                            x.Action==FactoryCapacityReservations.Reserved||
                            x.Action==FactoryCapacityReservations.Cancelled)),
                        FactoryOperationClock.Read(events)));
                }catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            });
        return routes;
    }
}
