using Microsoft.EntityFrameworkCore;

// Sampling-frequency what-if on the frozen per-order control-plan requirement.
// It cannot certify approved frequency, MSA, calibration, or actual per-lot
// sampling: inspection results currently reference an operation, not a lot.
public sealed record FactorySamplingCoverageResult(
    Guid OrderId,Guid RequirementId,Guid OperationId,string Characteristic,
    int HypotheticalEveryUnits,decimal ReportedOperationUnits,
    int MinimumSamplesUnderHypothesis,int ActualRecordedSamples,
    int ApparentCountGap,bool PerLotSamplingVerified,
    bool SamplingPolicyApproved,bool MeasurementSystemVerified,
    string Assessment,string Disclaimer);

public static class FactorySamplingCoverage
{
    public static FactorySamplingCoverageResult Evaluate(Guid orderId,
        ManufacturingOrderOperation operation,
        ManufacturingInspectionRequirement requirement,
        IReadOnlyList<ManufacturingInspectionResult> samples,int everyUnits)
    {
        if(everyUnits is <1 or >10000)
            throw new ArgumentOutOfRangeException(nameof(everyUnits));
        var units=operation.GoodQuantity+operation.ScrapQuantity+
            operation.ReworkQuantity;
        if(units<0 || units>1_000_000m)
            throw new ArgumentOutOfRangeException(nameof(operation));
        var expected=(int)decimal.Ceiling(units/everyUnits);
        var recorded=samples.Count;
        var gap=Math.Max(0,expected-recorded);
        return new(orderId,requirement.Id,operation.Id,requirement.Code,
            everyUnits,units,expected,recorded,gap,false,false,false,
            units==0?"NoProduction":
                recorded==0?"NoSamples":
                gap>0?"PotentialSampleCountGap":"CountOnlyUnverified",
            "بسامد نمونه‌برداری این فرم صرفاً فرض آزمایشی است؛ نسخه سیاست نمونه‌برداری مصوب ذخیره نشده و نمونه‌ها به بچ/تعداد قطعه/ابزار ارجاع ندارند. برابری تعداد نمونه‌ها اثبات رعایت فراوانی یا کیفیت نیست؛ بدون شواهد MSA و طرح نمونه‌برداری واقعی اجازه تأیید EOL، آزادسازی یا Cp/Cpk صادر نمی‌شود. کالیبراسیون در سامانه مستقل سازمان باقی می‌ماند.");
    }

    public static IEndpointRouteBuilder MapFactorySamplingCoverage(
        this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/manufacturing-orders/{id:guid}/sampling-coverage/{requirementId:guid}",
            async(Guid id,Guid requirementId,int everyUnits,ProductionDb db,CancellationToken ct)=>{
                if(everyUnits is <1 or >10000)
                    return Results.BadRequest(new{message="تعداد قطعه در هر نمونه باید بین ۱ تا ۱۰۰۰۰ باشد."});
                var req=await db.ManufacturingInspectionRequirements.AsNoTracking()
                    .SingleOrDefaultAsync(x=>x.Id==requirementId,ct);
                if(req is null)return Results.NotFound();
                var op=await db.ManufacturingOrderOperations.AsNoTracking()
                    .SingleOrDefaultAsync(x=>x.Id==req.OrderOperationId&&x.OrderId==id,ct);
                if(op is null)return Results.NotFound();
                var samples=await db.ManufacturingInspectionResults.AsNoTracking()
                    .Where(x=>x.OrderId==id&&x.RequirementId==requirementId)
                    .OrderBy(x=>x.RecordedUtc).ThenBy(x=>x.Id)
                    .Take(501).ToListAsync(ct);
                if(samples.Count>500)
                    return Results.Conflict(new{message="نتایج بازرسی بیش از سقف گزارش است؛ خروجی ناقص ارائه نمی‌شود."});
                try{return Results.Ok(Evaluate(id,op,req,samples,everyUnits));}
                catch(ArgumentOutOfRangeException){
                    return Results.Conflict(new{
                        message="مقادیر تولید عملیات برای محاسبه فرضی فراوانی نمونه‌برداری قابل اتکا نیست."});
                }
            }).RequireAuthorization("IpProductionUser");
        return routes;
    }
}
