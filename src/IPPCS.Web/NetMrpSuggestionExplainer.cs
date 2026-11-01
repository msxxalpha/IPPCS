// Read-only audit reconciliation of the existing net MRP suggested-order rows.
// Explains net shortage, batch uplift, lead-time offset and suggested receipts.
// This intentionally does not assign manufactured supply to a particular MPS path.
public record MrpSuggestionExplanation(Guid ItemId,DateOnly NeedDate,decimal GrossDemand,
    decimal AvailableBeforeDemand,decimal NetShortage,decimal SuggestedReceipt,
    decimal LotSizingSurplus,DateOnly? ProposedReleaseDate,decimal EndingProjectedBalance,
    bool ReleaseBeforePlanningBaseline,string? Warning);

public static class NetMrpSuggestionExplainer
{
    public static IReadOnlyList<MrpSuggestionExplanation> Explain(
        IReadOnlyList<MrpRow> rows,IReadOnlyList<PlanningBalance> balances)
    {
        var baselines=balances.ToDictionary(x=>x.ItemId);
        var result=new List<MrpSuggestionExplanation>();
        foreach(var row in rows.OrderBy(x=>x.NeededOn).ThenBy(x=>x.ItemId))
        {
            if(!baselines.TryGetValue(row.ItemId,out var baseline))
                throw new InvalidOperationException("فرضیات موجودی برای ردیف پیشنهاد MRP موجود نیست.");
            if(row.GrossRequired<0||row.NetShortage<0||row.SuggestedQuantity<0||
                row.OpeningAvailable<0||baseline.LeadDays<0)
                throw new InvalidOperationException("مقادیر پیشنهاد MRP معتبر نیست.");
            var shortage=Math.Max(0m,row.GrossRequired-row.OpeningAvailable);
            if(shortage!=row.NetShortage)
                throw new InvalidOperationException("میزان کسری محاسبه MRP با موجودی قبل از نیاز سازگار نیست.");
            if(row.SuggestedQuantity<row.NetShortage)
                throw new InvalidOperationException("مقدار پیشنهاد ساخت از کسری خالص کمتر است.");
            if(row.ClosingAvailable!=row.OpeningAvailable+row.SuggestedQuantity-row.GrossRequired)
                throw new InvalidOperationException("مانده پایان ردیف MRP با جریان موجودی سازگار نیست.");
            DateOnly? release=row.SuggestedQuantity>0?row.NeededOn.AddDays(-baseline.LeadDays):null;
            if(release!=row.SuggestedRelease)
                throw new InvalidOperationException("تاریخ آزادسازی MRP با زمان تأمین قطعه تطابق ندارد.");
            var late=release.HasValue&&release.Value<baseline.SnapshotDate;
            result.Add(new(row.ItemId,row.NeededOn,row.GrossRequired,row.OpeningAvailable,
                row.NetShortage,row.SuggestedQuantity,row.SuggestedQuantity-row.NetShortage,
                release,row.ClosingAvailable,late,late?
                "تاریخ پیشنهادی آزادسازی پیش از تاریخ مبنای برنامه است؛ نیازمند تصمیم برنامه‌ریز.":null));
        }
        return result;
    }
}
