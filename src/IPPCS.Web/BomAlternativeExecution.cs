using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Order-specific authorized component substitutions and local usage evidence.
// This records manufacturing intent/actual use only; it never posts warehouse stock.
public sealed class ManufacturingOrderMaterialAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid BomLineId { get; set; }
    public decimal SharePercent { get; set; }
    public decimal PlannedQuantity { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public record ManufacturingOrderMaterialShareInput(Guid BomLineId, decimal SharePercent);
public record ManufacturingOrderMaterialGroupInput(string GroupCode, IReadOnlyList<ManufacturingOrderMaterialShareInput> Shares);
public record ManufacturingOrderMaterialAllocationInput(IReadOnlyList<ManufacturingOrderMaterialGroupInput> Groups);

public static class BomAlternativeExecution
{
    public static void ConfigureModel(ModelBuilder model)
    {
        var allocation=model.Entity<ManufacturingOrderMaterialAllocation>();
        allocation.ToTable("ManufacturingOrderMaterialAllocations",table =>
        {
            table.HasCheckConstraint("CK_ManufacturingOrderMaterialAllocations_SharePercent",
                "[SharePercent] > 0 AND [SharePercent] <= 100");
            table.HasCheckConstraint("CK_ManufacturingOrderMaterialAllocations_PlannedQuantity",
                "[PlannedQuantity] > 0");
        });
        allocation.Property(x=>x.SharePercent).HasPrecision(8,4);
        allocation.Property(x=>x.PlannedQuantity).HasPrecision(18,4);
        allocation.HasIndex(x=>x.BomLineId);
        allocation.HasIndex(x=>new{x.OrderId,x.BomLineId}).IsUnique();
        allocation.HasOne<ManufacturingBomLine>().WithMany()
            .HasForeignKey(x=>x.BomLineId).OnDelete(DeleteBehavior.Restrict);
        allocation.HasOne<ManufacturingOrder>().WithMany()
            .HasForeignKey(x=>x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }

    public static decimal CalculatePlannedComponentQuantity(
        decimal orderQuantity, ManufacturingBomLine line, decimal? sharePercent = null)
    {
        if (orderQuantity <= 0 || line.QuantityPer <= 0 || line.ScrapPercent is < 0 or >= 100 ||
            (sharePercent.HasValue && sharePercent.Value is <= 0 or > 100))
            throw new InvalidOperationException("مقدار دستور، ضریب مصرف، ضایعات یا سهم آلترناتیو نامعتبر است.");
        var quantity = orderQuantity * line.QuantityPer / (1m - line.ScrapPercent / 100m);
        return sharePercent.HasValue ? quantity * sharePercent.Value / 100m : quantity;
    }

    public static decimal SelectedSharePercent(ManufacturingBomLine line,
        IReadOnlyList<ManufacturingBomLine> bomLines,
        IReadOnlyList<ManufacturingOrderMaterialAllocation> allocations)
    {
        if (string.IsNullOrWhiteSpace(line.AlternativeGroupCode)) return 100m;
        var group = bomLines.Where(x => string.Equals(x.AlternativeGroupCode,
            line.AlternativeGroupCode, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (group.Length < 2) throw new InvalidOperationException("گروه آلترناتیو BOM نامعتبر است.");
        var ids = group.Select(x => x.Id).ToHashSet();
        var selected = allocations.Where(x => ids.Contains(x.BomLineId)).ToArray();
        if (selected.Length == 0)
        {
            if (group.Count(x => x.IsDefaultAlternative) != 1)
                throw new InvalidOperationException("گروه آلترناتیو باید دقیقاً یک پیش‌فرض داشته باشد.");
            return line.IsDefaultAlternative ? 100m : 0m;
        }
        if (selected.Any(x => x.SharePercent <= 0 || x.SharePercent > 100 || x.PlannedQuantity <= 0) ||
            selected.Select(x => x.BomLineId).Distinct().Count() != selected.Length ||
            selected.Sum(x => x.SharePercent) != 100m)
            throw new InvalidOperationException("تخصیص آلترناتیوها معتبر نیست.");
        return selected.SingleOrDefault(x => x.BomLineId == line.Id)?.SharePercent ?? 0m;
    }

    public static decimal CalculateSelectedComponentQuantity(decimal outputQuantity,
        ManufacturingBomLine line, IReadOnlyList<ManufacturingBomLine> bomLines,
        IReadOnlyList<ManufacturingOrderMaterialAllocation> allocations)
    {
        var share = SelectedSharePercent(line, bomLines, allocations);
        if (share == 0) return 0m;
        return CalculatePlannedComponentQuantity(outputQuantity, line, share);
    }

    public static IEndpointRouteBuilder MapBomAlternativeExecution(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        var planningApi = routes.MapGroup("/api/v1").RequireAuthorization("IpPlanningWrite");

        api.MapGet("/manufacturing-orders/{id:guid}/material-options", async (Guid id, ProductionDb db, CancellationToken ct) =>
        {
            var order = await db.ManufacturingOrders.AsNoTracking()
                .Include(x => x.Operations).ThenInclude(x => x.Confirmations)
                .SingleOrDefaultAsync(x => x.Id == id, ct);
            if (order is null) return Results.NotFound();
            var lines = await db.ManufacturingBomLines.AsNoTracking()
                .Where(x => x.RevisionId == order.BomRevisionId)
                .OrderBy(x => x.Sequence).ThenBy(x => x.AlternativePriority).ToListAsync(ct);
            var allocations = await db.Set<ManufacturingOrderMaterialAllocation>().AsNoTracking()
                .Where(x => x.OrderId == id).ToListAsync(ct);
            var usageRecords = await db.FactoryMaterialUsages.AsNoTracking()
                .Where(x => x.OrderId == id).ToListAsync(ct);
            IReadOnlyList<FactoryConsumptionLedgerRow> usageLedger;
            try { usageLedger = FactoryLineSideCorrections.Ledger(usageRecords); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
            var usedByComponent = usageLedger.GroupBy(x => x.ComponentItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.NetQuantity));
            var allocationByLine = allocations.ToDictionary(x => x.BomLineId);
            return Results.Ok(new
            {
                orderId = id,
                orderStatus = order.Status,
                plannedQuantity = order.PlannedQuantity,
                groups = lines.Where(x => !string.IsNullOrWhiteSpace(x.AlternativeGroupCode))
                    .GroupBy(x => x.AlternativeGroupCode!, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new
                    {
                        groupCode = g.Key,
                        options = g.Select(line => new
                        {
                            line.Id, line.ComponentItemId, line.QuantityPer, line.ScrapPercent,
                            line.AlternativePriority, line.IsDefaultAlternative,
                            sharePercent = allocationByLine.GetValueOrDefault(line.Id)?.SharePercent
                                ?? (line.IsDefaultAlternative && !allocations.Any(a => g.Any(gl => gl.Id == a.BomLineId)) ? 100m : 0m),
                            plannedQuantity = allocationByLine.GetValueOrDefault(line.Id)?.PlannedQuantity
                                ?? (line.IsDefaultAlternative && !allocations.Any(a => g.Any(gl => gl.Id == a.BomLineId))
                                    ? CalculatePlannedComponentQuantity(order.PlannedQuantity, line) : 0m),
                            actualQuantity = usedByComponent.GetValueOrDefault(line.ComponentItemId)
                        })
                    }),
                mandatoryComponents = lines.Where(x => string.IsNullOrWhiteSpace(x.AlternativeGroupCode))
                    .Select(line => new
                    {
                        line.Id, line.ComponentItemId, line.QuantityPer, line.ScrapPercent,
                        plannedQuantity = CalculatePlannedComponentQuantity(order.PlannedQuantity, line),
                        actualQuantity = usedByComponent.GetValueOrDefault(line.ComponentItemId)
                    }),
                lots = order.Operations.SelectMany(op => op.Confirmations.Select(c => new
                {
                    id = c.Id,
                    c.LotCode,
                    operationSequence = op.Sequence,
                    goodQuantity = c.GoodQuantity,
                    scrapQuantity = c.ScrapQuantity,
                    reworkQuantity = c.ReworkQuantity,
                    occurredUtc = c.OccurredUtc
                })).Where(x => !string.IsNullOrWhiteSpace(x.LotCode))
                    .OrderBy(x => x.occurredUtc).ThenBy(x => x.id)
            });
        });

        api.MapGet("/manufacturing-orders/{id:guid}/material-usage", async (Guid id, ProductionDb db, CancellationToken ct) =>
        {
            if (!await db.ManufacturingOrders.AsNoTracking().AnyAsync(x => x.Id == id, ct))
                return Results.NotFound();
            var records = await db.FactoryMaterialUsages.AsNoTracking()
                .Where(x => x.OrderId == id).ToListAsync(ct);
            try { return Results.Ok(new { orderId = id, rows = FactoryLineSideCorrections.Ledger(records) }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
        });

        planningApi.MapPost("/manufacturing-orders/{id:guid}/material-options", async (
            Guid id, ManufacturingOrderMaterialAllocationInput input, ProductionDb db, CancellationToken ct) =>
        {
            if (input.Groups is null || input.Groups.Count is < 1 or > 100 ||
                input.Groups.Any(g => g is null || g.Shares is null) ||
                input.Groups.Select(x => x.GroupCode?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Groups.Count ||
                input.Groups.Any(g => string.IsNullOrWhiteSpace(g.GroupCode) || g.GroupCode.Trim().Length > 32 ||
                    g.Shares.Count is < 1 or > 100 ||
                    g.Shares.Any(x => x is null || x.BomLineId == Guid.Empty || x.SharePercent <= 0 || x.SharePercent > 100) ||
                    g.Shares.Select(x => x.BomLineId).Distinct().Count() != g.Shares.Count ||
                    g.Shares.Sum(x => x.SharePercent) != 100m))
                return Results.BadRequest(new { message = "برای تمام گروه‌ها، قطعات بدون تکرار و سهم‌های مثبت لازم است که مجموع هر گروه دقیقاً ۱۰۰٪ باشد." });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try { await FactoryCapacityReservations.LockOrder(db, id, ct); }
            catch (SqlException) { return Results.Conflict(new { message = "قفل دستور برای تغییر تخصیص مواد در دسترس نیست." }); }
            var order = await db.ManufacturingOrders.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (order is null) return Results.NotFound();
            if (order.Status is not (ManufacturingOrderStatus.Draft or ManufacturingOrderStatus.Released or ManufacturingOrderStatus.InProgress))
                return Results.Conflict(new { message = "ترکیب آلترناتیوها در وضعیت فعلی دستور قابل تغییر نیست." });

            var lines = await db.ManufacturingBomLines.AsNoTracking()
                .Where(x => x.RevisionId == order.BomRevisionId && x.AlternativeGroupCode != null)
                .ToListAsync(ct);
            var usageRecords = await db.FactoryMaterialUsages.AsNoTracking()
                .Where(x => x.OrderId == id).ToListAsync(ct);
            IReadOnlyList<FactoryConsumptionLedgerRow> usageLedger;
            try { usageLedger = FactoryLineSideCorrections.Ledger(usageRecords); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
            var groupMap = lines.GroupBy(x => x.AlternativeGroupCode!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
            if (groupMap.Count != input.Groups.Count ||
                input.Groups.Any(x => !groupMap.ContainsKey(x.GroupCode.Trim())))
                return Results.BadRequest(new { message = "تمام گروه‌های آلترناتیو این BOM باید یکجا ارسال شوند." });
            if (groupMap.Values.Any(x => x.Count < 2))
                return Results.Conflict(new { message = "هر گروه آلترناتیو باید دست‌کم دو گزینه داشته باشد." });

            var allocations = db.Set<ManufacturingOrderMaterialAllocation>();
            var prior = await allocations.Where(x => x.OrderId == id).ToListAsync(ct);
            var allAlternativeIds = groupMap.Values.SelectMany(g => g).Select(x => x.Id).ToHashSet();
            if (prior.Any(x => !allAlternativeIds.Contains(x.BomLineId)))
                return Results.Conflict(new { message = "تخصیص ذخیره‌شده به نسخه آلترناتیو جاری دستور تعلق ندارد." });
            var auditPrevious = prior.OrderBy(x => x.BomLineId)
                .Select(x => $"{x.BomLineId}:{x.SharePercent}%").ToArray();
            var auditCurrent = new List<string>();
            var changed = false;

            foreach (var groupInput in input.Groups)
            {
                var groupCode = groupInput.GroupCode.Trim();
                var groupLines = groupMap[groupCode];
                var byId = groupLines.ToDictionary(x => x.Id);
                if (groupInput.Shares.Any(x => !byId.ContainsKey(x.BomLineId)))
                    return Results.BadRequest(new { message = $"همه قطعات تخصیص‌یافته باید عضو گروه {groupCode} باشند." });

                var groupLineIds = groupLines.Select(x => x.Id).ToArray();
                var groupComponentIds = groupLines.Select(x => x.ComponentItemId).ToArray();
                var hasUsage = usageLedger.Any(x => groupComponentIds.Contains(x.ComponentItemId));
                var requested = groupInput.Shares.OrderBy(x => x.BomLineId).ToArray();
                var priorGroup = prior.Where(x => groupLineIds.Contains(x.BomLineId)).ToArray();

                if (hasUsage)
                {
                    // A consumed group is immutable. It may still be resubmitted unchanged
                    // while another, as-yet-unused group is adjusted in the same atomic request.
                    var effectivePrior = priorGroup.Length > 0
                        ? priorGroup.Select(x => new ManufacturingOrderMaterialShareInput(x.BomLineId, x.SharePercent))
                            .OrderBy(x => x.BomLineId).ToArray()
                        : groupLines.Where(x => x.IsDefaultAlternative)
                            .Select(x => new ManufacturingOrderMaterialShareInput(x.Id, 100m)).ToArray();
                    if (requested.Length != effectivePrior.Length ||
                        requested.Where((x, i) => x.BomLineId != effectivePrior[i].BomLineId ||
                            x.SharePercent != effectivePrior[i].SharePercent).Any())
                        return Results.Conflict(new { message = $"پس از ثبت مصرف واقعی، ترکیب گروه {groupCode} قفل شده است." });
                    auditCurrent.AddRange(requested.Select(x => $"{groupCode}:{x.BomLineId}:{x.SharePercent}%"));
                    continue;
                }

                if (priorGroup.Length > 0)
                {
                    db.RemoveRange(priorGroup);
                    changed = true;
                }
                foreach (var share in requested)
                {
                    var line = byId[share.BomLineId];
                    allocations.Add(new ManufacturingOrderMaterialAllocation
                    {
                        OrderId = id,
                        BomLineId = line.Id,
                        SharePercent = share.SharePercent,
                        PlannedQuantity = CalculatePlannedComponentQuantity(order.PlannedQuantity, line, share.SharePercent)
                    });
                    auditCurrent.Add($"{groupCode}:{line.Id}:{share.SharePercent}%");
                    changed = true;
                }
            }

            if (changed)
            {
                db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit
                {
                    OrderId = id,
                    Action = prior.Count > 0 ? "MaterialAlternativesRevised" : "MaterialAlternativesAllocated",
                    Details = $"Previous={string.Join(",", auditPrevious)};Current={string.Join(",", auditCurrent)}"
                });
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new { orderId = id, groupCount = input.Groups.Count,
                totalSharePercentPerGroup = 100m, allocationCount = auditCurrent.Count, changed });
        });

        return routes;
    }
}
