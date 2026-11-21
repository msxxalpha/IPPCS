using Microsoft.EntityFrameworkCore;
using System.Text.Json;

// Read-only provenance graph for net MRP. All supply is an IPPCS planning assumption;
// planned receipts are proposals, never confirmed warehouse or purchasing transactions.
public record NetPegDemand(string DemandKey, Guid ItemId, DateOnly NeededOn, decimal Quantity,
    Guid? MpsLineId, string? ParentLotKey, Guid? BomRevisionId, string Path, int Level);
public record NetPegAllocation(string DemandKey, Guid ItemId, DateOnly NeededOn,
    string SourceType, string SourceKey, DateOnly AvailableOn, decimal Quantity, bool IsFirm);
public record NetPegPlannedLot(string LotKey, Guid ItemId, DateOnly ReceiptOn, DateOnly ReleaseOn,
    decimal Quantity, decimal NetShortage, decimal LotSizingSurplus, string ProposalType,
    Guid? BomRevisionId, bool ReleaseBeforeBaseline, decimal UnallocatedQuantity);
public record NetPeggingReport(IReadOnlyList<NetPegDemand> Demands,
    IReadOnlyList<NetPegAllocation> Allocations, IReadOnlyList<NetPegPlannedLot> PlannedLots);


public sealed class NetMrpMpsSnapshot
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public PlanningApproval State { get; set; }
    public List<NetMrpMpsLineSnapshot> Lines { get; set; } = new();
}
public sealed class NetMrpMpsLineSnapshot
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public DateOnly NeededOn { get; set; }
    public decimal Quantity { get; set; }
    public string Reference { get; set; } = "";
}
public sealed class NetMrpBomSnapshot
{
    public Guid Id { get; set; }
    public Guid ParentItemId { get; set; }
    public string Revision { get; set; } = "";
    public ManufacturingRevisionState State { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public List<NetMrpBomLineSnapshot> Lines { get; set; } = new();
}
public sealed class NetMrpBomLineSnapshot
{
    public Guid Id { get; set; }
    public Guid ComponentItemId { get; set; }
    public decimal QuantityPer { get; set; }
    public decimal ScrapPercent { get; set; }
    public int Sequence { get; set; }
    public string? AlternativeGroupCode { get; set; }
    public bool IsDefaultAlternative { get; set; }
    public int AlternativePriority { get; set; }
}
public sealed class NetMrpRunInputSnapshot
{
    public NetMrpMpsSnapshot Mps { get; set; } = new();
    public List<NetMrpBomSnapshot> Bom { get; set; } = new();
    public List<PlanningBalance> Balances { get; set; } = new();
    public List<PlanningExpectedReceipt> Receipts { get; set; } = new();
    public Dictionary<Guid, PlanningItemType> Types { get; set; } = new();
}

public static class NetMrpDemandPegging
{
    private sealed class Source(DateOnly availableOn, string type, string key, decimal quantity, bool firm)
    {
        public DateOnly AvailableOn { get; } = availableOn;
        public string Type { get; } = type;
        public string Key { get; } = key;
        public bool Firm { get; } = firm;
        public decimal Remaining { get; set; } = quantity;
    }

    public static NetPeggingReport Build(
        IReadOnlyList<MpsDemandLine> mpsLines,
        IReadOnlyList<ManufacturingBomRevision> boms,
        IReadOnlyList<PlanningBalance> balances,
        IReadOnlyList<PlanningExpectedReceipt> receipts,
        IReadOnlyDictionary<Guid, PlanningItemType> types,
        IReadOnlyList<MrpRow> mrpRows)
    {
        if (mpsLines.Any(x => x.Id == Guid.Empty || x.ItemId == Guid.Empty || x.Quantity <= 0) ||
            mpsLines.Select(x => x.Id).Distinct().Count() != mpsLines.Count)
            throw new InvalidOperationException("ردیف‌های MPS باید شناسه یکتا، قلم معتبر و مقدار مثبت داشته باشند.");
        if (balances.Any(x => x.ItemId == Guid.Empty) ||
            balances.Select(x => x.ItemId).Distinct().Count() != balances.Count)
            throw new InvalidOperationException("برای هر قلم دقیقاً یک رکورد مبنای برنامه‌ریزی لازم است.");
        if (receipts.Any(x => x.Id == Guid.Empty || x.ItemId == Guid.Empty || x.Quantity <= 0) ||
            receipts.Select(x => x.Id).Distinct().Count() != receipts.Count)
            throw new InvalidOperationException("دریافت‌های برنامه‌ای باید شناسه یکتا، قلم معتبر و مقدار مثبت داشته باشند.");
        if (mrpRows.Select(x => (x.ItemId, x.NeededOn)).Distinct().Count() != mrpRows.Count)
            throw new InvalidOperationException("برای هر قلم و تاریخ فقط یک ردیف MRP قابل پذیرش است.");

        var approved = boms.Where(b => b.State == ManufacturingRevisionState.Approved)
            .GroupBy(b => b.ParentItemId).ToDictionary(g => g.Key, g => g.ToList());
        var depth = new Dictionary<Guid, int>();
        void Visit(Guid item, int level, HashSet<Guid> stack)
        {
            if (level > 50 || !stack.Add(item))
                throw new InvalidOperationException("چرخه یا عمق غیرمجاز BOM شناسایی شد.");
            if (!types.ContainsKey(item))
                throw new InvalidOperationException("اطلاعات پایه قطعه در محاسبات موجود نیست.");
            depth[item] = Math.Max(depth.GetValueOrDefault(item), level);
            if (approved.TryGetValue(item, out var revisions))
                foreach (var line in revisions.SelectMany(PlanningCalculators.EffectiveDemandLines))
                    Visit(line.ComponentItemId, level + 1, stack);
            stack.Remove(item);
        }
        foreach (var item in mpsLines.Select(x => x.ItemId).Distinct())
            Visit(item, 0, new HashSet<Guid>());

        var demands = mpsLines.OrderBy(x => x.NeededOn).ThenBy(x => x.Id)
            .Select(x =>
            {
                if (x.Quantity <= 0) throw new InvalidOperationException("تعداد MPS باید مثبت باشد.");
                return new NetPegDemand("MPS:" + x.Id.ToString("N"), x.ItemId,
                    x.NeededOn, x.Quantity, x.Id, null, null, x.ItemId.ToString("N"), 0);
            }).ToList();
        var allocations = new List<NetPegAllocation>();
        var lotRecords = new List<(NetPegPlannedLot Lot, Source Source)>();
        var balanceMap = balances.ToDictionary(x => x.ItemId);
        var receiptMap = receipts.GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.AvailableOn).ThenBy(x => x.Id).ToList());
        var rowMap = mrpRows.ToDictionary(x => (x.ItemId, x.NeededOn));
        var visited = new HashSet<(Guid, DateOnly)>();

        foreach (var item in depth.OrderBy(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key))
        {
            if (!balanceMap.TryGetValue(item, out var balance))
                throw new InvalidOperationException("برای یکی از اقلام، موجودی مبنای برنامه‌ریزی تعریف نشده است.");
            if (balance.PhysicalQuantity < 0 || balance.ReservedQuantity < 0 ||
                balance.QuarantinedQuantity < 0 || balance.SafetyQuantity < 0 ||
                balance.LeadDays < 0 || balance.MinimumBatch <= 0 || balance.BatchMultiple <= 0)
                throw new InvalidOperationException("پارامترهای موجودی، بچ یا زمان تأمین نامعتبر است.");
            var usable = Math.Max(0m, balance.PhysicalQuantity - balance.ReservedQuantity -
                balance.QuarantinedQuantity - balance.SafetyQuantity);
            var supplies = new List<Source>
            {
                new(balance.SnapshotDate, "PlanningOpeningBalance",
                    "BASE:" + item.ToString("N"), usable, true)
            };
            if (receiptMap.TryGetValue(item, out var itemReceipts))
                foreach (var receipt in itemReceipts)
                {
                    if (receipt.Quantity <= 0 || receipt.AvailableOn <= balance.SnapshotDate)
                        throw new InvalidOperationException("مقدار یا تاریخ دریافت برنامه‌ای نامعتبر است.");
                    supplies.Add(new(receipt.AvailableOn, "ExpectedReceipt",
                        receipt.Id.ToString("N"), receipt.Quantity, true));
                }

            var groups = demands.Where(x => x.ItemId == item)
                .GroupBy(x => x.NeededOn).OrderBy(x => x.Key).ToArray();
            foreach (var group in groups)
            {
                var date = group.Key;
                if (date < balance.SnapshotDate)
                    throw new InvalidOperationException("نیاز اجزای ساخت قبل از تاریخ مبنای برنامه ایجاد شده است؛ تاریخ آزادسازی را اصلاح کنید.");
                var key = (item, date);
                if (!rowMap.TryGetValue(key, out var row))
                    throw new InvalidOperationException("نیاز BOM با محاسبه MRP تطابق ندارد.");
                visited.Add(key);
                var gross = group.Sum(x => x.Quantity);
                // Capture supply before creating this date's own proposal. A proposal
                // generated to cover this row must not inflate the opening balance
                // used to validate its own net shortage.
                var before = supplies.Where(x => x.AvailableOn <= date).Sum(x => x.Remaining);
                if (row.GrossRequired != gross || row.OpeningAvailable != before ||
                    row.NetShortage != Math.Max(0, gross - before) ||
                    row.ClosingAvailable != before + row.SuggestedQuantity - gross ||
                    row.SuggestedQuantity < row.NetShortage ||
                    (row.SuggestedQuantity > 0) != row.SuggestedRelease.HasValue)
                    throw new InvalidOperationException("نتایج MRP با منشأ تخصیص خالص همخوانی ندارند.");

                if (row.SuggestedQuantity > 0)
                {
                    var release = row.SuggestedRelease!.Value;
                    if (release != date.AddDays(-balance.LeadDays))
                        throw new InvalidOperationException("تاریخ آزادسازی پیشنهاد MRP صحیح نیست.");
                    var lotKey = "PLAN:" + item.ToString("N") + ":" + date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    var manufactured = types[item] is PlanningItemType.Finished or PlanningItemType.SemiFinished;
                    ManufacturingBomRevision? revision = null;
                    if (manufactured)
                    {
                        var matching = approved.GetValueOrDefault(item)?.Where(x =>
                            x.ValidFrom <= release &&
                            (x.ValidUntil is null || release <= x.ValidUntil.Value)).ToArray()
                            ?? Array.Empty<ManufacturingBomRevision>();
                        if (matching.Length != 1 || matching[0].Lines.Count == 0)
                            throw new InvalidOperationException("برای تاریخ شروع پیشنهاد ساخت دقیقاً یک BOM مصوب معتبر لازم است.");
                        revision = matching[0];
                    }
                    var source = new Source(date,
                        manufactured ? "ProposedManufacturingReceipt" : "ProposedExternalReceipt",
                        lotKey, row.SuggestedQuantity, false);
                    supplies.Add(source);
                    var lot = new NetPegPlannedLot(lotKey, item, date, release,
                        row.SuggestedQuantity, row.NetShortage, row.SuggestedQuantity - row.NetShortage,
                        manufactured ? "Manufacturing" : "ExternalPlanningOnly", revision?.Id,
                        release < balance.SnapshotDate, 0);
                    lotRecords.Add((lot, source));

                    // Only net planned manufacturing orders explode into lower-level BOM demand.
                    // The entire manufactured batch, including lot-size uplift, consumes components.
                    if (revision is not null)
                        foreach (var part in PlanningCalculators.EffectiveDemandLines(revision).OrderBy(x => x.Sequence).ThenBy(x => x.Id))
                        {
                            if (part.QuantityPer <= 0 || part.ScrapPercent is < 0 or >= 100)
                                throw new InvalidOperationException("ضریب مصرف یا درصد ضایعات BOM نامعتبر است.");
                            var componentQty = row.SuggestedQuantity * part.QuantityPer /
                                (1 - part.ScrapPercent / 100m);
                            demands.Add(new NetPegDemand("COMP:" + lotKey + ":" + part.Id.ToString("N"),
                                part.ComponentItemId, release, componentQty, null, lotKey,
                                revision.Id, lotKey + "/" + part.ComponentItemId.ToString("N"),
                                depth[item] + 1));
                        }
                }
                foreach (var need in group.OrderBy(x => x.DemandKey, StringComparer.Ordinal))
                {
                    var left = need.Quantity;
                    foreach (var supply in supplies.Where(x => x.AvailableOn <= date && x.Remaining > 0)
                        .OrderBy(x => x.AvailableOn)
                        .ThenBy(x => x.Firm ? 0 : 1)
                        .ThenBy(x => x.Key, StringComparer.Ordinal))
                    {
                        if (left <= 0) break;
                        var amount = Math.Min(left, supply.Remaining);
                        if (amount <= 0) continue;
                        allocations.Add(new NetPegAllocation(need.DemandKey, item, date,
                            supply.Type, supply.Key, supply.AvailableOn, amount, supply.Firm));
                        supply.Remaining -= amount;
                        left -= amount;
                    }
                    if (left != 0)
                        throw new InvalidOperationException("تخصیص یک نیاز خالص ناقص باقی مانده است.");
                }
                if (supplies.Where(x => x.AvailableOn <= date).Sum(x => x.Remaining) != row.ClosingAvailable)
                    throw new InvalidOperationException("مانده عرضه تخصیص داده‌شده با محاسبه MRP تطابق ندارد.");
            }
        }
        if (visited.Count != mrpRows.Count)
            throw new InvalidOperationException("تعداد ردیف‌های MRP با ردگیری نیازها یکسان نیست.");

        return new NetPeggingReport(
            demands.OrderBy(x => x.NeededOn).ThenBy(x => x.ItemId)
                .ThenBy(x => x.DemandKey, StringComparer.Ordinal).ToArray(),
            allocations.OrderBy(x => x.NeededOn).ThenBy(x => x.ItemId)
                .ThenBy(x => x.DemandKey, StringComparer.Ordinal)
                .ThenBy(x => x.SourceKey, StringComparer.Ordinal).ToArray(),
            lotRecords.Select(x => x.Lot with { UnallocatedQuantity = x.Source.Remaining })
                .OrderBy(x => x.ReceiptOn).ThenBy(x => x.ItemId).ToArray());
    }


    /// <summary>
    /// Rebuilds pegging strictly from the immutable input/result snapshot of one
    /// saved MRP run. Later edits to live BOM, balances or receipts cannot alter
    /// the evidence associated with that run.
    /// </summary>
    public sealed record RestoredRunSnapshot(MpsVersion Mps,
        IReadOnlyList<ManufacturingBomRevision> Boms,
        IReadOnlyList<PlanningBalance> Balances,
        IReadOnlyList<PlanningExpectedReceipt> Receipts,
        IReadOnlyDictionary<Guid,PlanningItemType> Types,
        IReadOnlyList<MrpRow> MrpRows);

    public static bool TryRestoreRunSnapshot(PlanningCalculation run,
        out RestoredRunSnapshot? restored,out string error)
    {
        restored=null;
        error="";
        if(run.Id==Guid.Empty||run.MpsVersionId==Guid.Empty||
            run.AlgorithmVersion!="MRP-PREVIEW-1")
        {error="نسخه محاسبات MRP برای بازسازی تصویر ورودی پشتیبانی نمی‌شود.";return false;}
        try
        {
            var snapshot=JsonSerializer.Deserialize<NetMrpRunInputSnapshot>(
                run.InputSnapshotJson,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
            var rows=JsonSerializer.Deserialize<List<MrpRow>>(run.ResultSnapshotJson);
            if(snapshot is null||rows is null||snapshot.Mps.Id!=run.MpsVersionId||
                snapshot.Mps.State!=PlanningApproval.Approved||
                snapshot.Mps.Lines.Count==0||snapshot.Types.Count==0)
            {error="تصویر ورودی یا نتیجه اجرای MRP ناقص یا ناسازگار است.";return false;}
            var mps=new MpsVersion{Id=snapshot.Mps.Id,Code=snapshot.Mps.Code,
                State=snapshot.Mps.State};
            mps.Lines=snapshot.Mps.Lines.Select(x=>new MpsDemandLine{
                Id=x.Id,MpsVersionId=mps.Id,Version=mps,ItemId=x.ItemId,
                NeededOn=x.NeededOn,Quantity=x.Quantity,Reference=x.Reference
            }).ToList();
            var boms=snapshot.Bom.Select(x=>{
                var revision=new ManufacturingBomRevision{
                    Id=x.Id,ParentItemId=x.ParentItemId,Revision=x.Revision,
                    State=x.State,ValidFrom=x.ValidFrom,ValidUntil=x.ValidUntil
                };
                revision.Lines=x.Lines.Select(line=>new ManufacturingBomLine{
                    Id=line.Id,RevisionId=x.Id,Revision=revision,
                    ComponentItemId=line.ComponentItemId,QuantityPer=line.QuantityPer,
                    ScrapPercent=line.ScrapPercent,Sequence=line.Sequence,
                    AlternativeGroupCode=line.AlternativeGroupCode,
                    IsDefaultAlternative=line.IsDefaultAlternative,
                    AlternativePriority=line.AlternativePriority
                }).ToList();
                return revision;
            }).ToArray();
            restored=new RestoredRunSnapshot(mps,boms,snapshot.Balances,
                snapshot.Receipts,snapshot.Types,rows);
            return true;
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or
            ArgumentException or OverflowException or KeyNotFoundException)
        {
            error="بازسازی تصویر ذخیره‌شده MRP ممکن نیست: "+ex.Message;
            return false;
        }
    }

    public static bool TryBuildFromRunSnapshot(PlanningCalculation run,
        out NetPeggingReport report,out string error)
    {
        report=new NetPeggingReport([],[],[]);
        if(!TryRestoreRunSnapshot(run,out var restored,out error)||restored is null)
            return false;
        try
        {
            report=Build(restored.Mps.Lines,restored.Boms,restored.Balances,
                restored.Receipts,restored.Types,restored.MrpRows);
            return true;
        }
        catch(Exception ex) when(ex is InvalidOperationException or
            ArgumentException or OverflowException or KeyNotFoundException)
        {
            error="اعتبارسنجی گراف اجرای ذخیره‌شده MRP ناموفق بود: "+ex.Message;
            return false;
        }
    }

    public static IEndpointRouteBuilder MapNetMrpDemandPegging(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");

        api.MapGet("/mrp/{id:guid}/runs",async(Guid id,ProductionDb db,CancellationToken ct)=>{
            if(!await db.MpsVersions.AsNoTracking().AnyAsync(x=>x.Id==id,ct))
                return Results.NotFound();
            var runs=await db.PlanningCalculations.AsNoTracking()
                .Where(x=>x.MpsVersionId==id&&x.AlgorithmVersion=="MRP-PREVIEW-1")
                .OrderByDescending(x=>x.CalculatedUtc).Take(50)
                .Select(x=>new{x.Id,x.MpsVersionId,x.CalculatedUtc,x.AlgorithmVersion})
                .ToListAsync(ct);
            return Results.Ok(runs);
        });
        api.MapGet("/mrp/runs/{runId:guid}/net-demand-pegging",
            async(Guid runId,ProductionDb db,CancellationToken ct)=>{
            var run=await db.PlanningCalculations.AsNoTracking()
                .SingleOrDefaultAsync(x=>x.Id==runId,ct);
            if(run is null)return Results.NotFound();
            if(!TryBuildFromRunSnapshot(run,out var traced,out var error))
                return Results.Conflict(new{message=error});
            return Results.Ok(new{
                kind="ImmutableMrpRunNetDemandPegging",
                runId=run.Id,mpsVersionId=run.MpsVersionId,
                calculatedUtc=run.CalculatedUtc,algorithm=run.AlgorithmVersion,
                warning="این ردگیری دقیقاً از تصویر ورودی و نتیجه همان اجرای ذخیره‌شده MRP بازسازی شده است؛ تغییرات بعدی اطلاعات پایه، BOM، موجودی برنامه‌ریزی یا دریافت‌ها در آن اعمال نمی‌شود. پیشنهادها همچنان سفارش قطعی یا رزرو انبار نیستند.",
                demands=traced.Demands.Select(x=>new{x.DemandKey,x.ItemId,
                    neededOnJalali=PersianPlanningDates.Format(x.NeededOn),
                    x.Quantity,x.MpsLineId,x.ParentLotKey,x.BomRevisionId,x.Path,x.Level}),
                allocations=traced.Allocations.Select(x=>new{x.DemandKey,x.ItemId,
                    neededOnJalali=PersianPlanningDates.Format(x.NeededOn),
                    x.SourceType,x.SourceKey,x.IsFirm,
                    availableOnJalali=PersianPlanningDates.Format(x.AvailableOn),x.Quantity}),
                plannedLots=traced.PlannedLots.Select(x=>new{x.LotKey,x.ItemId,
                    receiptOnJalali=PersianPlanningDates.Format(x.ReceiptOn),
                    releaseOnJalali=PersianPlanningDates.Format(x.ReleaseOn),
                    x.Quantity,x.NetShortage,x.LotSizingSurplus,x.ProposalType,
                    x.BomRevisionId,x.ReleaseBeforeBaseline,x.UnallocatedQuantity})
            });
        });
        api.MapGet("/mrp/{id:guid}/net-demand-pegging", async (Guid id, ProductionDb db, CancellationToken ct) =>
        {
            var mps = await db.MpsVersions.AsNoTracking().Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            if (mps is null) return Results.NotFound();
            if (mps.State != PlanningApproval.Approved)
                return Results.Conflict(new { message = "ردگیری MRP صرفاً برای MPS مصوب انجام می‌شود." });
            var boms = await db.ManufacturingBomRevisions.AsNoTracking().Include(x => x.Lines).ToListAsync(ct);
            var balances = await db.PlanningBalances.AsNoTracking().ToListAsync(ct);
            var receipts = await db.PlanningExpectedReceipts.AsNoTracking().ToListAsync(ct);
            var types = await db.Items.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Type, ct);
            if (balances.Count > 0 && balances.Any(x => x.SnapshotDate != balances[0].SnapshotDate))
                return Results.Conflict(new { message = "تاریخ مبنای موجودی‌ها یکسان نیست." });
            if (balances.Count > 0 && receipts.Any(x => x.AvailableOn <= balances[0].SnapshotDate))
                return Results.Conflict(new { message = "دریافت برنامه‌ای تکراری نسبت به مبنای موجودی ثبت شده است." });
            try
            {
                var rows = PlanningCalculators.RunMrp(mps.Lines, boms, balances, receipts, types);
                var traced = Build(mps.Lines, boms, balances, receipts, types, rows);
                return Results.Ok(new
                {
                    kind = "TimePhasedNetMrpProposalGraph",
                    warning = "ردیابی خالص پیشنهادهای MRP صرفاً شبیه‌سازی است. پیشنهاد ساخت/تأمین، موجودی قطعی یا سفارش تأییدشده نیست. مسیر lot و اجزای BOM قابل پیگیری است.",
                    demands = traced.Demands.Select(x => new { x.DemandKey, x.ItemId,
                        neededOnJalali = PersianPlanningDates.Format(x.NeededOn),
                        x.Quantity, x.MpsLineId, x.ParentLotKey, x.BomRevisionId, x.Path, x.Level }),
                    allocations = traced.Allocations.Select(x => new { x.DemandKey, x.ItemId,
                        neededOnJalali = PersianPlanningDates.Format(x.NeededOn),
                        x.SourceType, x.SourceKey, x.IsFirm,
                        availableOnJalali = PersianPlanningDates.Format(x.AvailableOn), x.Quantity }),
                    plannedLots = traced.PlannedLots.Select(x => new { x.LotKey, x.ItemId,
                        receiptOnJalali = PersianPlanningDates.Format(x.ReceiptOn),
                        releaseOnJalali = PersianPlanningDates.Format(x.ReleaseOn),
                        x.Quantity, x.NetShortage, x.LotSizingSurplus, x.ProposalType,
                        x.BomRevisionId, x.ReleaseBeforeBaseline, x.UnallocatedQuantity })
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or OverflowException or ArgumentOutOfRangeException)
            {
                return Results.UnprocessableEntity(new { message = ex.Message });
            }
        });
        return routes;
    }
}
