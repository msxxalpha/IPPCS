// Conservative material-feasibility gate for APS. Inputs are IPPCS-only
// manually entered planning balances and expected receipts, never warehouse transactions.
// This is intentionally a safety gate, not a substitute for time-phased MRP pegging.
public record MaterialGateResult(Guid DemandLineId,Guid ItemId,bool Feasible,
    IReadOnlyList<string> Reasons);

public static class MaterialConstrainedPlanning
{
    public static IReadOnlyList<MaterialGateResult> Check(
        IReadOnlyList<MpsDemandLine> lines,
        IReadOnlyList<ManufacturingBomRevision> revisions,
        IReadOnlyList<PlanningBalance> balances,
        IReadOnlyList<PlanningExpectedReceipt> receipts,
        IReadOnlyDictionary<Guid,PlanningItemType> itemTypes,
        DateOnly horizonStart)
    {
        if (lines is null || revisions is null || balances is null || receipts is null || itemTypes is null)
            throw new ArgumentNullException("ورودی‌های کنترل مواد نمی‌توانند تهی باشند.");
        if (balances.GroupBy(x => x.ItemId).Any(g => g.Count() > 1))
            throw new InvalidOperationException("برای هر قلم فقط یک موجودی مبنای برنامه‌ریزی مجاز است.");
        if (receipts.Any(x => x.Quantity <= 0))
            throw new InvalidOperationException("مقدار دریافت برنامه‌ای باید مثبت باشد.");

        var byBalance=balances.ToDictionary(x=>x.ItemId);
        var approved=revisions.Where(x=>x.State==ManufacturingRevisionState.Approved).ToList();
        var cumulativeDemand=new Dictionary<Guid,decimal>();
        var results=new List<MaterialGateResult>();
        var ordered=lines.OrderBy(x=>x.NeededOn).ThenBy(x=>x.Id).ToList();
        foreach(var line in ordered)
        {
            var causes=new List<string>();
            var structureValid=true;
            var requirements=new Dictionary<Guid,decimal>();
            void Expand(Guid item,decimal qty,HashSet<Guid> stack)
            {
                if(!stack.Add(item))
                {
                    causes.Add("چرخه ساختار مواد شناسایی شد.");
                    structureValid=false;
                    return;
                }
                var kind=itemTypes.GetValueOrDefault(item);
                if(!itemTypes.ContainsKey(item))
                {
                    causes.Add("اطلاعات پایه یک قطعه وجود ندارد.");
                    structureValid=false;
                    stack.Remove(item);return;
                }
                // Only raw materials have assumed exogenous supply; semi-finished items
                // must have a valid approved BOM to be expanded. No stock transfer occurs.
                if(kind==PlanningItemType.RawMaterial)
                {
                    requirements[item]=requirements.GetValueOrDefault(item)+qty;
                    stack.Remove(item);return;
                }
                var found=approved.Where(x=>x.ParentItemId==item &&
                    x.ValidFrom<=horizonStart &&
                    (x.ValidUntil==null||horizonStart<=x.ValidUntil.Value)).ToList();
                if(found.Count!=1||found[0].Lines.Count==0)
                {
                    causes.Add("BOM مصوب معتبر برای کالای ساختنی موجود نیست.");
                    structureValid=false;
                    stack.Remove(item);return;
                }
                foreach(var component in found[0].Lines)
                {
                    if(component.QuantityPer<=0||component.ScrapPercent is <0 or >=100)
                    {
                        causes.Add("مقادیر BOM معتبر نیست.");
                        structureValid=false;
                        continue;
                    }
                    Expand(component.ComponentItemId,
                        qty*component.QuantityPer/(1-component.ScrapPercent/100m),stack);
                }
                stack.Remove(item);
            }
            if(line.Quantity<=0)
            {
                causes.Add("مقدار تقاضا نامعتبر است.");
                structureValid=false;
            }
            else Expand(line.ItemId,line.Quantity,new HashSet<Guid>());
            foreach(var required in requirements.OrderBy(x=>x.Key))
            {
                if(!byBalance.TryGetValue(required.Key,out var bal))
                {
                    causes.Add("موجودی مبنای یکی از مواد اولیه ثبت نشده است.");
                    continue;
                }
                if(bal.SnapshotDate>horizonStart || bal.SnapshotDate>line.NeededOn)
                {
                    causes.Add("تاریخ مبنای موجودی از تاریخ برنامه جدیدتر است.");
                    continue;
                }
                if(bal.PhysicalQuantity<0||bal.ReservedQuantity<0||
                    bal.QuarantinedQuantity<0||bal.SafetyQuantity<0)
                {
                    causes.Add("موجودی یا اقلام قرنطینه نامعتبر است.");continue;
                }
                decimal usable=Math.Max(0,bal.PhysicalQuantity-bal.ReservedQuantity-
                    bal.QuarantinedQuantity-bal.SafetyQuantity);
                usable+=receipts.Where(x=>x.ItemId==required.Key &&
                    x.AvailableOn>bal.SnapshotDate &&
                    x.AvailableOn<=horizonStart).Sum(x=>x.Quantity);
                var committed=cumulativeDemand.GetValueOrDefault(required.Key);
                if(usable-committed<required.Value)
                    causes.Add("ماده اولیه کافی در تاریخ شروع افق موجود نیست.");
            }

            // Preserve demand priority even when this line is itself infeasible.
            // Otherwise its shortfall would leave the same stock available to a later
            // line, producing a false-positive APS material gate.
            if(structureValid)
                foreach(var r in requirements)
                    cumulativeDemand[r.Key]=cumulativeDemand.GetValueOrDefault(r.Key)+r.Value;
            results.Add(new MaterialGateResult(line.Id,line.ItemId,causes.Count==0,
                causes.Distinct().ToArray()));
        }
        return results;
    }
}