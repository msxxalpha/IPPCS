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

public sealed class ManufacturingOrderMaterialUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid BomLineId { get; set; }
    public Guid ComponentItemId { get; set; } // Immutable component identity at recording time.
    public decimal Quantity { get; set; }
    public string? LotCode { get; set; }
    public string? Note { get; set; }
    public string RecordedBy { get; set; } = "unknown";
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}

public record ManufacturingOrderMaterialShareInput(Guid BomLineId, decimal SharePercent);
public record ManufacturingOrderMaterialAllocationInput(IReadOnlyList<ManufacturingOrderMaterialShareInput> Shares);
public record ManufacturingOrderMaterialUsageInput(Guid BomLineId, decimal Quantity, string? LotCode, string? Note);

public static class BomAlternativeExecution
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ManufacturingOrderMaterialAllocation>().HasIndex(x => new { x.OrderId, x.BomLineId }).IsUnique();
        model.Entity<ManufacturingOrderMaterialAllocation>().Property(x => x.SharePercent).HasPrecision(8, 4);
        model.Entity<ManufacturingOrderMaterialAllocation>().Property(x => x.PlannedQuantity).HasPrecision(18, 4);
        model.Entity<ManufacturingOrderMaterialAllocation>().HasOne<ManufacturingOrder>()
            .WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderMaterialAllocation>().HasOne<ManufacturingBomLine>()
            .WithMany().HasForeignKey(x => x.BomLineId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderMaterialUsage>().Property(x => x.Quantity).HasPrecision(18, 4);
        model.Entity<ManufacturingOrderMaterialUsage>().Property(x => x.LotCode).HasMaxLength(100);
        model.Entity<ManufacturingOrderMaterialUsage>().Property(x => x.Note).HasMaxLength(500);
        model.Entity<ManufacturingOrderMaterialUsage>().Property(x => x.RecordedBy).HasMaxLength(256);
        model.Entity<ManufacturingOrderMaterialUsage>().HasIndex(x => new { x.OrderId, x.BomLineId, x.OccurredUtc });
        model.Entity<ManufacturingOrderMaterialUsage>().HasOne<ManufacturingOrder>()
            .WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderMaterialUsage>().HasOne<ManufacturingBomLine>()
            .WithMany().HasForeignKey(x => x.BomLineId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderMaterialUsage>().HasOne<PlanningItem>()
            .WithMany().HasForeignKey(x => x.ComponentItemId).OnDelete(DeleteBehavior.Restrict);
    }

    private static decimal RequiredQuantity(ManufacturingOrder order, ManufacturingBomLine line, decimal? sharePercent = null)
    {
        var quantity = order.PlannedQuantity * line.QuantityPer / (1m - line.ScrapPercent / 100m);
        return sharePercent.HasValue ? quantity * sharePercent.Value / 100m : quantity;
    }

    public static IEndpointRouteBuilder MapBomAlternativeExecution(this IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");
        var planningApi = routes.MapGroup("/api/v1").RequireAuthorization("IpPlanningWrite");
        var floorApi = routes.MapGroup("/api/v1").RequireAuthorization("IpShopFloorWrite");

        api.MapGet("/manufacturing-orders/{id:guid}/material-options", async (Guid id, ProductionDb db, CancellationToken ct) =>
        {
            var order = await db.ManufacturingOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (order is null) return Results.NotFound();
            var lines = await db.ManufacturingBomLines.AsNoTracking()
                .Where(x => x.RevisionId == order.BomRevisionId)
                .OrderBy(x => x.Sequence).ThenBy(x => x.AlternativePriority).ToListAsync(ct);
            var allocations = await db.Set<ManufacturingOrderMaterialAllocation>().AsNoTracking()
                .Where(x => x.OrderId == id).ToListAsync(ct);
            var used = await db.Set<ManufacturingOrderMaterialUsage>().AsNoTracking()
                .Where(x => x.OrderId == id).GroupBy(x => x.BomLineId)
                .Select(g => new { BomLineId = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToListAsync(ct);
            var usedByLine = used.ToDictionary(x => x.BomLineId, x => x.Quantity);
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
                                    ? RequiredQuantity(order, line) : 0m),
                            actualQuantity = usedByLine.GetValueOrDefault(line.Id)
                        })
                    }),
                mandatoryComponents = lines.Where(x => string.IsNullOrWhiteSpace(x.AlternativeGroupCode))
                    .Select(line => new
                    {
                        line.Id, line.ComponentItemId, line.QuantityPer, line.ScrapPercent,
                        plannedQuantity = RequiredQuantity(order, line),
                        actualQuantity = usedByLine.GetValueOrDefault(line.Id)
                    })
            });
        });

        api.MapGet("/manufacturing-orders/{id:guid}/material-usage", async (Guid id, ProductionDb db, CancellationToken ct) =>
        {
            if (!await db.ManufacturingOrders.AsNoTracking().AnyAsync(x => x.Id == id, ct))
                return Results.NotFound();
            var rows = await db.Set<ManufacturingOrderMaterialUsage>().AsNoTracking()
                .Where(x => x.OrderId == id).OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.Id, x.BomLineId, x.ComponentItemId, x.Quantity, x.LotCode,
                    x.Note, x.RecordedBy, x.OccurredUtc
                }).ToListAsync(ct);
            return Results.Ok(new { orderId = id, rows });
        });

        planningApi.MapPost("/manufacturing-orders/{id:guid}/material-options/{groupCode}", async (
            Guid id, string groupCode, ManufacturingOrderMaterialAllocationInput input,
            ProductionDb db, HttpContext context, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(groupCode) || groupCode.Trim().Length > 32 ||
                input.Shares is null || input.Shares.Count < 1 ||
                input.Shares.Any(x => x.BomLineId == Guid.Empty || x.SharePercent <= 0 || x.SharePercent > 100) ||
                input.Shares.Select(x => x.BomLineId).Distinct().Count() != input.Shares.Count ||
                input.Shares.Sum(x => x.SharePercent) != 100m)
                return Results.BadRequest(new { message = "سهم قطعات باید مثبت، بدون تکرار و در مجموع دقیقاً ۱۰۰ درصد باشد." });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try { await FactoryCapacityReservations.LockOrder(db, id, ct); }
            catch (SqlException) { return Results.Conflict(new { message = "قفل دستور برای تغییر تخصیص مواد در دسترس نیست." }); }
            var order = await db.ManufacturingOrders.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (order is null) return Results.NotFound();
            if (order.Status is not (ManufacturingOrderStatus.Draft or ManufacturingOrderStatus.Released or ManufacturingOrderStatus.InProgress))
                return Results.Conflict(new { message = "ترکیب آلترناتیوها در وضعیت فعلی دستور قابل تغییر نیست." });

            var lines = await db.ManufacturingBomLines.AsNoTracking()
                .Where(x => x.RevisionId == order.BomRevisionId &&
                    x.AlternativeGroupCode != null && x.AlternativeGroupCode.ToUpper() == groupCode.Trim().ToUpper())
                .ToListAsync(ct);
            if (lines.Count < 2)
                return Results.NotFound();
            var byId = lines.ToDictionary(x => x.Id);
            if (input.Shares.Any(x => !byId.ContainsKey(x.BomLineId)))
                return Results.BadRequest(new { message = "تمام قطعات تخصیص‌یافته باید عضو همین گروه آلترناتیو مصوب باشند." });
            var groupLineIds = lines.Select(x => x.Id).ToArray();
            if (await db.Set<ManufacturingOrderMaterialUsage>().AnyAsync(x =>
                x.OrderId == id && groupLineIds.Contains(x.BomLineId), ct))
                return Results.Conflict(new { message = "پس از اولین مصرف واقعی از هر قطعه این گروه، ترکیب آلترناتیو قفل می‌شود؛ اصلاح باید از مسیر کنترل‌شده مغایرت انجام شود." });

            var prior = await db.Set<ManufacturingOrderMaterialAllocation>()
                .Where(x => x.OrderId == id && lines.Select(l => l.Id).Contains(x.BomLineId)).ToListAsync(ct);
            var priorShares = prior.OrderBy(x => x.BomLineId)
                .Select(x => $"{x.BomLineId}:{x.SharePercent}%").ToArray();
            if (prior.Count > 0) db.RemoveRange(prior);
            foreach (var share in input.Shares)
            {
                var line = byId[share.BomLineId];
                db.Set<ManufacturingOrderMaterialAllocation>().Add(new ManufacturingOrderMaterialAllocation
                {
                    OrderId = id,
                    BomLineId = line.Id,
                    SharePercent = share.SharePercent,
                    PlannedQuantity = RequiredQuantity(order, line, share.SharePercent)
                });
            }
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit
            {
                OrderId = id, Action = prior.Count > 0 ? "MaterialAlternativeRevised" : "MaterialAlternativeAllocated",
                Details = $"By={context.User.Identity?.Name ?? "unknown"};Group={groupCode.Trim().ToUpperInvariant()};Previous={string.Join(",", priorShares)};Current=" +
                    string.Join(",", input.Shares.Select(x => $"{x.BomLineId}:{x.SharePercent}%"))
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { orderId = id, groupCode = groupCode.Trim().ToUpperInvariant(), totalSharePercent = 100m });
        });

        floorApi.MapPost("/manufacturing-orders/{id:guid}/material-usage", async (
            Guid id, ManufacturingOrderMaterialUsageInput input, ProductionDb db, HttpContext context, CancellationToken ct) =>
        {
            if (input.BomLineId == Guid.Empty || input.Quantity <= 0 || input.Quantity > 1_000_000_000m ||
                (input.LotCode?.Length ?? 0) > 100 || (input.Note?.Length ?? 0) > 500)
                return Results.BadRequest(new { message = "قطعه، مقدار مصرف، بچ یا شرح معتبر نیست." });
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try { await FactoryCapacityReservations.LockOrder(db, id, ct); }
            catch (SqlException) { return Results.Conflict(new { message = "قفل دستور برای ثبت مصرف مواد در دسترس نیست." }); }
            var order = await db.ManufacturingOrders.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (order is null) return Results.NotFound();
            if (order.Status is not (ManufacturingOrderStatus.Released or ManufacturingOrderStatus.InProgress))
                return Results.Conflict(new { message = "ثبت مصرف مواد فقط برای دستور آزادشده یا در حال اجرا مجاز است." });
            var line = await db.ManufacturingBomLines.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == input.BomLineId && x.RevisionId == order.BomRevisionId, ct);
            if (line is null) return Results.BadRequest(new { message = "قطعه متعلق به BOM منجمد این دستور نیست." });

            decimal planned;
            if (string.IsNullOrWhiteSpace(line.AlternativeGroupCode))
                planned = RequiredQuantity(order, line);
            else
            {
                var groupLines = await db.ManufacturingBomLines.AsNoTracking()
                    .Where(x => x.RevisionId == order.BomRevisionId &&
                        x.AlternativeGroupCode == line.AlternativeGroupCode).ToListAsync(ct);
                var allocations = await db.Set<ManufacturingOrderMaterialAllocation>().AsNoTracking()
                    .Where(x => x.OrderId == id && groupLines.Select(g => g.Id).Contains(x.BomLineId)).ToListAsync(ct);
                if (allocations.Count > 0)
                {
                    var allocation = allocations.SingleOrDefault(x => x.BomLineId == line.Id);
                    if (allocation is null)
                        return Results.Conflict(new { message = "این آلترناتیو برای دستور انتخاب نشده است." });
                    planned = allocation.PlannedQuantity;
                }
                else
                {
                    if (!line.IsDefaultAlternative)
                        return Results.Conflict(new { message = "بدون تخصیص صریح، فقط قطعه پیش‌فرض گروه مجاز است." });
                    planned = RequiredQuantity(order, line);
                }
            }
            var priorUsage = await db.Set<ManufacturingOrderMaterialUsage>().AsNoTracking()
                .Where(x => x.OrderId == id && x.BomLineId == line.Id).SumAsync(x => (decimal?)x.Quantity, ct) ?? 0m;
            if (priorUsage + input.Quantity > planned && string.IsNullOrWhiteSpace(input.Note))
                return Results.Conflict(new { message = "مصرف تجمعی از مقدار برنامه‌ریزی‌شده بیشتر است؛ ثبت علت انحراف الزامی است." });
            var usage = new ManufacturingOrderMaterialUsage
            {
                OrderId = id, BomLineId = line.Id, ComponentItemId = line.ComponentItemId,
                Quantity = input.Quantity, LotCode = input.LotCode?.Trim(), Note = input.Note?.Trim(),
                RecordedBy = context.User.Identity?.Name ?? "unknown"
            };
            db.Set<ManufacturingOrderMaterialUsage>().Add(usage);
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit
            {
                OrderId = id, Action = "MaterialUsageRecorded",
                Details = $"By={usage.RecordedBy};Component={line.ComponentItemId};Qty={input.Quantity};Lot={usage.LotCode ?? ""}"
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/v1/manufacturing-orders/{id}/material-usage/{usage.Id}",
                new { usage.Id, usage.BomLineId, usage.ComponentItemId, usage.Quantity, usage.LotCode, usage.RecordedBy, usage.OccurredUtc });
        });
        return routes;
    }
}
