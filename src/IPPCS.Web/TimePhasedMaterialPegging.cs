using Microsoft.EntityFrameworkCore;

// Independent manual planning assumptions only; not warehouse reservations or purchase receipts.
public record TimePhasedAllocation(Guid MpsLineId,Guid ItemId,DateOnly NeededOn,
    string SourceType,Guid? ReceiptId,DateOnly AvailableOn,decimal Quantity,string BomPath);
public record TimePhasedShortage(Guid MpsLineId,Guid ItemId,DateOnly NeededOn,
    decimal Quantity,string BomPath);
public record TimePhasedPeggingResult(IReadOnlyList<TimePhasedAllocation> Allocations,
    IReadOnlyList<TimePhasedShortage> Shortages);

public static class TimePhasedMaterialPegging
{
    private sealed class SupplyLot(DateOnly date,decimal quantity,string kind,Guid? id)
    {
        public DateOnly Date { get; }=date;
        public string Kind { get; }=kind;
        public Guid? Id { get; }=id;
        public decimal Remaining { get; set; }=quantity;
    }
    // Time-phased gross-material sourcing. No planned manufacturing supply is inferred.
    // Source assignments are deterministic and never use supplies available after the demand.
    public static TimePhasedPeggingResult Allocate(
        IReadOnlyList<BomDemandOrigin> origins,
        IReadOnlyList<PlanningBalance> balances,
        IReadOnlyList<PlanningExpectedReceipt> receipts)
    {
        var allocations=new List<TimePhasedAllocation>();
        var shortages=new List<TimePhasedShortage>();
        var balanceMap=balances.ToDictionary(x=>x.ItemId);
        foreach(var itemGroup in origins.GroupBy(x=>x.ComponentItemId))
        {
            if(!balanceMap.TryGetValue(itemGroup.Key,out var balance))
                throw new InvalidOperationException("برای قطعه موردنیاز موجودی مبنای برنامه‌ریزی ثبت نشده است.");
            if(balance.PhysicalQuantity<0||balance.ReservedQuantity<0||
               balance.QuarantinedQuantity<0||balance.SafetyQuantity<0)
                throw new InvalidOperationException("اقلام موجودی و رزرو نمی‌توانند منفی باشند.");
            var lots=new List<SupplyLot>{
                new(balance.SnapshotDate,Math.Max(0m,balance.PhysicalQuantity-
                    balance.ReservedQuantity-balance.QuarantinedQuantity-balance.SafetyQuantity),
                    "PlanningOpeningBalance",null)
            };
            foreach(var receipt in receipts.Where(x=>x.ItemId==itemGroup.Key)
                .OrderBy(x=>x.AvailableOn).ThenBy(x=>x.Id))
            {
                if(receipt.Quantity<=0||receipt.AvailableOn<=balance.SnapshotDate)
                    throw new InvalidOperationException("دریافت برنامه‌ای نامعتبر یا تکراری نسبت به تاریخ مبنا وجود دارد.");
                lots.Add(new(receipt.AvailableOn,receipt.Quantity,"ExpectedReceipt",receipt.Id));
            }
            lots=lots.OrderBy(x=>x.Date).ThenBy(x=>x.Kind).ThenBy(x=>x.Id).ToList();
            foreach(var need in itemGroup.OrderBy(x=>x.NeededOn)
                .ThenBy(x=>x.MpsLineId).ThenBy(x=>x.Path))
            {
                if(need.GrossQuantity<=0||need.NeededOn<balance.SnapshotDate)
                    throw new InvalidOperationException("مقدار یا تاریخ نیاز نسبت به مبنای برنامه نامعتبر است.");
                var remaining=need.GrossQuantity;
                foreach(var supply in lots)
                {
                    if(supply.Date>need.NeededOn||remaining<=0)break;
                    var taken=Math.Min(remaining,supply.Remaining);
                    if(taken<=0)continue;
                    supply.Remaining-=taken;
                    remaining-=taken;
                    allocations.Add(new(need.MpsLineId,need.ComponentItemId,need.NeededOn,
                        supply.Kind,supply.Id,supply.Date,taken,need.Path));
                }
                if(remaining>0)
                    shortages.Add(new(need.MpsLineId,need.ComponentItemId,need.NeededOn,
                        remaining,need.Path));
            }
        }
        return new(allocations,shortages);
    }

    public static IEndpointRouteBuilder MapTimePhasedPegging(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/mrp/{id:guid}/time-phased-sources",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            var mps=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(mps is null)return Results.NotFound();
            if(mps.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="MPS باید مصوب باشد."});
            var boms=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                .Where(x=>x.State==ManufacturingRevisionState.Approved).ToListAsync(ct);
            var balances=await db.PlanningBalances.AsNoTracking().ToListAsync(ct);
            var receipts=await db.PlanningExpectedReceipts.AsNoTracking().ToListAsync(ct);
            try{
                var origin=ManufacturingDemandTrace.Trace(mps.Lines,boms);
                var result=Allocate(origin,balances,receipts);
                return Results.Ok(new{
                    kind="TimePhasedGrossSupplyAssignment",
                    warning="تخصیص زمانی موجودی/دریافت به نیاز ناخالص BOM است؛ پیشنهاد ساخت خالص MRP و Pegging کامل تولید نیست.",
                    allocations=result.Allocations.Select(x=>new{x.MpsLineId,x.ItemId,
                        neededOnJalali=PersianPlanningDates.Format(x.NeededOn),
                        availableOnJalali=PersianPlanningDates.Format(x.AvailableOn),
                        x.SourceType,x.ReceiptId,x.Quantity,x.BomPath}),
                    shortages=result.Shortages.Select(x=>new{x.MpsLineId,x.ItemId,
                        neededOnJalali=PersianPlanningDates.Format(x.NeededOn),
                        x.Quantity,x.BomPath})
                });
            }
            catch(Exception ex)when(ex is InvalidOperationException or OverflowException)
                {return Results.UnprocessableEntity(new{message=ex.Message});}
        });
        return routes;
    }
}
