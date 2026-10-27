using Microsoft.EntityFrameworkCore;

// Read-only engineering change feasibility. This is an ECO impact assessment,
// NOT signed engineering change authorization and NOT automatic version cutover.
public record FactoryEngineeringPartDelta(Guid ComponentId,decimal OldQuantity,decimal NewQuantity,
    decimal OldScrapPercent,decimal NewScrapPercent,string ChangeType);
public record FactoryEngineeringStepDelta(int Sequence,Guid OldCenter,Guid NewCenter,
    decimal OldMinutesPerUnit,decimal NewMinutesPerUnit,decimal OldSetup,decimal NewSetup,
    decimal OldYield,decimal NewYield,string ChangeType);
public record FactoryEngineeringAffectedOrder(Guid OrderId,string Number,
    ManufacturingOrderStatus Status,DateOnly PlannedStart,Guid FrozenRevisionId,
    string Risk);
public record FactoryEngineeringImpact(Guid BaselineId,Guid CandidateId,string Kind,
    bool ReadyForRegularApproval,bool OverlappingEffectiveDates,
    IReadOnlyList<FactoryRevisionIssue> Issues,int AffectedOrders,
    int ActiveOrCompletedOrders,IReadOnlyList<FactoryEngineeringAffectedOrder> Orders,
    IReadOnlyList<FactoryEngineeringPartDelta> Parts,
    IReadOnlyList<FactoryEngineeringStepDelta> Operations,string Disclaimer);

public static class FactoryEngineeringChangeImpact
{
    public static IReadOnlyList<FactoryEngineeringPartDelta> CompareBom(
        ManufacturingBomRevision baseline,ManufacturingBomRevision proposal)
    {
        var left=baseline.Lines.ToDictionary(x=>x.ComponentItemId);
        var right=proposal.Lines.ToDictionary(x=>x.ComponentItemId);
        return left.Keys.Concat(right.Keys).Distinct().Select(key=>{
            left.TryGetValue(key,out var old);
            right.TryGetValue(key,out var next);
            return new FactoryEngineeringPartDelta(key,old?.QuantityPer??0,
                next?.QuantityPer??0,old?.ScrapPercent??0,next?.ScrapPercent??0,
                old is null?"Added":next is null?"Removed":"Changed");
        }).Where(x=>x.ChangeType!="Changed"||
            x.OldQuantity!=x.NewQuantity||x.OldScrapPercent!=x.NewScrapPercent)
            .OrderBy(x=>x.ComponentId).ToArray();
    }
    public static IReadOnlyList<FactoryEngineeringStepDelta> CompareRouting(
        RoutingRevision baseline,RoutingRevision proposal)
    {
        var left=baseline.Operations.ToDictionary(x=>x.Sequence);
        var right=proposal.Operations.ToDictionary(x=>x.Sequence);
        return left.Keys.Concat(right.Keys).Distinct().Select(seq=>{
            left.TryGetValue(seq,out var old);
            right.TryGetValue(seq,out var next);
            return new FactoryEngineeringStepDelta(seq,old?.WorkCenterId??Guid.Empty,
                next?.WorkCenterId??Guid.Empty,
                old?.RunMinutesPerUnit??0,next?.RunMinutesPerUnit??0,
                old?.SetupMinutes??0,next?.SetupMinutes??0,
                old?.YieldPercent??0,next?.YieldPercent??0,
                old is null?"Added":next is null?"Removed":"Changed");
        }).Where(x=>x.ChangeType!="Changed"||x.OldCenter!=x.NewCenter||
            x.OldMinutesPerUnit!=x.NewMinutesPerUnit||x.OldSetup!=x.NewSetup||
            x.OldYield!=x.NewYield).OrderBy(x=>x.Sequence).ToArray();
    }

    private static FactoryEngineeringImpact Impact(Guid baselineId,Guid candidateId,
        string kind,DateOnly baselineFrom,DateOnly? baselineTo,
        DateOnly candidateFrom,DateOnly? candidateTo,
        IReadOnlyList<FactoryRevisionIssue> issues,
        IReadOnlyList<ManufacturingOrder> frozenOrders,
        IReadOnlyList<FactoryEngineeringPartDelta> parts,
        IReadOnlyList<FactoryEngineeringStepDelta> steps)
    {
        var overlap=FactoryRevisionGovernance.Overlaps(baselineFrom,baselineTo,
            candidateFrom,candidateTo);
        var impacted=frozenOrders.Where(x=>x.PlannedStart>=candidateFrom||
            x.Status is ManufacturingOrderStatus.Released or
                ManufacturingOrderStatus.InProgress or ManufacturingOrderStatus.OnHold)
            .OrderBy(x=>x.PlannedStart).Take(501).Select(x=>
                new FactoryEngineeringAffectedOrder(x.Id,x.Number,x.Status,
                    x.PlannedStart,kind=="BOM"?x.BomRevisionId:x.RoutingRevisionId,
                    x.Status==ManufacturingOrderStatus.Draft?
                        "دستور پیش‌نویس نیازمند کنترل و تعیین تکلیف نسخه است.":
                        "دستور آزادشده/اجراشده، نسخه منجمد قبلی را حفظ می‌کند."))
            .ToArray();
        if(impacted.Length>500)
            throw new InvalidOperationException("تعداد دستورات تحت تأثیر بیش از ۵۰۰ است؛ فهرست ناقص ارائه نمی‌شود.");
        return new(baselineId,candidateId,kind,issues.Count==0,overlap,issues,
            impacted.Length,impacted.Count(x=>x.Status is ManufacturingOrderStatus.Released or
                ManufacturingOrderStatus.InProgress or ManufacturingOrderStatus.OnHold or
                ManufacturingOrderStatus.Completed),
            impacted,parts,steps,
            "فقط مقایسه پیشنهادی مهندسی است؛ تصویب ECO یا تغییر خودکار نسخه دستورات موجود نیست. بازه متداخل، مجاز به تصویب معمول نیست؛ نسخه مصوب و داده‌های تاریخی بازنویسی نمی‌شوند.");
    }
    public static FactoryEngineeringImpact Bom(
        ManufacturingBomRevision baseline,ManufacturingBomRevision candidate,
        IReadOnlyList<ManufacturingBomRevision> otherApproved,
        IReadOnlyList<ManufacturingOrder> orders)
    {
        if(baseline.Id==candidate.Id||baseline.ParentItemId!=candidate.ParentItemId||
            baseline.State!=ManufacturingRevisionState.Approved)
            throw new InvalidOperationException("باید دو نسخه متفاوت از یک محصول با مبنای BOM مصوب انتخاب شود.");
        var issues=FactoryRevisionGovernance.BomIssues(candidate,otherApproved);
        return Impact(baseline.Id,candidate.Id,"BOM",baseline.ValidFrom,baseline.ValidUntil,
            candidate.ValidFrom,candidate.ValidUntil,issues,
            orders.Where(x=>x.BomRevisionId==baseline.Id).ToArray(),
            CompareBom(baseline,candidate),[]);
    }
    public static FactoryEngineeringImpact Routing(
        RoutingRevision baseline,RoutingRevision candidate,
        IReadOnlyList<RoutingRevision> approved,
        IReadOnlyList<ManufacturingOrder> orders)
    {
        if(baseline.Id==candidate.Id||baseline.ItemId!=candidate.ItemId||
            baseline.State!=RoutingState.Approved)
            throw new InvalidOperationException("باید دو نسخه متفاوت از یک محصول با مبنای مسیر مصوب انتخاب شود.");
        var issues=FactoryRevisionGovernance.RoutingIssues(candidate,approved);
        return Impact(baseline.Id,candidate.Id,"Routing",baseline.EffectiveFrom,baseline.EffectiveTo,
            candidate.EffectiveFrom,candidate.EffectiveTo,issues,
            orders.Where(x=>x.RoutingRevisionId==baseline.Id).ToArray(),
            [],CompareRouting(baseline,candidate));
    }

    public static IEndpointRouteBuilder MapEngineeringChangeImpact(this IEndpointRouteBuilder routes)
    {
        var api=routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        api.MapGet("/factory-engineering/bom/{candidateId:guid}/impact",
            async(Guid candidateId,Guid baselineId,ProductionDb db,CancellationToken ct)=>{
                var rows=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                    .Where(x=>x.Id==candidateId||x.Id==baselineId).ToListAsync(ct);
                var before=rows.SingleOrDefault(x=>x.Id==baselineId);
                var candidate=rows.SingleOrDefault(x=>x.Id==candidateId);
                if(before is null||candidate is null)return Results.NotFound();
                var other=await db.ManufacturingBomRevisions.AsNoTracking().Include(x=>x.Lines)
                    .Where(x=>x.State==ManufacturingRevisionState.Approved).ToListAsync(ct);
                var orders=await db.ManufacturingOrders.AsNoTracking()
                    .Where(x=>x.BomRevisionId==baselineId).Take(501).ToListAsync(ct);
                if(orders.Count>500)return Results.Conflict(new{message="دستورات منجمد بیش از سقف بررسی است؛ تحلیل ناقص ارائه نمی‌شود."});
                try{return Results.Ok(Bom(before,candidate,other,orders));}
                catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            });
        api.MapGet("/factory-engineering/routing/{candidateId:guid}/impact",
            async(Guid candidateId,Guid baselineId,ProductionDb db,CancellationToken ct)=>{
                var rows=await db.RoutingRevisions.AsNoTracking().Include(x=>x.Operations)
                    .Where(x=>x.Id==candidateId||x.Id==baselineId).ToListAsync(ct);
                var before=rows.SingleOrDefault(x=>x.Id==baselineId);
                var candidate=rows.SingleOrDefault(x=>x.Id==candidateId);
                if(before is null||candidate is null)return Results.NotFound();
                var other=await db.RoutingRevisions.AsNoTracking()
                    .Where(x=>x.State==RoutingState.Approved&&x.ItemId==before.ItemId)
                    .ToListAsync(ct);
                var orders=await db.ManufacturingOrders.AsNoTracking()
                    .Where(x=>x.RoutingRevisionId==baselineId).Take(501).ToListAsync(ct);
                if(orders.Count>500)return Results.Conflict(new{message="دستورات منجمد بیش از سقف بررسی است؛ تحلیل ناقص ارائه نمی‌شود."});
                try{return Results.Ok(Routing(before,candidate,other,orders));}
                catch(InvalidOperationException ex){return Results.Conflict(new{message=ex.Message});}
            });
        return routes;
    }
}
