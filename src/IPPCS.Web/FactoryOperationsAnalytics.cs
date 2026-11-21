using System.Data;
using Microsoft.EntityFrameworkCore;

// Internal production-only consumption assumptions: this is NOT warehouse stock or inventory movement.
public sealed class FactoryMaterialUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ComponentItemId { get; set; }
    public decimal QuantityConsumed { get; set; }
    public string Reference { get; set; } = "";
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
    public string RecordedBy { get; set; } = "";
}
public sealed class FactoryCenterCostRate
{
    public Guid WorkCenterId { get; set; }
    public decimal ConversionRialsPerMinute { get; set; }
    public decimal ScrapRialsPerUnit { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string Note { get; set; } = "";
}
public record MaterialConsumptionInput(Guid BomLineId,decimal Quantity,string Reference,Guid? ComponentItemId=null);
public record CenterCostInput(Guid WorkCenterId,decimal ConversionRialsPerMinute,
    decimal ScrapRialsPerUnit,string EffectiveFromJalali,string? Note);
public record FactoryScenarioInput(Guid MpsVersionId,string StartJalali,int HorizonDays,
    decimal BaselineCapacity,decimal ScenarioCapacity);
public record FactoryMaterialVariance(Guid ComponentItemId,decimal PlannedRequired,
    decimal RecordedConsumed,decimal Variance);
public record FactoryAdherenceRow(Guid ItemId,decimal MpsRequired,decimal CompletedGood,
    decimal Variance,decimal? FulfilmentPercent);

public static class FactoryOperationalMetrics
{
    public static List<FactoryMaterialVariance> MaterialVariances(decimal plannedQuantity,
        IReadOnlyList<ManufacturingBomLine> lines,IEnumerable<FactoryMaterialUsage> usage,
        IReadOnlyList<ManufacturingOrderMaterialAllocation> allocations)
    {
        var consumed=usage.GroupBy(x=>x.ComponentItemId)
            .ToDictionary(x=>x.Key,x=>x.Sum(y=>y.QuantityConsumed));
        var requirements=lines.GroupBy(x=>x.ComponentItemId).ToDictionary(x=>x.Key,
            x=>x.Sum(line=>BomAlternativeExecution.CalculateSelectedComponentQuantity(
                plannedQuantity,line,lines,allocations)));
        return requirements.Keys.Union(consumed.Keys).OrderBy(x=>x).Select(id=>{
            var required=requirements.GetValueOrDefault(id);
            var actual=consumed.GetValueOrDefault(id);
            return new FactoryMaterialVariance(id,required,actual,actual-required);
        }).ToList();
    }
    public static List<FactoryAdherenceRow> Adherence(
        IEnumerable<MpsDemandLine> lines,IEnumerable<ManufacturingOrder> orders,
        DateOnly from,DateOnly to)
    {
        var goals=lines.Where(x=>x.NeededOn>=from && x.NeededOn<=to)
            .GroupBy(x=>x.ItemId).ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Quantity));
        var completed=orders.Where(x=>x.Status==ManufacturingOrderStatus.Completed
            && x.DueOn>=from && x.DueOn<=to).GroupBy(x=>x.ItemId)
            .ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Operations.OrderBy(o=>o.Sequence)
                .LastOrDefault()?.GoodQuantity??0m));
        return goals.Keys.Union(completed.Keys).OrderBy(x=>x)
            .Select(id=>new FactoryAdherenceRow(id,goals.GetValueOrDefault(id),
                completed.GetValueOrDefault(id),
                completed.GetValueOrDefault(id)-goals.GetValueOrDefault(id),
                goals.GetValueOrDefault(id)<=0?null:Math.Round(
                    100m*completed.GetValueOrDefault(id)/goals.GetValueOrDefault(id),2)))
            .ToList();
    }
}

public static class FactoryOperationsAnalyticsEndpoints
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<FactoryMaterialUsage>().HasOne<ManufacturingOrder>().WithMany()
            .HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FactoryMaterialUsage>().HasOne<PlanningItem>().WithMany()
            .HasForeignKey(x=>x.ComponentItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FactoryMaterialUsage>().Property(x=>x.QuantityConsumed).HasPrecision(18,4);
        model.Entity<FactoryMaterialUsage>().Property(x=>x.Reference).HasMaxLength(200);
        model.Entity<FactoryMaterialUsage>().Property(x=>x.RecordedBy).HasMaxLength(256);
        model.Entity<FactoryMaterialUsage>().HasIndex(x=>new{x.OrderId,x.ComponentItemId});
        model.Entity<FactoryCenterCostRate>().HasKey(x=>x.WorkCenterId);
        model.Entity<FactoryCenterCostRate>().HasOne<WorkCenter>().WithMany()
            .HasForeignKey(x=>x.WorkCenterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<FactoryCenterCostRate>().Property(x=>x.ConversionRialsPerMinute).HasPrecision(18,4);
        model.Entity<FactoryCenterCostRate>().Property(x=>x.ScrapRialsPerUnit).HasPrecision(18,4);
        model.Entity<FactoryCenterCostRate>().Property(x=>x.Note).HasMaxLength(400);
    }

    public static IEndpointRouteBuilder MapOperationsAnalytics(this IEndpointRouteBuilder endpoints)
    {
        var api=endpoints.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-material/{orderId:guid}",async(Guid orderId,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==orderId,ct);
            if(order is null)return Results.NotFound();
            var bom=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==order.BomRevisionId,ct);
            var consumed=await db.FactoryMaterialUsages.AsNoTracking()
                .Where(x=>x.OrderId==orderId).OrderBy(x=>x.RecordedUtc).ToListAsync(ct);
            var allocations=await db.Set<ManufacturingOrderMaterialAllocation>().AsNoTracking()
                .Where(x=>x.OrderId==orderId).ToListAsync(ct);
            try{
                return Results.Ok(new{orderId,order.Number,
                    rows=FactoryOperationalMetrics.MaterialVariances(order.PlannedQuantity,
                        bom?.Lines??new(),consumed,allocations),history=FactoryLineSideCorrections.Ledger(consumed)});
            }catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
        });
        api.MapPost("/factory-material/{orderId:guid}/consumption",
            async(Guid orderId,MaterialConsumptionInput input,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            if(input.Quantity<=0||input.Quantity>1000000000m||string.IsNullOrWhiteSpace(input.Reference)
                ||input.Reference.Trim().Length>160||
                FactoryLineSideCorrections.IsReservedReference(input.Reference.Trim()))
                return Results.BadRequest(new{message="مقدار و مرجع مصرف تولیدی معتبر نیست."});
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            try{await FactoryCapacityReservations.LockOrder(db,orderId,ct);}
            catch(Microsoft.Data.SqlClient.SqlException){
                return Results.Conflict(new{message="قفل مصرف دستور در دسترس نیست."});
            }
            var order=await db.ManufacturingOrders.SingleOrDefaultAsync(x=>x.Id==orderId,ct);
            if(order is null)return Results.NotFound();
            if(!ManufacturingExecutionEndpoints.IsActive(order.Status))
                return Results.Conflict(new{message="فقط هنگام اجرای دستور می‌توان مصرف پای خط ثبت کرد."});
            var bom=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==order.BomRevisionId,ct);
            if(bom is null)return Results.BadRequest(new{message="BOM منجمد دستور موجود نیست."});
            ManufacturingBomLine? component=null;
            if(input.BomLineId!=Guid.Empty)
                component=bom.Lines.SingleOrDefault(x=>x.Id==input.BomLineId);
            else if(input.ComponentItemId.HasValue&&input.ComponentItemId.Value!=Guid.Empty)
            {
                var legacyMatches=bom.Lines.Where(x=>x.ComponentItemId==input.ComponentItemId.Value).ToArray();
                if(legacyMatches.Length>1)
                    return Results.Conflict(new{message="این قطعه در چند ردیف BOM وجود دارد؛ برای جلوگیری از انتساب مبهم، شناسه ردیف BOM را ارسال کنید."});
                component=legacyMatches.SingleOrDefault();
            }
            if(component is null)
                return Results.BadRequest(new{message="ردیف BOM انتخاب‌شده در نسخه منجمد دستور وجود ندارد."});
            var allocations=await db.Set<ManufacturingOrderMaterialAllocation>().AsNoTracking()
                .Where(x=>x.OrderId==orderId).ToListAsync(ct);
            decimal plannedLimit;
            try{
                plannedLimit=BomAlternativeExecution.CalculateSelectedComponentQuantity(
                    order.PlannedQuantity,component,bom.Lines,allocations);
            }catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            var priorRecords=await db.FactoryMaterialUsages.AsNoTracking()
                .Where(x=>x.OrderId==orderId).ToListAsync(ct);
            IReadOnlyList<FactoryConsumptionLedgerRow> ledger;
            try{ledger=FactoryLineSideCorrections.Ledger(priorRecords);}
            catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            var sameItemLines=bom.Lines.Where(x=>x.ComponentItemId==component.ComponentItemId).ToArray();
            var hasUnattributedLegacy=ledger.Any(x=>x.ComponentItemId==component.ComponentItemId&&
                x.BomLineId is null&&x.NetQuantity!=0m);
            if(sameItemLines.Length>1&&hasUnattributedLegacy)
                return Results.Conflict(new{message="سوابق قدیمی این قطعه فاقد ردیف BOM هستند؛ تا تعیین تکلیف دستی این سوابق، ثبت مصرف جدید برای ردیف‌های تکراری مجاز نیست."});
            var recorded=ledger.Where(x=>x.BomLineId==component.Id||
                (x.BomLineId is null&&x.ComponentItemId==component.ComponentItemId))
                .Sum(x=>x.NetQuantity);
            if(recorded+input.Quantity>plannedLimit)
                return Results.Conflict(new{message="مصرف تجمعی ردیف BOM از مقدار مجاز ترکیب آلترناتیو دستور بیشتر است؛ از ثبت بچ و کنترل علت انحراف استفاده کنید."});
            var record=new FactoryMaterialUsage{OrderId=orderId,ComponentItemId=component.ComponentItemId,
                QuantityConsumed=input.Quantity,
                Reference=FactoryLineSideCorrections.BomLineReference(component.Id,input.Reference.Trim()),
                RecordedBy=ctx.User.Identity?.Name??"unknown"};
            db.FactoryMaterialUsages.Add(record);
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=orderId,Action="MaterialUsage",
                Details=$"BomLine={component.Id:N};Item={component.ComponentItemId:N};Qty={input.Quantity}"});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/factory-material/{orderId}",new{record.Id,
                warning="این صرفاً ثبت مصرف عملیات تولید است و موجودی هیچ انباری را تغییر نمی‌دهد."});
        }).RequireAuthorization("IpShopFloorWrite");

        api.MapGet("/factory-cost/rates",async(ProductionDb db,CancellationToken ct)=>{
            var rows=await db.FactoryCenterCostRates.AsNoTracking()
                .OrderBy(x=>x.WorkCenterId).ToListAsync(ct);
            return Results.Ok(rows.Select(x=>new{x.WorkCenterId,x.ConversionRialsPerMinute,
                x.ScrapRialsPerUnit,x.Note,effectiveFromJalali=PersianPlanningDates.Format(x.EffectiveFrom)}));
        });
        api.MapPut("/factory-cost/rates",async(CenterCostInput input,ProductionDb db,CancellationToken ct)=>{
            if(input.ConversionRialsPerMinute<0||input.ScrapRialsPerUnit<0
                ||(input.Note?.Length??0)>400
                ||!PersianPlanningDates.TryParse(input.EffectiveFromJalali,out var start))
                return Results.BadRequest(new{message="نرخ‌های فرضی یا تاریخ شمسی اعتبار نامعتبر است."});
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            try{await FactoryCostRateHistory.LockRateSchedule(db,ct);}
            catch(Microsoft.Data.SqlClient.SqlException)
                {return Results.Conflict(new{message="قفل تغییر نرخ تبدیل در دسترس نیست."});}
            if(!await db.WorkCenters.AnyAsync(x=>x.Id==input.WorkCenterId,ct))return Results.NotFound();
            var rate=await db.FactoryCenterCostRates.FindAsync([input.WorkCenterId],ct);
            if(rate is null){rate=new FactoryCenterCostRate{WorkCenterId=input.WorkCenterId};
                db.FactoryCenterCostRates.Add(rate);}
            rate.ConversionRialsPerMinute=input.ConversionRialsPerMinute;
            rate.ScrapRialsPerUnit=input.ScrapRialsPerUnit;rate.EffectiveFrom=start;
            rate.Note=input.Note?.Trim()??"";
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return Results.Ok(new{rate.WorkCenterId,
                warning="تغییر نرخ فقط بر دستورات آزادشونده جدید اثر دارد؛ نرخ‌های دستورات قبلی قبلاً منجمد شده‌اند."});
        }).RequireAuthorization("IpOeePlanning");
        api.MapGet("/factory-cost/orders/{orderId:guid}",async(Guid orderId,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .SingleOrDefaultAsync(x=>x.Id==orderId,ct);
            if(order is null)return Results.NotFound();
            var rates=await db.FactoryCenterCostRates.AsNoTracking().ToDictionaryAsync(x=>x.WorkCenterId,ct);
            var rows=order.Operations.OrderBy(x=>x.Sequence).Select(x=>{
                rates.TryGetValue(x.WorkCenterId,out var rate);
                decimal estimateTime=x.SetupMinutes+x.RunMinutesPerUnit*order.PlannedQuantity;
                decimal observedTime=x.SetupMinutes+x.RunMinutesPerUnit*
                    (x.GoodQuantity+x.ScrapQuantity+x.ReworkQuantity);
                decimal conversion=rate is null?0:observedTime*rate.ConversionRialsPerMinute;
                decimal scrap=rate is null?0:(x.ScrapQuantity+x.QualityRejectedQuantity)*
                    rate.ScrapRialsPerUnit;
                return new{x.Sequence,x.Name,x.WorkCenterId,estimateTime,observedTime,
                    hasCostRate=rate is not null,conversionRials=conversion,scrapRials=scrap};
            }).ToList();
            return Results.Ok(new{orderId,rows,
                totalConversionRials=rows.Sum(x=>x.conversionRials),
                totalScrapRials=rows.Sum(x=>x.scrapRials),
                missingRates=rows.Count(x=>!x.hasCostRate),
                disclaimer="برآورد استاندارد تبدیل/ضایعات تولید، نه حسابداری یا بهای تمام‌شده مالی."});
        });

        api.MapGet("/factory-kpis/overview",async(ProductionDb db,CancellationToken ct)=>{
            var orders=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .OrderByDescending(x=>x.CreatedUtc).Take(1000).ToListAsync(ct);
            var pending=await db.ManufacturingQualityHolds.AsNoTracking()
                .CountAsync(x=>x.Status==ManufacturingQualityStatus.Open,ct);
            var stops=await db.ManufacturingDowntimes.AsNoTracking().SumAsync(x=>(int?)x.DurationMinutes,ct)??0;
            var completed=orders.Where(x=>x.Status==ManufacturingOrderStatus.Completed).ToList();
            var finishedGood=completed.Sum(x=>x.Operations.OrderBy(o=>o.Sequence)
                .LastOrDefault()?.GoodQuantity??0m);
            return Results.Ok(new{orderCount=orders.Count,completedOrders=completed.Count,
                activeOrders=orders.Count(x=>ManufacturingExecutionEndpoints.IsActive(x.Status)),
                heldOrders=orders.Count(x=>x.Status==ManufacturingOrderStatus.OnHold),
                openQualityHolds=pending,totalLoggedDowntimeMinutes=stops,
                finalGoodQuantity=finishedGood,
                overdueOpenOrders=orders.Count(x=>x.DueOn<DateOnly.FromDateTime(DateTime.UtcNow)
                    && x.Status is not (ManufacturingOrderStatus.Cancelled or ManufacturingOrderStatus.Completed)),
                scope="Last 1000 orders; global downtime/quality totals, development preview"});
        });
        api.MapGet("/factory-kpis/adherence/{mpsId:guid}",async(Guid mpsId,string fromJalali,
            string throughJalali,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(fromJalali,out var from)
                ||!PersianPlanningDates.TryParse(throughJalali,out var through) || through<from)
                return Results.BadRequest(new{message="بازه شمسی معتبر نیست."});
            var mps=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==mpsId,ct);
            if(mps is null)return Results.NotFound();
            if(mps.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="گزارش تحقق فقط برای نسخه MPS مصوب معتبر است."});
            var scopedLines=mps.Lines.Where(x=>x.NeededOn>=from&&x.NeededOn<=through)
                .OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId).ToArray();
            var lineIds=scopedLines.Select(x=>x.Id).ToArray();
            var allMpsLineIds=mps.Lines.Select(x=>x.Id).ToArray();
            var itemIds=scopedLines.Select(x=>x.ItemId).Distinct().ToArray();
            var links=await db.ManufacturingOrderPegs.AsNoTracking()
                .Where(x=>lineIds.Contains(x.MpsLineId)).ToListAsync(ct);
            var linkedOrderIds=links.Select(x=>x.OrderId).Distinct().ToArray();
            var allMpsLinkedOrderIds=await db.ManufacturingOrderPegs.AsNoTracking()
                .Where(x=>allMpsLineIds.Contains(x.MpsLineId))
                .Select(x=>x.OrderId).Distinct().ToArrayAsync(ct);
            var linkedOrders=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>linkedOrderIds.Contains(x.Id)).ToListAsync(ct);
            var byOrder=linkedOrders.ToDictionary(x=>x.Id);
            var lineRows=scopedLines.Select(line=>{
                var assignments=links.Where(x=>x.MpsLineId==line.Id)
                    .Where(x=>byOrder.TryGetValue(x.OrderId,out var order)
                        &&order.Status!=ManufacturingOrderStatus.Cancelled).ToArray();
                var assigned=assignments.Sum(x=>x.LinkedQuantity);
                var completed=assignments.Sum(link=>{
                    var order=byOrder[link.OrderId];
                    var finalGood=order.Operations.OrderBy(x=>x.Sequence)
                        .LastOrDefault()?.GoodQuantity??0m;
                    if(order.PlannedQuantity<=0)return 0m;
                    var allocatedGood=Math.Min(order.PlannedQuantity,Math.Max(0m,finalGood))
                        *link.LinkedQuantity/order.PlannedQuantity;
                    return Math.Min(link.LinkedQuantity,allocatedGood);
                });
                return new{
                    lineId=line.Id,line.ItemId,line.Reference,line.Quantity,
                    neededOnJalali=PersianPlanningDates.Format(line.NeededOn),
                    assignedQuantity=assigned,completedGood=completed,
                    outstandingQuantity=Math.Max(0m,assigned-completed),
                    unassignedQuantity=Math.Max(0m,line.Quantity-assigned),
                    remainingDemand=Math.Max(0m,line.Quantity-completed),
                    variance=completed-line.Quantity,
                    fulfilmentPercent=line.Quantity<=0?(decimal?)null:
                        Math.Round(100m*completed/line.Quantity,2),
                    orders=assignments.Select(link=>{
                        var order=byOrder[link.OrderId];
                        var finalGood=order.Operations.OrderBy(x=>x.Sequence)
                            .LastOrDefault()?.GoodQuantity??0m;
                        var allocatedGood=order.PlannedQuantity<=0?0m:
                            Math.Min(link.LinkedQuantity,
                                Math.Min(order.PlannedQuantity,Math.Max(0m,finalGood))
                                *link.LinkedQuantity/order.PlannedQuantity);
                        return new{link.OrderId,link.LinkedQuantity,order.Number,order.Status,
                            completedGood=allocatedGood,
                            dueOnJalali=PersianPlanningDates.Format(order.DueOn)};
                    })
                };
            }).ToArray();
            var rows=lineRows.GroupBy(x=>x.ItemId).OrderBy(x=>x.Key).Select(group=>{
                var required=group.Sum(x=>x.Quantity);
                var assigned=group.Sum(x=>x.assignedQuantity);
                var completed=group.Sum(x=>x.completedGood);
                return new{
                    ItemId=group.Key,MpsRequired=required,Assigned=assigned,
                    CompletedGood=completed,Outstanding=Math.Max(0m,assigned-completed),
                    Unassigned=Math.Max(0m,required-assigned),
                    Variance=completed-required,
                    FulfilmentPercent=required<=0?(decimal?)null:
                        Math.Round(100m*completed/required,2)
                };
            }).ToArray();
            var unlinked=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>itemIds.Contains(x.ItemId)&&x.DueOn>=from&&x.DueOn<=through
                    &&!allMpsLinkedOrderIds.Contains(x.Id)).ToListAsync(ct);
            return Results.Ok(new{
                mpsId,rows,lineRows,
                unlinkedOrders=unlinked.Select(order=>new{
                    order.Id,order.Number,order.ItemId,order.PlannedQuantity,order.Status,
                    completedGood=order.Operations.OrderBy(x=>x.Sequence)
                        .LastOrDefault()?.GoodQuantity??0m,
                    dueOnJalali=PersianPlanningDates.Format(order.DueOn)
                }),
                note="تحقق هر ردیف فقط از مقدار دستورهای تخصیص‌یافته به همان ردیف MPS محاسبه می‌شود. مقدار خوب مرحله نهایی به نسبت سهم تخصیص در دستور توزیع می‌شود. دستورهای بدون انتساب در فهرست جداگانه نمایش داده می‌شوند و به‌طور خودکار به MPS نسبت داده نمی‌شوند."
            });
        });

        api.MapPost("/factory-whatif/capacity",async(FactoryScenarioInput input,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(input.StartJalali,out var start)
                ||input.HorizonDays is <1 or >90
                ||input.BaselineCapacity is <=0 or >2||input.ScenarioCapacity is <=0 or >2)
                return Results.BadRequest(new{message="مشخصات سناریو و تاریخ شمسی نامعتبر است."});
            var version=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==input.MpsVersionId,ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="فقط MPS مصوب برای سناریو پذیرفته می‌شود."});
            var boms=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                .Where(x=>x.State==ManufacturingRevisionState.Approved).ToListAsync(ct);
            var balances=await db.PlanningBalances.AsNoTracking().ToListAsync(ct);
            var receipts=await db.PlanningExpectedReceipts.AsNoTracking().ToListAsync(ct);
            var types=await db.Items.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Type,ct);
            var routings=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>x.State==RoutingState.Approved).ToListAsync(ct);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            var capacityRates=await WorkCenterProductCapacityRules.LoadActiveRates(db,ct);
            WorkCenterProductCapacityRules.ApplyToRoutes(routings,capacityRates);
            try
            {
                var net=PlanningCalculators.FactoryNetMrpCapacityPlanning.BuildNetPlan(
                    version.Lines,boms,balances,receipts,types);
                var bridge=FactoryApsMrpBridge.Build(net.Pegging,start);
                if(bridge.MaterialShortages.Count>0)
                    return Results.UnprocessableEntity(new{
                        message="سناریو به علت کمبود اجزای ساخت با پیشنهاد تأمین خارجی قابل زمان‌بندی نیست.",
                        materialWarnings=bridge.MaterialShortages
                    });
                var tasks=new List<ApsTask>();
                foreach(var line in bridge.DemandLines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId))
                {
                    var selection=routings.Where(x=>x.ItemId==line.ItemId&&x.EffectiveFrom<=line.NeededOn
                        &&(x.EffectiveTo is null||line.NeededOn<=x.EffectiveTo.Value)).ToArray();
                    if(selection.Length!=1)
                        return Results.UnprocessableEntity(new{
                            message="برای هر بچ ساخت خالص دقیقاً یک مسیر ساخت مصوب و معتبر لازم است.",
                            itemId=line.ItemId,neededOnJalali=PersianPlanningDates.Format(line.NeededOn)
                        });
                    tasks.AddRange(FactoryApsTaskExpansion.Expand(line,selection[0]));
                }
                var baseline=FiniteFactoryScheduler.Schedule(tasks,shifts,start,input.HorizonDays,
                    input.BaselineCapacity,earliestStartDates:bridge.EarliestStartDates,
                    prerequisites:bridge.Prerequisites);
                var scenario=FiniteFactoryScheduler.Schedule(tasks,shifts,start,input.HorizonDays,
                    input.ScenarioCapacity,earliestStartDates:bridge.EarliestStartDates,
                    prerequisites:bridge.Prerequisites);
                var lots=bridge.Lots.Where(x=>x.ProposalType=="Manufacturing").ToArray();
                return Results.Ok(new{
                    baseline=new{scheduled=baseline.Allocations.Count,
                        unscheduled=baseline.Unscheduled.Count,late=baseline.TotalLateOperations,
                        allocatedMinutes=baseline.Allocations.Sum(x=>x.DurationMinutes)},
                    scenario=new{scheduled=scenario.Allocations.Count,
                        unscheduled=scenario.Unscheduled.Count,late=scenario.TotalLateOperations,
                        allocatedMinutes=scenario.Allocations.Sum(x=>x.DurationMinutes)},
                    calculationBasis="NetMrpManufacturingLots",
                    plannedManufacturingLots=lots.Length,
                    netManufacturingQuantitiesByItem=lots.GroupBy(x=>x.ItemId)
                        .OrderBy(x=>x.Key).Select(x=>new{itemId=x.Key,quantity=x.Sum(y=>y.Quantity)}),
                    planningBaselineJalali=PersianPlanningDates.Format(net.BaselineDate),
                    note="سناریوی ظرفیت فقط برای بچ‌های ساخت خالص MRP اجرا شده است؛ موجودی و دریافت‌های برنامه‌ای از تقاضای ناخالص کسر شده‌اند. تاریخ آزادسازی، بازده تجمعی عملیات و تقدم نیمه‌ساخته‌ها رعایت می‌شود. این محاسبه فقط شبیه‌سازی است؛ هیچ برنامه، سفارش یا ظرفیت واقعی تغییر نکرده است."
                });
            }
            catch(Exception ex)when(ex is InvalidOperationException or ArgumentException
                or OverflowException or KeyNotFoundException)
            {
                return Results.UnprocessableEntity(new{message=ex.Message});
            }
        }).RequireAuthorization("IpPlanningWrite");
        return endpoints;
    }
}
