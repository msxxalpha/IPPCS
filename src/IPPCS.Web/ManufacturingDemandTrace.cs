using Microsoft.EntityFrameworkCore;

// Gross provenance only. Never claim these paths are net-stock allocations or MRP planned-order pegging.
public record BomDemandOrigin(Guid MpsLineId,Guid RootItemId,Guid ComponentItemId,
    Guid ParentItemId,Guid BomRevisionId,DateOnly NeededOn,decimal GrossQuantity,
    string Path,int Level);

public static class ManufacturingDemandTrace
{
    public static IReadOnlyList<BomDemandOrigin> Trace(
        IReadOnlyList<MpsDemandLine> lines,
        IReadOnlyList<ManufacturingBomRevision> revisions)
    {
        var approved=revisions.Where(x=>x.State==ManufacturingRevisionState.Approved).ToArray();
        var output=new List<BomDemandOrigin>();
        foreach(var line in lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.Id))
        {
            if(line.Quantity<=0)throw new InvalidOperationException("مقدار ردیف MPS نامعتبر است.");
            void Expand(Guid parent,decimal qty,string path,HashSet<Guid> ancestors,int level)
            {
                if(level>50||!ancestors.Add(parent))
                    throw new InvalidOperationException("چرخه یا عمق غیرمجاز BOM شناسایی شد.");
                var matching=approved.Where(x=>x.ParentItemId==parent&&
                    x.ValidFrom<=line.NeededOn &&
                    (!x.ValidUntil.HasValue||line.NeededOn<=x.ValidUntil.Value)).ToArray();
                if(matching.Length>1)throw new InvalidOperationException("برای یک تاریخ چند BOM مصوب معتبر است.");
                if(matching.Length==1)
                {
                    foreach(var component in PlanningCalculators.EffectiveDemandLines(matching[0]).OrderBy(x=>x.Sequence))
                    {
                        if(component.QuantityPer<=0||component.ScrapPercent<0||component.ScrapPercent>=100)
                            throw new InvalidOperationException("نسبت مصرف یا ضایعات BOM نامعتبر است.");
                        var need=qty*component.QuantityPer/(1-component.ScrapPercent/100m);
                        var nextPath=path+"/"+component.ComponentItemId;
                        output.Add(new BomDemandOrigin(line.Id,line.ItemId,component.ComponentItemId,
                            parent,matching[0].Id,line.NeededOn,need,nextPath,level));
                        Expand(component.ComponentItemId,need,nextPath,ancestors,level+1);
                    }
                }
                ancestors.Remove(parent);
            }
            Expand(line.ItemId,line.Quantity,line.ItemId.ToString(),new HashSet<Guid>(),1);
        }
        return output;
    }
    public static IEndpointRouteBuilder MapDemandTrace(this IEndpointRouteBuilder routes)
    {
        var group=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        group.MapGet("/mrp/{mpsVersionId:guid}/gross-origins",async(Guid mpsVersionId,
            ProductionDb db,CancellationToken ct)=>{
            var version=await db.MpsVersions.AsNoTracking().Include(x=>x.Lines)
                .SingleOrDefaultAsync(x=>x.Id==mpsVersionId,ct);
            if(version is null)return Results.NotFound();
            if(version.State!=PlanningApproval.Approved)
                return Results.Conflict(new{message="ردیابی فقط برای MPS مصوب انجام می‌شود."});
            var boms=await db.ManufacturingBomRevisions.AsNoTracking()
                .Include(x=>x.Lines).Where(x=>x.State==ManufacturingRevisionState.Approved).ToListAsync(ct);
            try{
                var origins=Trace(version.Lines,boms);
                return Results.Ok(new{
                    kind="GrossBomProvenance",
                    warning="این گزارش منشأ نیاز ناخالص را نمایش می‌دهد و تخصیص موجودی، رسید یا پیشنهاد ساخت خالص MRP نیست.",
                    rows=origins.Select(x=>new{x.MpsLineId,x.RootItemId,x.ComponentItemId,x.ParentItemId,
                        x.BomRevisionId,x.GrossQuantity,x.Level,x.Path,
                        neededOnJalali=PersianPlanningDates.Format(x.NeededOn)})
                });
            }
            catch(Exception ex) when(ex is InvalidOperationException or OverflowException)
            {return Results.UnprocessableEntity(new{message=ex.Message});}
        });
        return routes;
    }
}
