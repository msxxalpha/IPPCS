using System.Security.Cryptography;
using System.Text;

// Bridges net MRP proposals into APS without scheduling gross MPS demand.
// Only internally manufactured lots become operation chains; external component
// proposals remain explicit material-feasibility blockers.
public sealed record FactoryApsMaterialShortage(
    Guid ItemId,DateOnly NeededOn,decimal ProposedExternalQuantity,
    string DemandKey,string ParentLotKey);

public sealed record FactoryApsMrpBridgeResult(
    IReadOnlyList<MpsDemandLine> DemandLines,
    IReadOnlyList<NetPegPlannedLot> Lots,
    IReadOnlyDictionary<Guid,DateOnly> EarliestStartDates,
    IReadOnlyDictionary<Guid,IReadOnlyCollection<Guid>> Prerequisites,
    IReadOnlyList<FactoryApsMaterialShortage> MaterialShortages);

public static class FactoryApsMrpBridge
{
    public static FactoryApsMrpBridgeResult Build(NetPeggingReport report,DateOnly planningStart)
    {
        ArgumentNullException.ThrowIfNull(report);
        if(report.PlannedLots.Any(x=>string.IsNullOrWhiteSpace(x.LotKey)||
            x.ItemId==Guid.Empty||x.Quantity<=0||x.ReleaseOn>x.ReceiptOn)||
            report.PlannedLots.Select(x=>x.LotKey).Distinct(StringComparer.Ordinal).Count()!=report.PlannedLots.Count)
            throw new InvalidOperationException("بچ‌های پیشنهادی MRP برای اتصال به APS معتبر یا یکتا نیستند.");

        var lots=report.PlannedLots.Where(x=>x.ProposalType=="Manufacturing")
            .OrderBy(x=>x.ReceiptOn).ThenBy(x=>x.ItemId).ThenBy(x=>x.LotKey,StringComparer.Ordinal)
            .ToArray();
        var idByLot=lots.ToDictionary(x=>x.LotKey,x=>StableLotId(x.LotKey),StringComparer.Ordinal);
        if(idByLot.Values.Distinct().Count()!=idByLot.Count)
            throw new InvalidOperationException("شناسه پایدار بچ‌های پیشنهادی APS تکراری است.");

        var demandLines=lots.Select(lot=>new MpsDemandLine{
            Id=idByLot[lot.LotKey],ItemId=lot.ItemId,NeededOn=lot.ReceiptOn,
            Quantity=lot.Quantity,Reference=lot.LotKey
        }).ToArray();
        var earliest=lots.ToDictionary(x=>idByLot[x.LotKey],
            x=>x.ReleaseOn<planningStart?planningStart:x.ReleaseOn);

        var demandByKey=report.Demands.ToDictionary(x=>x.DemandKey,StringComparer.Ordinal);
        var prerequisites=new Dictionary<Guid,HashSet<Guid>>();
        foreach(var allocation in report.Allocations.Where(x=>
            x.SourceType=="ProposedManufacturingReceipt"))
        {
            if(!demandByKey.TryGetValue(allocation.DemandKey,out var demand)||
                demand.ParentLotKey is null||
                !idByLot.TryGetValue(demand.ParentLotKey,out var parentId)||
                !idByLot.TryGetValue(allocation.SourceKey,out var componentId))
                continue;
            if(parentId==componentId)
                throw new InvalidOperationException("وابستگی بچ‌های ساخت در MRP به چرخه رسیده است.");
            if(!prerequisites.TryGetValue(parentId,out var required))
                prerequisites[parentId]=required=new();
            required.Add(componentId);
        }

        var external=report.Allocations.Where(x=>x.SourceType=="ProposedExternalReceipt")
            .Where(x=>demandByKey.TryGetValue(x.DemandKey,out var demand)&&
                demand.ParentLotKey is not null)
            .Select(x=>{
                var demand=demandByKey[x.DemandKey];
                return new FactoryApsMaterialShortage(x.ItemId,x.NeededOn,x.Quantity,
                    x.DemandKey,demand.ParentLotKey!);
            }).OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId).ThenBy(x=>x.DemandKey,StringComparer.Ordinal)
            .ToArray();

        var prerequisiteView=prerequisites.ToDictionary(x=>x.Key,
            x=>(IReadOnlyCollection<Guid>)x.Value.OrderBy(id=>id).ToArray());
        return new(demandLines,lots,earliest,prerequisiteView,external);
    }

    private static Guid StableLotId(string lotKey)
    {
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(lotKey));
        return new Guid(hash.AsSpan(0,16));
    }
}
