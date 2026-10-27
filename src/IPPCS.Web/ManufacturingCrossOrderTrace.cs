using System.Data;
using Microsoft.EntityFrameworkCore;

// Stage 11: traceability between IPPCS semi-finished output confirmations and
// final-assembly confirmations. These attest recorded production provenance;
// they DO NOT move stock or communicate with external inventory services.
public record AssemblyOriginInput(Guid SemiFinishedConfirmationId,
    Guid AssemblyConfirmationId,decimal ComponentQuantity);
public record AssemblyOriginRow(Guid LinkId,Guid SourceOrderId,string SourceOrderNumber,
    Guid SourceConfirmationId,string SourceLot,Guid ComponentItemId,
    Guid AssemblyConfirmationId,string AssemblyLot,decimal ComponentQuantity,
    DateTime LinkedUtc);
public record AssemblyBomCoverage(Guid ComponentItemId,decimal RequiredForReportedOutput,
    decimal LinkedProductionQuantity,decimal MissingProductionQuantity,
    decimal OverlinkedProductionQuantity,bool TraceableSemiFinished);
public record FactoryOperationGate(Guid OperationId,int Sequence,string Name,
    decimal Good,decimal Scrap,decimal Rework,decimal ForwardAvailable,
    decimal PendingQuality,int MandatoryCharacteristics,int MissingMandatory,
    int FailedMandatory,bool IsComplete,bool CanProceed);

public static class ManufacturingCrossOrderTrace
{
    public static bool TryLink(ManufacturingOrder semi,ManufacturingOrder final,
        ManufacturingConfirmation source,ManufacturingConfirmation target,
        ManufacturingBomLine bom,decimal quantity,decimal allocatedFrom,decimal allocatedTo,
        decimal destinationOutput,out string reason)
    {
        reason="";
        if(quantity<=0||quantity>1_000_000_000m){
            reason="مقدار شجره تولید باید مثبت و محدود باشد.";return false;}
        if(semi.Id==final.Id||semi.Kind!=ManufacturingOrderKind.SemiFinished||
            final.Kind!=ManufacturingOrderKind.FinalAssembly||
            final.Status is ManufacturingOrderStatus.Cancelled or ManufacturingOrderStatus.Draft||
            semi.Status!=ManufacturingOrderStatus.Completed)
        {reason="منشأ باید دستور نیمه‌ساخته تکمیل‌شده و مقصد دستور مونتاژ معتبر و جداگانه باشد.";return false;}
        if(bom.RevisionId!=final.BomRevisionId||bom.ComponentItemId!=semi.ItemId)
        {reason="کالای نیمه‌ساخته در BOM نسخه قطعی این دستور مونتاژ موجود نیست.";return false;}
        var semiLast=semi.Operations.OrderBy(x=>x.Sequence).LastOrDefault();
        var finalLast=final.Operations.OrderBy(x=>x.Sequence).LastOrDefault();
        if(semiLast is null||finalLast is null||
            source.OperationId!=semiLast.Id||target.OperationId!=finalLast.Id||
            source.Id==target.Id||string.IsNullOrWhiteSpace(source.LotCode)||
            string.IsNullOrWhiteSpace(target.LotCode))
        {reason="شجره باید بین بچ خروجی آخرین عملیات نیمه‌ساخته و بچ خروجی آخرین عملیات مونتاژ باشد.";return false;}
        if(source.OccurredUtc>target.OccurredUtc)
        {reason="ثبت بچ نیمه‌ساخته پس از مونتاژ مقصد است.";return false;}
        if(!ManufacturingInspectionRules.GatePassed(semiLast) ||
            semiLast.QualityPendingQuantity!=0 ||
            !ManufacturingInspectionRules.GatePassed(finalLast)||
            finalLast.QualityPendingQuantity!=0)
        {reason="کنترل‌های اجباری کیفیت یا قرنطینه بچ هنوز تعیین تکلیف نشده است.";return false;}
        if(source.GoodQuantity<=0||target.GoodQuantity<=0||
            semiLast.QualityRejectedQuantity<0 || finalLast.QualityRejectedQuantity<0)
        {reason="انتساب فقط برای خروجی سالم و غیرقرنطینه‌ای مجاز است.";return false;}
        // A final operation may report multiple batches. Aggregate quality
        // rejection conservatively and never assume a rejected unit is fit for dispatch.
        var eligibleSource=Math.Max(0m,Math.Min(source.GoodQuantity,
            semiLast.GoodQuantity-semiLast.QualityRejectedQuantity));
        if(allocatedFrom+quantity>eligibleSource)
        {reason="مقدار شجره از خروجی سالم قابل اتکای نیمه‌ساخته بیشتر است.";return false;}
        if(bom.QuantityPer<=0||bom.ScrapPercent<0||bom.ScrapPercent>=100)
        {reason="نسبت مصرف BOM معتبر نیست.";return false;}
        var required=destinationOutput*bom.QuantityPer*100m/(100m-bom.ScrapPercent);
        if(allocatedTo+quantity>required)
        {reason="انتساب از نیاز قطعه نیمه‌ساخته طبق BOM و خروجی ثبت‌شده مونتاژ بیشتر است.";return false;}
        return true;
    }

    public static IReadOnlyList<AssemblyBomCoverage> Coverage(
        IReadOnlyList<ManufacturingBomLine> bom,IReadOnlyList<ManufacturingConfirmation> lastStep,
        IReadOnlyList<(Guid ComponentId,decimal Linked)> links)
    {
        var reported=lastStep.Sum(x=>x.GoodQuantity+x.ScrapQuantity+x.ReworkQuantity);
        return bom.GroupBy(x=>x.ComponentItemId).Select(group=>{
            var required=group.Sum(x=>reported*x.QuantityPer*100m/(100m-x.ScrapPercent));
            var linked=links.Where(x=>x.ComponentId==group.Key).Sum(x=>x.Linked);
            return new AssemblyBomCoverage(group.Key,required,linked,
                Math.Max(0m,required-linked),Math.Max(0m,linked-required),true);
        }).OrderBy(x=>x.ComponentItemId).ToArray();
    }

    public static IReadOnlyList<FactoryOperationGate> Readiness(
        ManufacturingOrder order)
    {
        var wip=ManufacturingPerformance.CalculateWip(order)
            .ToDictionary(x=>x.OperationId);
        var ordered=order.Operations.OrderBy(x=>x.Sequence).ToArray();
        return ordered.Select((op,index)=>{
            var mandatory=op.InspectionRequirements.Where(x=>x.Mandatory).ToArray();
            var bad=mandatory.Count(x=>x.LastPassed==false);
            var missing=mandatory.Count(x=>x.LastPassed is null);
            var upstream=index==0 || (ordered[index-1].IsComplete &&
                ordered[index-1].QualityPendingQuantity==0 &&
                ManufacturingInspectionRules.GatePassed(ordered[index-1]));
            return new FactoryOperationGate(op.Id,op.Sequence,op.Name,op.GoodQuantity,
                op.ScrapQuantity,op.ReworkQuantity,
                wip[op.Id].ForwardAvailable,op.QualityPendingQuantity,
                mandatory.Length,missing,bad,op.IsComplete,upstream&&!op.IsComplete &&
                ManufacturingExecutionEndpoints.IsActive(order.Status));
        }).ToArray();
    }

    public static IEndpointRouteBuilder MapCrossOrderTrace(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");

        api.MapGet("/manufacturing-orders/{id:guid}/assembly-genealogy",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            if(order.Kind!=ManufacturingOrderKind.FinalAssembly)
                return Results.Conflict(new{message="این گزارش فقط برای دستور مونتاژ نهایی است."});
            var bom=await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==order.BomRevisionId,ct);
            if(bom is null)return Results.Conflict(new{message="BOM دستور وجود ندارد."});
            var lastId=order.Operations.OrderBy(x=>x.Sequence).LastOrDefault()?.Id;
            var results=await db.ManufacturingConfirmations.AsNoTracking()
                .Where(x=>x.OperationId==lastId).ToListAsync(ct);
            var targetIds=results.Select(x=>x.Id).ToArray();
            var links=await db.ManufacturingLotGenealogies.AsNoTracking()
                .Where(x=>x.OrderId==id && targetIds.Contains(x.DestinationConfirmationId))
                .ToListAsync(ct);
            var fromIds=links.Select(x=>x.SourceConfirmationId).Distinct().ToArray();
            var sources=await db.ManufacturingConfirmations.AsNoTracking()
                .Include(x=>x.Operation).ThenInclude(x=>x.Order)
                .Where(x=>fromIds.Contains(x.Id)).ToListAsync(ct);
            var sourceMap=sources.ToDictionary(x=>x.Id);
            var edges=links.Where(x=>sourceMap.TryGetValue(x.SourceConfirmationId,out var src)&&
                src.Operation.OrderId!=id).Select(x=>{
                var src=sourceMap[x.SourceConfirmationId];
                return new AssemblyOriginRow(x.Id,src.Operation.OrderId,
                    src.Operation.Order.Number,src.Id,src.LotCode??"",
                    src.Operation.Order.ItemId,x.DestinationConfirmationId,
                    results.Single(y=>y.Id==x.DestinationConfirmationId).LotCode??"",
                    x.Quantity,x.LinkedUtc);
            }).ToArray();
            var componentIds=bom.Lines.Select(l=>l.ComponentItemId).Distinct().ToArray();
            var traceableItems=await db.Items.AsNoTracking()
                .Where(x=>componentIds.Contains(x.Id) &&
                    x.Type==PlanningItemType.SemiFinished)
                .Select(x=>x.Id).ToListAsync(ct);
            var coverage=Coverage(bom.Lines,results,
                edges.Select(x=>(x.ComponentItemId,x.ComponentQuantity)).ToArray())
                .Where(x=>traceableItems.Contains(x.ComponentItemId))
                .ToArray();
            return Results.Ok(new{orderId=id,orderNumber=order.Number,
                links=edges,coverage,
                disclaimer="پیوند مستند بچ نیمه‌ساخته به مونتاژ نهایی فقط در IPPCS؛ کسری ردیابی نشانه قطعی کسری موجودی نیست. این گزارش جایگزین ثبت انبار یا گواهی کیفیت نیست."});
        });

        api.MapPost("/manufacturing-orders/{id:guid}/assembly-genealogy",
            async(Guid id,AssemblyOriginInput input,ProductionDb db,HttpContext context,CancellationToken ct)=>{
            if(input.SemiFinishedConfirmationId==Guid.Empty||
                input.AssemblyConfirmationId==Guid.Empty||input.ComponentQuantity<=0||
                input.ComponentQuantity>1_000_000_000m)
                return Results.BadRequest(new{message="شناسه بچ و مقدار انتساب معتبر نیست."});
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            // Global lock: all cross-order links may compete for one semi-finished
            // confirmation across different final assembly orders/hosts.
            try{
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"DECLARE @res int;
                       EXEC @res = sys.sp_getapplock @Resource={"IPPCS:AssemblyGenealogy"},
                           @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
                       IF @res < 0 THROW 51004, 'Genealogy lock unavailable', 1;",ct);
            }catch(Microsoft.Data.SqlClient.SqlException){
                return Results.Conflict(new{message="قفل شجره تولید در دسترس نیست."});
            }
            try{await FactoryCapacityReservations.LockOrder(db,id,ct);}
            catch(Microsoft.Data.SqlClient.SqlException)
            {return Results.Conflict(new{message="قفل دستور مونتاژ در دسترس نیست."});}
            if(await db.ManufacturingOrderAudits.AsNoTracking().AnyAsync(x=>
                x.OrderId==id && x.Action==FactoryEndOfLineAcceptance.ApprovedAction,ct))
                return Results.Conflict(new{message="گواهی کنترل نهایی صادر شده؛ منشأ بچ مصوب دیگر قابل افزودن نیست."});
            var final=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .ThenInclude(x=>x.InspectionRequirements).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(final is null)return Results.NotFound();
            var confs=await db.ManufacturingConfirmations.AsNoTracking()
                .Where(x=>x.Id==input.SemiFinishedConfirmationId||
                    x.Id==input.AssemblyConfirmationId).ToListAsync(ct);
            var source=confs.SingleOrDefault(x=>x.Id==input.SemiFinishedConfirmationId);
            var target=confs.SingleOrDefault(x=>x.Id==input.AssemblyConfirmationId);
            if(source is null||target is null)return Results.NotFound();
            var sourceOp=await db.ManufacturingOrderOperations.AsNoTracking()
                .SingleOrDefaultAsync(x=>x.Id==source.OperationId,ct);
            if(sourceOp is null)return Results.NotFound();
            var semi=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .ThenInclude(x=>x.InspectionRequirements)
                .SingleOrDefaultAsync(x=>x.Id==sourceOp.OrderId,ct);
            if(semi is null)return Results.NotFound();
            try{await FactoryCapacityReservations.LockOrder(db,semi.Id,ct);}
            catch(Microsoft.Data.SqlClient.SqlException)
            {return Results.Conflict(new{message="قفل دستور نیمه‌ساخته در دسترس نیست."});}
            var bom=await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.Id==final.BomRevisionId,ct);
            var line=bom?.Lines.SingleOrDefault(x=>x.ComponentItemId==semi.ItemId);
            if(line is null)return Results.Conflict(new{message="نیمه‌ساخته در BOM مصوب دستور مونتاژ نیست."});
            var links=await db.ManufacturingLotGenealogies.AsNoTracking()
                .Where(x=>x.SourceConfirmationId==source.Id ||
                    x.DestinationConfirmationId==target.Id).ToListAsync(ct);
            if(links.Any(x=>x.SourceConfirmationId==source.Id&&
                x.DestinationConfirmationId==target.Id))
                return Results.Conflict(new{message="ارتباط این دو گزارش از قبل ثبت شده است."});
            var targetLinks=links.Where(x=>x.DestinationConfirmationId==target.Id).ToArray();
            var otherFromIds=targetLinks.Select(x=>x.SourceConfirmationId).Distinct().ToArray();
            var otherSources=await db.ManufacturingConfirmations.AsNoTracking()
                .Include(x=>x.Operation).ThenInclude(x=>x.Order)
                .Where(x=>otherFromIds.Contains(x.Id)).ToListAsync(ct);
            var alreadyTo=targetLinks.Join(otherSources,
                link=>link.SourceConfirmationId,conf=>conf.Id,(link,conf)=>new{link,conf})
                .Where(x=>x.conf.Operation.Order.ItemId==semi.ItemId &&
                    x.conf.Operation.OrderId!=id).Sum(x=>x.link.Quantity);
            var alreadyFrom=links.Where(x=>x.SourceConfirmationId==source.Id)
                .Sum(x=>x.Quantity);
            if(!TryLink(semi,final,source,target,line,input.ComponentQuantity,
                alreadyFrom,alreadyTo,target.GoodQuantity+target.ScrapQuantity+
                target.ReworkQuantity,out var error))
                return Results.Conflict(new{message=error});
            var link=new ManufacturingLotGenealogy{OrderId=id,
                SourceConfirmationId=source.Id,DestinationConfirmationId=target.Id,
                Quantity=input.ComponentQuantity,
                LinkedBy=context.User.Identity?.Name??"unknown"};
            db.ManufacturingLotGenealogies.Add(link);
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{
                OrderId=id,Action="AssemblyOriginLinked",
                Details=$"SourceOrder={semi.Id:N};Source={source.Id:N};Target={target.Id:N};Qty={input.ComponentQuantity}"
            });
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/manufacturing-orders/{id}/assembly-genealogy",
                new{link.Id,sourceOrderId=semi.Id,componentItemId=semi.ItemId});
        }).RequireAuthorization("IpQualityApprove");

        api.MapGet("/manufacturing-orders/{id:guid}/production-readiness",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .ThenInclude(x=>x.InspectionRequirements).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            var rows=Readiness(order);
            var unresolved=await db.ManufacturingQualityHolds.AsNoTracking()
                .CountAsync(x=>x.OrderId==id&&x.Status==ManufacturingQualityStatus.Open,ct);
            return Results.Ok(new{orderId=id,order.Number,order.Kind,order.Status,
                rows,openQualityHolds=unresolved,
                isFinalQualityReady=ManufacturingInspectionRules.CanComplete(order)&&unresolved==0,
                disclaimer="وضعیت آمادگی بر اساس گزارش‌ها و بازرسی ثبت‌شده IPPCS است؛ نه اجازه تحویل فیزیکی یا ثبت موجودی انبار."});
        });

        api.MapGet("/manufacturing-orders/{id:guid}/inspection-summary",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .ThenInclude(x=>x.InspectionRequirements).SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(order is null)return Results.NotFound();
            var all=order.Operations.SelectMany(x=>x.InspectionRequirements)
                .ToArray();
            var ids=all.Select(x=>x.Id).ToArray();
            var results=await db.ManufacturingInspectionResults.AsNoTracking()
                .Where(x=>x.OrderId==id&&ids.Contains(x.RequirementId))
                .ToListAsync(ct);
            return Results.Ok(new{orderId=id,resultsCount=results.Count,
                failedResults=results.Count(x=>!x.Passed),
                heldQuantity=results.Sum(x=>x.QuarantinedQuantity),
                characteristics=all.Select(x=>new{
                    x.Id,x.OrderOperationId,x.Code,x.Minimum,x.Maximum,x.Mandatory,
                    sampleCount=results.Count(y=>y.RequirementId==x.Id),
                    passedSamples=results.Count(y=>y.RequirementId==x.Id&&y.Passed),
                    failedSamples=results.Count(y=>y.RequirementId==x.Id&&!y.Passed),
                    lastPassed=x.LastPassed,lastMeasuredUtc=x.LastMeasuredUtc,
                    awaitingMandatory=x.Mandatory&&x.LastPassed!=true
                }).ToArray(),
                disclaimer="تعداد دفعات اندازه‌گیری تاریخی با برنامه نمونه‌برداری SPC یا MSA معادل نیست. نتایج مردود و آخرین وضعیت مشخصه‌ها جدا هستند."});
        });
        return routes;
    }
}
