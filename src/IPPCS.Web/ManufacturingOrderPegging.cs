using System.Data;
using Microsoft.EntityFrameworkCore;

// Explicit planning provenance within IPPCS; no sales, purchasing or warehouse integration.
public sealed class ManufacturingOrderPeg
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid MpsLineId { get; set; }
    public decimal LinkedQuantity { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string LinkedBy { get; set; } = "";
}
public record OrderPegInput(Guid MpsLineId, decimal LinkedQuantity);
public record PegSummary(Guid MpsLineId,Guid ItemId,decimal Required,decimal Assigned,
    decimal CompletedGood,decimal Outstanding,decimal Unassigned);

public static class ManufacturingOrderPegging
{
    public static bool IsEligible(ManufacturingOrder order,MpsDemandLine line,
        decimal quantity,decimal orderAlreadyLinked,decimal lineAlreadyLinked,
        out string reason)
    {
        reason="";
        if(order.Status is ManufacturingOrderStatus.Cancelled or ManufacturingOrderStatus.Completed)
        {reason="دستور لغوشده یا تکمیل‌شده قابل تخصیص جدید نیست.";return false;}
        if(order.ItemId!=line.ItemId){reason="کالای دستور و ردیف MPS یکسان نیست.";return false;}
        if(quantity<=0 || quantity>order.PlannedQuantity-orderAlreadyLinked)
        {reason="مقدار تخصیص از مانده دستور بیشتر است.";return false;}
        if(quantity>line.Quantity-lineAlreadyLinked)
        {reason="مقدار تخصیص از مانده برنامه اصلی تولید بیشتر است.";return false;}
        if(order.DueOn>line.NeededOn)
        {reason="موعد دستور پس از تاریخ نیاز MPS است.";return false;}
        return true;
    }

    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ManufacturingOrderPeg>().HasOne<ManufacturingOrder>()
            .WithMany().HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderPeg>().HasOne<MpsDemandLine>()
            .WithMany().HasForeignKey(x=>x.MpsLineId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderPeg>().HasIndex(x=>new{x.OrderId,x.MpsLineId}).IsUnique();
        model.Entity<ManufacturingOrderPeg>().Property(x=>x.LinkedQuantity).HasPrecision(18,4);
        model.Entity<ManufacturingOrderPeg>().Property(x=>x.LinkedBy).HasMaxLength(256);
    }
    public static IEndpointRouteBuilder MapOrderPegging(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/production-pegging/mps/{id:guid}",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var version=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(version is null)return Results.NotFound();
            var lineIds=version.Lines.Select(x=>x.Id).ToList();
            var links=await db.ManufacturingOrderPegs.AsNoTracking()
                .Where(x=>lineIds.Contains(x.MpsLineId)).ToListAsync(ct);
            var orderIds=links.Select(x=>x.OrderId).Distinct().ToList();
            var orders=await db.ManufacturingOrders.AsNoTracking().Include(x=>x.Operations)
                .Where(x=>orderIds.Contains(x.Id)).ToListAsync(ct);
            var byOrder=orders.ToDictionary(x=>x.Id);
            var rows=version.Lines.OrderBy(x=>x.NeededOn).Select(line=>{
                var assigned=links.Where(x=>x.MpsLineId==line.Id)
                    .Where(x=>byOrder.TryGetValue(x.OrderId,out var order)
                        && order.Status!=ManufacturingOrderStatus.Cancelled).ToList();
                var qty=assigned.Sum(x=>x.LinkedQuantity);
                decimal completed=0;
                foreach(var link in assigned){
                    var order=byOrder[link.OrderId];
                    if(order.Status!=ManufacturingOrderStatus.Completed)continue;
                    var good=order.Operations.OrderBy(x=>x.Sequence).LastOrDefault()?.GoodQuantity??0m;
                    completed+=order.PlannedQuantity>0?
                        Math.Min(link.LinkedQuantity,good*link.LinkedQuantity/order.PlannedQuantity):0m;
                }
                return new{
                    line.Id,line.ItemId,line.Quantity,
                    neededOnJalali=PersianPlanningDates.Format(line.NeededOn),
                    assignedQuantity=qty,completedGood=completed,
                    remainingDemand=Math.Max(0,line.Quantity-completed),
                    unassignedQuantity=Math.Max(0,line.Quantity-qty),
                    orders=assigned.Select(x=>new{x.OrderId,x.LinkedQuantity,
                        orderNumber=byOrder[x.OrderId].Number,status=byOrder[x.OrderId].Status})
                };
            }).ToList();
            return Results.Ok(new{version.Id,version.Code,version.State,rows});
        });
        api.MapPost("/production-pegging/orders/{orderId:guid}",
            async(Guid orderId,OrderPegInput input,ProductionDb db,HttpContext ctx,CancellationToken ct)=>{
            if(input.LinkedQuantity<=0 || input.MpsLineId==Guid.Empty)
                return Results.BadRequest(new{message="مقدار و ردیف برنامه معتبر لازم است."});
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var order=await db.ManufacturingOrders.SingleOrDefaultAsync(x=>x.Id==orderId,ct);
            if(order is null)return Results.NotFound();
            var line=await db.MpsDemandLines.SingleOrDefaultAsync(x=>x.Id==input.MpsLineId,ct);
            if(line is null)return Results.NotFound();
            var version=await db.MpsVersions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==line.MpsVersionId,ct);
            if(version?.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="فقط برنامه MPS مصوب قابل تخصیص است."});
            if(await db.ManufacturingOrderPegs.AnyAsync(x=>x.OrderId==orderId && x.MpsLineId==line.Id,ct))
                return Results.Conflict(new{message="این ارتباط قبلاً ثبت شده است."});
            var orderAssigned=await db.ManufacturingOrderPegs
                .Where(x=>x.OrderId==orderId).SumAsync(x=>(decimal?)x.LinkedQuantity,ct)??0;
            // Cancelled orders no longer occupy MPS demand capacity; retain their
            // historical links for audit, but exclude their quantities from live netting.
            var lineAssigned=await db.ManufacturingOrderPegs
                .Where(x=>x.MpsLineId==line.Id)
                .Join(db.ManufacturingOrders,x=>x.OrderId,o=>o.Id,(x,o)=>new{x.LinkedQuantity,o.Status})
                .Where(x=>x.Status!=ManufacturingOrderStatus.Cancelled)
                .SumAsync(x=>(decimal?)x.LinkedQuantity,ct)??0;
            if(!IsEligible(order,line,input.LinkedQuantity,orderAssigned,lineAssigned,out var error))
                return Results.Conflict(new{message=error});
            var peg=new ManufacturingOrderPeg{OrderId=orderId,MpsLineId=line.Id,
                LinkedQuantity=input.LinkedQuantity,LinkedBy=ctx.User.Identity?.Name??"unknown"};
            db.ManufacturingOrderPegs.Add(peg);
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit{OrderId=orderId,
                Action="MpsLinked",Details=$"MpsLine={line.Id};Quantity={input.LinkedQuantity}"});
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Created($"/api/v1/production-pegging/mps/{line.MpsVersionId}",new{peg.Id});
        }).RequireAuthorization("IpPlanningWrite");
        return routes;
    }
}
