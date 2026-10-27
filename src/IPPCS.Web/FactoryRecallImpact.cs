using Microsoft.EntityFrameworkCore;

// Manufacturing-only containment enquiry. This cannot assert whether stock
// was moved, shipped, delivered, recalled or physically isolated.
public record FactoryRecallFinalLot(Guid ConfirmationId,Guid OrderId,string OrderNumber,
    string LotCode,decimal RecordedGood,int RegisteredSerials,decimal UnserializedGood);
public record FactoryRecallExposure(Guid RootConfirmationId,string RootLotCode,
    int GenealogyNodes,int GenealogyEdges,int FinalAssemblyOrders,
    int RegisteredSerialCount,bool DepthTruncated,bool HasUnserializedFinalOutput,
    IReadOnlyList<FactoryRecallFinalLot> FinalLots,
    IReadOnlyList<string> SerialNumbers,string Disclaimer);

public static class FactoryRecallImpact
{
    public static FactoryRecallExposure Assess(FactoryTraceResult descendants,
        IReadOnlyList<FactorySerialEvidence> serialLedger,
        IReadOnlyDictionary<Guid,ManufacturingOrder> orders)
    {
        if(descendants.Direction!="descendants")
            throw new InvalidOperationException("تحلیل دامنه اثر باید در جهت خروجی‌های متأثر باشد.");
        if(descendants.HasMoreBeyondDepth)
            throw new InvalidOperationException("عمق گزارش برای بررسی تمام بچ‌های متأثر کافی نیست.");
        var root=descendants.Nodes.SingleOrDefault(x=>x.ConfirmationId==descendants.RootId)
            ??throw new InvalidOperationException("بچ مبدأ شجره یافت نشد.");
        var lots=new List<FactoryRecallFinalLot>();
        foreach(var node in descendants.Nodes)
        {
            if(!orders.TryGetValue(node.OrderId,out var order)||
                order.Kind!=ManufacturingOrderKind.FinalAssembly)
                continue;
            var last=order.Operations.OrderBy(x=>x.Sequence).LastOrDefault();
            if(last is null||last.Id!=node.OperationId)continue;
            if(node.GoodQuantity<0||
                decimal.Truncate(node.GoodQuantity)!=node.GoodQuantity)
                throw new InvalidOperationException("مقدار خروجی نهایی برای ردیابی سریال نامعتبر است.");
            var issued=serialLedger.Count(x=>x.ConfirmationId==node.ConfirmationId);
            if(issued>node.GoodQuantity)
                throw new InvalidOperationException("تعداد سریال از خروجی قابل انتساب بچ نهایی بیشتر است.");
            lots.Add(new(node.ConfirmationId,node.OrderId,node.OrderNumber,
                node.LotCode,node.GoodQuantity,issued,node.GoodQuantity-issued));
        }
        var selected=lots.Select(x=>x.ConfirmationId).ToHashSet();
        var serials=serialLedger.Where(x=>selected.Contains(x.ConfirmationId))
            .Select(x=>x.SerialNumber).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        return new(root.ConfirmationId,root.LotCode,
            descendants.Nodes.Count,descendants.Edges.Count,
            lots.Select(x=>x.OrderId).Distinct().Count(),serials.Length,false,
            lots.Any(x=>x.UnserializedGood>0),
            lots.OrderBy(x=>x.OrderNumber).ThenBy(x=>x.LotCode).ToArray(),
            serials,
            "فقط پیامد پیوندهای مستند تولید IPPCS و سریال‌های ثبت‌شده نمایش داده می‌شود. " +
            "نبود سریال یا پیوند، اثبات بی‌خطر بودن نیست. نتیجه، وضعیت موجودی، ارسال، مشتری، " +
            "قرنطینه فیزیکی یا اجرای فراخوان واقعی را تعیین نمی‌کند.");
    }

    public static IEndpointRouteBuilder MapFactoryRecallImpact(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-recall/source-lot/{confirmationId:guid}",
            async(Guid confirmationId,int? depth,ProductionDb db,CancellationToken ct)=>{
            var maxDepth=depth??16;
            if(maxDepth is <1 or >16)
                return Results.BadRequest(new{message="عمق ردیابی باید بین ۱ و ۱۶ باشد."});
            if(!await db.ManufacturingConfirmations.AsNoTracking()
                .AnyAsync(x=>x.Id==confirmationId,ct))
                return Results.NotFound();
            var total=await db.ManufacturingLotGenealogies.AsNoTracking().CountAsync(ct);
            if(total>10000)
                return Results.Conflict(new{message="دفتر شجره از ظرفیت بررسی کامل بیشتر است؛ گزارش ناقص ارائه نمی‌شود."});
            var edges=await db.ManufacturingLotGenealogies.AsNoTracking().ToListAsync(ct);
            var ids=edges.SelectMany(x=>new[]{x.SourceConfirmationId,x.DestinationConfirmationId})
                .Append(confirmationId).Distinct().ToArray();
            var history=await db.ManufacturingConfirmations.AsNoTracking()
                .Include(x=>x.Operation).ThenInclude(x=>x.Order)
                .Where(x=>ids.Contains(x.Id)).ToListAsync(ct);
            var nodes=history.Select(x=>new FactoryTraceNode(
                x.Id,x.Operation.OrderId,x.Operation.Order.Number,
                x.Operation.Order.ItemId,x.OperationId,x.Operation.Sequence,
                x.LotCode??"",x.GoodQuantity,x.OccurredUtc)).ToArray();
            var links=edges.Select(x=>new FactoryTraceEdge(x.Id,
                x.SourceConfirmationId,x.DestinationConfirmationId,
                x.Quantity,x.OrderId,x.LinkedUtc)).ToArray();
            try
            {
                var trace=FactoryGenealogyExplorer.Explore(confirmationId,
                    "descendants",maxDepth,nodes,links);
                if(trace.HasMoreBeyondDepth)
                    return Results.Conflict(new{
                        message="شجره بعد از عمق انتخابی ادامه دارد؛ گزارش محدود برای تصمیم فراخوان قابل اتکا نیست."});
                var orderIds=trace.Nodes.Select(x=>x.OrderId).Distinct().ToArray();
                var orders=await db.ManufacturingOrders.AsNoTracking()
                    .Include(x=>x.Operations).Where(x=>orderIds.Contains(x.Id))
                    .ToDictionaryAsync(x=>x.Id,ct);
                var (ledger,error)=await FactorySerialGenealogy.LoadLedger(db,ct);
                if(error is not null)
                    return Results.Conflict(new{message=error});
                return Results.Ok(Assess(trace,ledger,orders));
            }
            catch(InvalidOperationException ex)
            {
                return Results.Conflict(new{message=ex.Message});
            }
        });
        return routes;
    }
}
