using Microsoft.EntityFrameworkCore;

// Stage 12: read-only production insights. All metrics use internal IPPCS
// records only. No warehouse, sales, purchasing, financial or HR integration.
public record FactoryMaterialExecutionRow(Guid ComponentItemId,decimal PlannedQuantity,
    decimal ReportedFinalOperationQuantity,decimal StandardForPlannedQuantity,
    decimal? StandardForReportedOutput,decimal? ActualLoggedConsumption,
    decimal? OutputBasedVariance,decimal? OutputBasedVariancePercent,
    int RecordedEntries,string Confidence);
public record FactorySpcSample(Guid Id,DateTime RecordedUtc,decimal Value,bool Passed);
public record FactorySpcReport(Guid OrderId,Guid RequirementId,string Code,int SampleCount,
    decimal? Mean,decimal? MovingRangeMean,decimal? LowerControlLimit,
    decimal? UpperControlLimit,int SpecificationFailures,int ControlLimitViolations,
    bool HasReferenceLimits,string Warning,IReadOnlyList<FactorySpcSample> Samples);
public record FactoryProductionAlert(string Type,string Severity,Guid ReferenceId,
    string Identifier,DateOnly? DueDate,int? DaysOverdue,string Message);
public record FactoryAlertsReport(string AsOfJalali,int DaysAhead,
    int CriticalCount,int HighCount,int MediumCount,
    IReadOnlyList<FactoryProductionAlert> Alerts,string Disclaimer);

public static class FactoryExecutionInsights
{
    public static IReadOnlyList<FactoryMaterialExecutionRow> MaterialReconciliation(
        ManufacturingOrder order,IReadOnlyList<ManufacturingBomLine> bom,
        IReadOnlyList<FactoryMaterialUsage> usages)
    {
        var last=order.Operations.OrderBy(x=>x.Sequence).LastOrDefault();
        var hasOutput=last is not null &&
            last.GoodQuantity+last.ScrapQuantity+last.ReworkQuantity>0;
        var reported=last is null?0m:last.GoodQuantity+last.ScrapQuantity+last.ReworkQuantity;
        return bom.GroupBy(x=>x.ComponentItemId).OrderBy(x=>x.Key).Select(group=>{
            var records=usages.Where(x=>x.ComponentItemId==group.Key).ToArray();
            var factor=group.Sum(x=>x.QuantityPer*100m/(100m-x.ScrapPercent));
            var planned=order.PlannedQuantity*factor;
            decimal? outputBased=hasOutput?reported*factor:null;
            decimal? actual=records.Length>0?records.Sum(x=>x.QuantityConsumed):null;
            decimal? delta=actual.HasValue&&outputBased.HasValue
                ?actual.Value-outputBased.Value:null;
            decimal? ratio=delta.HasValue && outputBased>0
                ?Math.Round(100m*delta.Value/outputBased.Value,2):null;
            var confidence=!hasOutput?"NoFinalOutput":
                records.Length==0?"NoConsumptionLog":"RecordedInternalEvidence";
            return new FactoryMaterialExecutionRow(group.Key,order.PlannedQuantity,
                reported,planned,outputBased,actual,delta,ratio,records.Length,confidence);
        }).ToArray();
    }

    public static FactorySpcReport IndividualsChart(Guid orderId,
        ManufacturingInspectionRequirement characteristic,
        IReadOnlyList<ManufacturingInspectionResult> ordered)
    {
        var rows=ordered.OrderBy(x=>x.RecordedUtc).ThenBy(x=>x.Id).ToArray();
        var samples=rows.Select(x=>new FactorySpcSample(x.Id,x.RecordedUtc,
            x.Value,x.Passed)).ToArray();
        var specFailures=rows.Count(x=>!x.Passed);
        decimal? mean=null,range=null,lcl=null,ucl=null;
        var breaches=0;
        var warning="";
        if(rows.Length>=5)
        {
            mean=rows.Average(x=>x.Value);
            range=rows.Skip(1).Select((x,i)=>Math.Abs(x.Value-rows[i].Value)).Average();
            if(range>0)
            {
                // 2.66 = 3 / d2 for an individuals chart using moving ranges of two.
                lcl=mean.Value-2.66m*range.Value;
                ucl=mean.Value+2.66m*range.Value;
                breaches=rows.Count(x=>x.Value<lcl.Value||x.Value>ucl.Value);
                warning=breaches>0?
                    "نقطه خارج از حدود کنترلی برآوردی دیده شد؛ تحلیل فرایند ضروری است.":
                    "حدود کنترل از داده‌های همین دستور برآورد شده‌اند و هنوز حدود مصوب SPC نیستند.";
            }
            else warning="پراکندگی اندازه‌گیری صفر است؛ حدود کنترلی آماری قابل اتکا نیست.";
        }
        else warning="برای محاسبه نمودار افراد و دامنه متحرک حداقل پنج اندازه‌گیری لازم است.";
        return new FactorySpcReport(orderId,characteristic.Id,characteristic.Code,
            rows.Length,mean,range,lcl,ucl,specFailures,breaches,
            characteristic.Minimum.HasValue||characteristic.Maximum.HasValue,
            warning,samples);
    }

    public static FactoryAlertsReport ProductionAlerts(DateOnly today,int daysAhead,
        IReadOnlyList<ManufacturingOrder> orders,
        IReadOnlyList<ManufacturingQualityHold> holds,
        IReadOnlyList<ProductionDeviation> deviations)
    {
        if(daysAhead is <1 or >30)throw new ArgumentOutOfRangeException(nameof(daysAhead));
        var alerts=new List<FactoryProductionAlert>();
        foreach(var hold in holds.Where(x=>x.Status==ManufacturingQualityStatus.Open))
        {
            alerts.Add(new("QualityHold","Critical",hold.Id,
                hold.OrderId.ToString("N"),null,null,
                "مقدار "+hold.Quantity+" در فرایند ساخت به علت "+hold.Reason+
                " هنوز تعیین تکلیف نشده است."));
        }
        foreach(var order in orders.Where(x=>x.Status is not
            (ManufacturingOrderStatus.Completed or ManufacturingOrderStatus.Cancelled)))
        {
            var remaining=order.DueOn.DayNumber-today.DayNumber;
            if(remaining<0)
                alerts.Add(new("OrderOverdue","High",order.Id,order.Number,order.DueOn,
                    -remaining,"دستور ساخت از موعد برنامه عبور کرده است."));
            else if(remaining<=daysAhead)
                alerts.Add(new("OrderDueSoon","Medium",order.Id,order.Number,order.DueOn,
                    0,"موعد برنامه دستور ساخت نزدیک است."));
        }
        foreach(var deviation in deviations)
        {
            if(deviation.Status==ProductionDeviationStatus.Closed)
            {
                var verification=FactoryDeviationEffectiveness.Evaluate(deviation);
                if(!verification.IndependentlyVerified)
                    alerts.Add(new("DeviationEffectivenessPending",
                        verification.State=="InvalidEvidence"?"Critical":"High",
                        deviation.Id,deviation.Responsible??
                            deviation.MpsLineId.ToString("N"),null,null,
                        verification.State=="InvalidEvidence"?
                            "سابقه بررسی اثربخشی اقدام اصلاحی نامعتبر است؛ بازبینی مدیریتی لازم است.":
                            "اقدام اصلاحی مختومه شده، اما اثربخشی آن هنوز به‌صورت مستقل تأیید نشده است."));
                continue;
            }
            var due=deviation.ActionDueOn;
            var days=due.HasValue?due.Value.DayNumber-today.DayNumber:(int?)null;
            var overdue=days.HasValue && days.Value<0;
            var noAction=deviation.Status==ProductionDeviationStatus.Open;
            var escalated=deviation.Events.Any(x=>x.Details.StartsWith(
                FactoryDeviationGovernance.EscalationPrefix,StringComparison.Ordinal));
            if(!overdue && !noAction && !(days.HasValue&&days<=daysAhead)&&!escalated)continue;
            var sev=escalated&&overdue?"Critical":overdue||escalated?"High":"Medium";
            alerts.Add(new("DeviationAction",sev,deviation.Id,
                deviation.Responsible??deviation.MpsLineId.ToString("N"),due,
                overdue?-days!.Value:0,
                (escalated?"پیگیری تشدیدشده ثبت شده است. ":"")+
                (overdue?"اقدام اصلاحی انحراف از موعد گذشته است.":
                noAction?"انحراف هنوز اقدام اصلاحی تخصیص‌یافته ندارد.":
                "سررسید اقدام اصلاحی نزدیک است.")));
        }
        var sorted=alerts.OrderBy(x=>x.Severity=="Critical"?0:x.Severity=="High"?1:2)
            .ThenBy(x=>x.DueDate).ThenBy(x=>x.ReferenceId).ToArray();
        return new(PersianPlanningDates.Format(today),daysAhead,
            sorted.Count(x=>x.Severity=="Critical"),
            sorted.Count(x=>x.Severity=="High"),
            sorted.Count(x=>x.Severity=="Medium"),sorted,
            "هشدارها از داده‌های داخلی IPPCS محاسبه می‌شوند و پرونده مختومه تا تأیید مستقل اثربخشی باز می‌ماند؛ هیچ پیامک، اقدام خودکار یا ارتباط با سامانه بیرونی ایجاد نمی‌شود.");
    }

    public static IEndpointRouteBuilder MapFactoryExecutionInsights(
        this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-material/{orderId:guid}/execution-reconciliation",
            async(Guid orderId,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .SingleOrDefaultAsync(x=>x.Id==orderId,ct);
            if(order is null)return Results.NotFound();
            var bom=await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==order.BomRevisionId,ct);
            if(bom is null)return Results.Conflict(new{message="BOM معتبر برای دستور یافت نشد."});
            if(bom.Lines.Any(x=>x.ScrapPercent<0||x.ScrapPercent>=100))
                return Results.Conflict(new{message="ضریب ضایعات BOM معتبر نیست."});
            var usage=await db.FactoryMaterialUsages.AsNoTracking()
                .Where(x=>x.OrderId==orderId).ToListAsync(ct);
            return Results.Ok(new{orderId,order.Number,rows=
                MaterialReconciliation(order,bom.Lines,usage),
                disclaimer="انحراف مقدار ثبت‌شده مصرف پای خط نسبت به نیاز استاندارد خروجی آخرین عملیات؛ داده مصرف ناموجود Null است، نه صفر. بدون صدور حواله یا ثبت انبار."});
        });
        api.MapGet("/manufacturing-orders/{id:guid}/spc/{requirementId:guid}",
            async(Guid id,Guid requirementId,ProductionDb db,CancellationToken ct)=>{
            var requirement=await db.ManufacturingInspectionRequirements.AsNoTracking()
                .SingleOrDefaultAsync(x=>x.Id==requirementId,ct);
            if(requirement is null)return Results.NotFound();
            if(!await db.ManufacturingOrderOperations.AsNoTracking()
                .AnyAsync(x=>x.Id==requirement.OrderOperationId&&x.OrderId==id,ct))
                return Results.NotFound();
            var samples=await db.ManufacturingInspectionResults.AsNoTracking()
                .Where(x=>x.OrderId==id && x.RequirementId==requirementId)
                .OrderBy(x=>x.RecordedUtc).ThenBy(x=>x.Id).Take(501).ToListAsync(ct);
            if(samples.Count>500)return Results.Conflict(new{
                message="تعداد نمونه بیش از محدوده گزارش است؛ نتایج ناقص نمایش داده نمی‌شوند."});
            return Results.Ok(IndividualsChart(id,requirement,samples));
        });
        api.MapGet("/factory-kpis/production-alerts",
            async(string asOfJalali,int daysAhead,ProductionDb db,CancellationToken ct)=>{
            if(!PersianPlanningDates.TryParse(asOfJalali,out var asOf)||
                daysAhead is <1 or >30)
                return Results.BadRequest(new{message="تاریخ شمسی و افق هشدار ۱ تا ۳۰ روز معتبر لازم است."});
            var orders=await db.ManufacturingOrders.AsNoTracking()
                .Where(x=>x.Status!=ManufacturingOrderStatus.Completed &&
                    x.Status!=ManufacturingOrderStatus.Cancelled)
                .OrderBy(x=>x.DueOn).Take(5001).ToListAsync(ct);
            var holds=await db.ManufacturingQualityHolds.AsNoTracking()
                .Where(x=>x.Status==ManufacturingQualityStatus.Open)
                .Take(5001).ToListAsync(ct);
            // Include closed-but-unverified CAPAs: closure alone must not
            // remove a real unresolved effectiveness alert from dashboards.
            // Refuse incomplete reports rather than silently truncating results.
            var deviations=await db.ProductionDeviations.AsNoTracking().Include(x=>x.Events)
                .Take(5001).ToListAsync(ct);
            if(orders.Count>5000||holds.Count>5000||deviations.Count>5000)
                return Results.Conflict(new{message="داده هشدار بیشتر از ظرفیت گزارش است؛ خروجی ناقص ارائه نمی‌شود."});
            return Results.Ok(ProductionAlerts(asOf,daysAhead,orders,holds,deviations));
        });
        return routes;
    }
}
