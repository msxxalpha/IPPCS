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
public record MaterialConsumptionInput(Guid ComponentItemId,decimal Quantity,string Reference);
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
        IEnumerable<ManufacturingBomLine> lines,IEnumerable<FactoryMaterialUsage> usage)
    {
        var consumed=usage.GroupBy(x=>x.ComponentItemId)
            .ToDictionary(x=>x.Key,x=>x.Sum(y=>y.QuantityConsumed));
        var requirements=lines.GroupBy(x=>x.ComponentItemId).ToDictionary(x=>x.Key,
            x=>x.Sum(y=>plannedQuantity*y.QuantityPer*100m/(100m-y.ScrapPercent)));
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
            return Results.Ok(new{orderId,order.Number,
                rows=FactoryOperationalMetrics.MaterialVariances(order.PlannedQuantity,
                    bom?.Lines??new(),consumed),history=consumed});
        });
        api.MapPost("/factory-material/{orderId:guid}/consumption",
            async(Guid orderId,MaterialConsumptionInput input,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            if(input.Quantity<=0||input.Quantity>1000000000m||string.IsNullOrWhiteSpace(input.Reference)
                ||input.Reference.Trim().Length>200)
                return Results.BadRequest(new{message="مقدار و مرجع مصرف تولیدی معتبر نیست."});
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var order=await db.ManufacturingOrders.SingleOrDefaultAsync(x=>x.Id==orderId,ct);
            if(order is null)return Results.NotFound();
            if(!ManufacturingExecutionEndpoints.IsActive(order.Status))
                return Results.Conflict(new{message="فقط هنگام اجرای دستور می‌توان مصرف پای خط ثبت کرد."});
            var belongs=await db.ManufacturingBomLines.AnyAsync(x=>x.RevisionId==order.BomRevisionId
                && x.ComponentItemId==input.ComponentItemId,ct);
            if(!belongs)return Results.BadRequest(new{message="قطعه در نسخه BOM این دستور نیست."});
            var record=new FactoryMaterialUsage{OrderId=orderId,ComponentItemId=input.ComponentItemId,
                QuantityConsumed=input.Quantity,Reference=input.Reference.Trim(),
                RecordedBy=ctx.User.Identity?.Name??"unknown"};
            db.FactoryMaterialUsages.Add(record);
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=orderId,Action="MaterialUsage",Details=$"Item={input.ComponentItemId};Qty={input.Quantity}"});
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
            if(!await db.WorkCenters.AnyAsync(x=>x.Id==input.WorkCenterId,ct))return Results.NotFound();
            var rate=await db.FactoryCenterCostRates.FindAsync([input.WorkCenterId],ct);
            if(rate is null){rate=new FactoryCenterCostRate{WorkCenterId=input.WorkCenterId};
                db.FactoryCenterCostRates.Add(rate);}
            rate.ConversionRialsPerMinute=input.ConversionRialsPerMinute;
            rate.ScrapRialsPerUnit=input.ScrapRialsPerUnit;rate.EffectiveFrom=start;
            rate.Note=input.Note?.Trim()??"";
            await db.SaveChangesAsync(ct);return Results.Ok(new{rate.WorkCenterId});
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
            var orders=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>x.DueOn>=from&&x.DueOn<=through).ToListAsync(ct);
            var output=FactoryOperationalMetrics.Adherence(mps.Lines,orders,from,through);
            return Results.Ok(new{mpsId,rows=output.Select(x=>new{x.ItemId,x.MpsRequired,
                x.CompletedGood,x.Variance,x.FulfilmentPercent}),note=
                "فقط مقایسه تجمیعی محصول و موعد است؛ ارتباط مستقیم دستور با ردیف MPS هنوز پیاده نشده."});
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
            var routings=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>x.State==RoutingState.Approved).ToListAsync(ct);
            var shifts=await db.WorkShifts.AsNoTracking().ToListAsync(ct);
            var tasks=new List<ApsTask>();
            foreach(var line in version.Lines)
            {
                var selection=routings.Where(x=>x.ItemId==line.ItemId&&x.EffectiveFrom<=line.NeededOn
                    && (x.EffectiveTo is null||line.NeededOn<=x.EffectiveTo.Value)).ToList();
                if(selection.Count!=1 || selection[0].Operations.Count==0
                    ||selection[0].Operations.Any(x=>x.YieldPercent<=0))
                    return Results.UnprocessableEntity(new{message="مسیر ساخت مصوب یکتا با بازده معتبر وجود ندارد."});
                tasks.AddRange(selection[0].Operations.Select(x=>new ApsTask(line.Id,line.ItemId,
                    line.NeededOn,line.Quantity,selection[0].Id,x.Sequence,x.WorkCenterId,
                    x.SetupMinutes+x.RunMinutesPerUnit*line.Quantity*100m/x.YieldPercent,x.Name)));
            }
            var baseline=FiniteFactoryScheduler.Schedule(tasks,shifts,start,input.HorizonDays,input.BaselineCapacity);
            var scenario=FiniteFactoryScheduler.Schedule(tasks,shifts,start,input.HorizonDays,input.ScenarioCapacity);
            return Results.Ok(new{baseline=new{scheduled=baseline.Allocations.Count,
                unscheduled=baseline.Unscheduled.Count,late=baseline.TotalLateOperations},
                scenario=new{scheduled=scenario.Allocations.Count,
                    unscheduled=scenario.Unscheduled.Count,late=scenario.TotalLateOperations},
                note="این محاسبه فقط شبیه‌سازی است؛ هیچ برنامه یا ظرفیت واقعی تغییر نکرده است."});
        }).RequireAuthorization("IpPlanningWrite");
        return endpoints;
    }
}
