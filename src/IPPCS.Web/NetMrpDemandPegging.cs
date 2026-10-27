using Microsoft.EntityFrameworkCore;

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
                foreach (var line in revisions.SelectMany(x => x.Lines))
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
                        foreach (var part in revision.Lines.OrderBy(x => x.Sequence).ThenBy(x => x.Id))
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

    public static IEndpointRouteBuilder MapNetMrpDemandPegging(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
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
