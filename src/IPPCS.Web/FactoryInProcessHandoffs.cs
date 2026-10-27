using Microsoft.EntityFrameworkCore;

// Stage 16: documented internal manufacturing operation handoffs; these
// are not warehouse transfers, material issues or physical custody evidence.
public record FactoryHandoffLotGap(Guid ConfirmationId,string LotCode,decimal ReportedGood,
    decimal DocumentedIncomingGood,decimal UntracedGood);
public record FactoryStageHandoff(Guid OrderId,int FromSequence,int ToSequence,
    Guid SourceOperationId,Guid TargetOperationId,decimal EligibleSourceGood,
    decimal DocumentedGood,decimal RemainingUpstreamGood,decimal UntracedDownstreamGood,
    bool QualityGatePassed,int OpenQualityHolds,
    IReadOnlyList<FactoryHandoffLotGap> DestinationLots);
public record FactoryHandoffAudit(Guid OrderId,int StagePairs,int PairsWithUntracedOutput,
    decimal TotalUntracedDownstreamGood,IReadOnlyList<FactoryStageHandoff> Pairs,
    string Disclaimer);

public static class FactoryInProcessHandoffs
{
    public static FactoryHandoffAudit Analyze(ManufacturingOrder order,
        IReadOnlyList<ManufacturingConfirmation> confirmations,
        IReadOnlyList<ManufacturingLotGenealogy> links,
        IReadOnlyList<ManufacturingQualityHold> holds)
    {
        var ops=order.Operations.OrderBy(x=>x.Sequence).ToArray();
        var records=confirmations.ToDictionary(x=>x.Id);
        var output=new List<FactoryStageHandoff>();
        for(var i=1;i<ops.Length;i++)
        {
            var from=ops[i-1];var to=ops[i];
            var previous=confirmations.Where(x=>x.OperationId==from.Id).ToArray();
            var destination=confirmations.Where(x=>x.OperationId==to.Id).ToArray();
            var fromIds=previous.Select(x=>x.Id).ToHashSet();
            var toIds=destination.Select(x=>x.Id).ToHashSet();
            var pairLinks=links.Where(x=>fromIds.Contains(x.SourceConfirmationId) &&
                toIds.Contains(x.DestinationConfirmationId)).ToArray();
            if(pairLinks.Any(x=>x.Quantity<=0))
                throw new InvalidOperationException("انتقال مستند بین مراحل دارای مقدار غیرمجاز است.");
            var forward=Math.Max(0m,from.GoodQuantity-
                from.QualityPendingQuantity-from.QualityRejectedQuantity);
            var documented=pairLinks.Sum(x=>x.Quantity);
            var gaps=destination.Select(lot=>{
                var received=pairLinks.Where(x=>x.DestinationConfirmationId==lot.Id)
                    .Sum(x=>x.Quantity);
                return new FactoryHandoffLotGap(lot.Id,lot.LotCode??"",lot.GoodQuantity,
                    received,Math.Max(0m,lot.GoodQuantity-received));
            }).ToArray();
            var held=holds.Count(x=>x.Status==ManufacturingQualityStatus.Open &&
                (x.OperationId==from.Id||x.OperationId==to.Id));
            var quality=held==0&&from.QualityPendingQuantity==0&&
                to.QualityPendingQuantity==0&&
                ManufacturingInspectionRules.GatePassed(from)&&
                ManufacturingInspectionRules.GatePassed(to);
            output.Add(new(order.Id,from.Sequence,to.Sequence,from.Id,to.Id,
                forward,documented,Math.Max(0m,forward-documented),
                gaps.Sum(x=>x.UntracedGood),quality,held,gaps));
        }
        return new(order.Id,output.Count,output.Count(x=>x.UntracedDownstreamGood>0),
            output.Sum(x=>x.UntracedDownstreamGood),output,
            "فقط مغایرت مستند بچ‌های متوالی داخل IPPCS بررسی می‌شود؛ انتقال فیزیکی یا تراکنش انبار ثبت یا استنباط نمی‌شود.");
    }

    public static IEndpointRouteBuilder MapInProcessHandoffs(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/manufacturing-orders/{id:guid}/handoff-audit",
            async(Guid id,ProductionDb db,CancellationToken ct)=>{
                var order=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                    .ThenInclude(x=>x.InspectionRequirements)
                    .SingleOrDefaultAsync(x=>x.Id==id,ct);
                if(order is null)return Results.NotFound();
                var opIds=order.Operations.Select(x=>x.Id).ToArray();
                var confirmations=await db.ManufacturingConfirmations.AsNoTracking()
                    .Where(x=>opIds.Contains(x.OperationId)).ToListAsync(ct);
                var ids=confirmations.Select(x=>x.Id).ToArray();
                var links=await db.ManufacturingLotGenealogies.AsNoTracking()
                    .Where(x=>x.OrderId==id&&ids.Contains(x.SourceConfirmationId)&&
                        ids.Contains(x.DestinationConfirmationId)).ToListAsync(ct);
                var holds=await db.ManufacturingQualityHolds.AsNoTracking()
                    .Where(x=>x.OrderId==id).ToListAsync(ct);
                try{return Results.Ok(Analyze(order,confirmations,links,holds));}
                catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            });
        return routes;
    }
}
