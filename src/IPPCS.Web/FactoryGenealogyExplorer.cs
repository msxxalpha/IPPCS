using Microsoft.EntityFrameworkCore;

// IPPCS-only evidence graph: follows explicit manufacturing genealogy links,
// including operation-to-operation and semi-finished-to-final-assembly links.
// It does not infer inventory, receipt, shipment or unrecorded ancestry.
public record FactoryTraceNode(Guid ConfirmationId,Guid OrderId,string OrderNumber,
    Guid ItemId,Guid OperationId,int Sequence,string LotCode,
    decimal GoodQuantity,DateTime OccurredUtc);
public record FactoryTraceEdge(Guid Id,Guid SourceId,Guid TargetId,
    decimal Quantity,Guid RecordedOrderId,DateTime LinkedUtc);
public record FactoryTraceResult(Guid RootId,string Direction,int DepthReached,
    IReadOnlyList<FactoryTraceNode> Nodes,IReadOnlyList<FactoryTraceEdge> Edges,
    int BatchCount,int OrderCount,bool HasLinks,bool HasMoreBeyondDepth,string Disclaimer);

public static class FactoryGenealogyExplorer
{
    // A bounded graph scan fails explicitly instead of silently hiding
    // relevant links in a recall enquiry.
    public static FactoryTraceResult Explore(Guid root,string direction,int maxDepth,
        IReadOnlyList<FactoryTraceNode> nodes,IReadOnlyList<FactoryTraceEdge> links)
    {
        if(direction is not ("ancestors" or "descendants") || maxDepth is <1 or >16)
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        if(nodes.Select(x=>x.ConfirmationId).Distinct().Count()!=nodes.Count||
            links.Select(x=>x.Id).Distinct().Count()!=links.Count)
            throw new InvalidOperationException("شجره تولید دارای شناسه تکراری است.");
        var lookup=nodes.ToDictionary(x=>x.ConfirmationId);
        if(!lookup.ContainsKey(root))
            throw new InvalidOperationException("بچ یا گزارش تولید انتخاب‌شده موجود نیست.");
        // The query loads the entire bounded journal. Validate ALL its edges
        // before giving any positive recall assurance: duplicate endpoints,
        // forged quantities, dangling reports and loops can hide exposure.
        var indegree=lookup.Keys.ToDictionary(id=>id,_=>0);
        var outbound=new Dictionary<Guid,List<Guid>>();
        foreach(var edge in links)
        {
            if(edge.Id==Guid.Empty||edge.SourceId==edge.TargetId||
                !lookup.ContainsKey(edge.SourceId)||
                !lookup.ContainsKey(edge.TargetId)||edge.Quantity<=0)
                throw new InvalidOperationException("شجره تولید دارای پیوند مفقود، خودارجاع یا مقدار مخدوش است.");
            if(!outbound.TryGetValue(edge.SourceId,out var targets))
                outbound[edge.SourceId]=targets=new List<Guid>();
            targets.Add(edge.TargetId);
            indegree[edge.TargetId]++;
        }
        var queue=new Queue<Guid>(indegree.Where(x=>x.Value==0).Select(x=>x.Key));
        var checkedNodes=0;
        while(queue.TryDequeue(out var id))
        {
            checkedNodes++;
            if(!outbound.TryGetValue(id,out var targets))continue;
            foreach(var target in targets)
                if(--indegree[target]==0)queue.Enqueue(target);
        }
        if(checkedNodes!=lookup.Count)
            throw new InvalidOperationException("چرخه غیرمجاز در شجره ثبت‌شده تولید وجود دارد.");
        var seen=new HashSet<Guid>{root};
        var selected=new HashSet<Guid>();
        var frontier=new HashSet<Guid>{root};
        var depth=0;
        for(var level=1;level<=maxDepth && frontier.Count>0;level++)
        {
            var matched=links.Where(x=>direction=="ancestors"?
                frontier.Contains(x.TargetId):frontier.Contains(x.SourceId)).ToArray();
            var next=new HashSet<Guid>();
            foreach(var edge in matched)
            {
                if(!lookup.ContainsKey(edge.SourceId)||!lookup.ContainsKey(edge.TargetId))
                    throw new InvalidOperationException("شجره تولید به گزارش ناموجود اشاره دارد.");
                if(edge.Quantity<=0)
                    throw new InvalidOperationException("ارتباط بچ مقدار نامعتبر دارد.");
                selected.Add(edge.Id);
                var neighbor=direction=="ancestors"?edge.SourceId:edge.TargetId;
                if(seen.Add(neighbor))next.Add(neighbor);
            }
            if(matched.Length>0)depth=level;
            frontier=next;
        }
        var hasMore=frontier.Any(id=>links.Any(edge=>direction=="ancestors"?
            edge.TargetId==id&&!seen.Contains(edge.SourceId):
            edge.SourceId==id&&!seen.Contains(edge.TargetId)));
        var reported=nodes.Where(x=>seen.Contains(x.ConfirmationId))
            .OrderBy(x=>x.OccurredUtc).ThenBy(x=>x.ConfirmationId).ToArray();
        var selectedEdges=links.Where(x=>selected.Contains(x.Id))
            .OrderBy(x=>x.LinkedUtc).ThenBy(x=>x.Id).ToArray();
        return new(root,direction,depth,reported,selectedEdges,
            reported.Select(x=>x.LotCode).Where(x=>!string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal).Count(),
            reported.Select(x=>x.OrderId).Distinct().Count(),
            selectedEdges.Length>0,hasMore,
            "فقط پیوندهای صریح ثبت‌شده ساخت در IPPCS نشان داده می‌شوند. نبود پیوند به معنی نبود قطعه یا اثبات عدم مصرف نیست. این گزارش جایگزین سامانه انبار یا فراخوان محصول نیست.");
    }

    public static IEndpointRouteBuilder MapGenealogyExplorer(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/manufacturing-genealogy/explore/{confirmationId:guid}",
            async(Guid confirmationId,string direction,int? depth,ProductionDb db,CancellationToken ct)=>{
            var max=depth??8;
            if(direction is not ("ancestors" or "descendants") || max is <1 or >16)
                return Results.BadRequest(new{message="جهت ردیابی یا عمق شبکه معتبر نیست."});
            var root=await db.ManufacturingConfirmations.AsNoTracking()
                .AnyAsync(x=>x.Id==confirmationId,ct);
            if(!root)return Results.NotFound();
            // Bound is a safety limit, never a partial "complete" report.
            var total=await db.ManufacturingLotGenealogies.AsNoTracking().CountAsync(ct);
            if(total>10000)return Results.Conflict(new{
                message="حجم شجره فراتر از ظرفیت گزارش آزمایشی است؛ گزارش ناقص ارائه نمی‌شود."});
            var source=await db.ManufacturingLotGenealogies.AsNoTracking().ToListAsync(ct);
            var ids=source.SelectMany(x=>new[]{x.SourceConfirmationId,x.DestinationConfirmationId})
                .Append(confirmationId).Distinct().ToArray();
            var confirmations=await db.ManufacturingConfirmations.AsNoTracking()
                .Include(x=>x.Operation).ThenInclude(x=>x.Order)
                .Where(x=>ids.Contains(x.Id)).ToListAsync(ct);
            var nodes=confirmations.Select(x=>new FactoryTraceNode(
                x.Id,x.Operation.OrderId,x.Operation.Order.Number,
                x.Operation.Order.ItemId,x.OperationId,x.Operation.Sequence,
                x.LotCode??"",x.GoodQuantity,x.OccurredUtc)).ToArray();
            var edges=source.Select(x=>new FactoryTraceEdge(x.Id,
                x.SourceConfirmationId,x.DestinationConfirmationId,
                x.Quantity,x.OrderId,x.LinkedUtc)).ToArray();
            try{
                return Results.Ok(Explore(confirmationId,direction,max,nodes,edges));
            }
            catch(InvalidOperationException ex){
                return Results.Conflict(new{message=ex.Message});
            }
        });
        return routes;
    }
}
