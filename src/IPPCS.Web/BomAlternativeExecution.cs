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
public record ManufacturingOrderMaterialGroupInput(string GroupCode, IReadOnlyList<ManufacturingOrderMaterialShareInput> Shares);
public record ManufacturingOrderMaterialAllocationInput(IReadOnlyList<ManufacturingOrderMaterialGroupInput> Groups);
public record ManufacturingOrderMaterialUsageInput(Guid BomLineId, decimal Quantity, string? LotCode, string? Note);

public static class BomAlternativeExecution
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<ManufacturingOrderMaterialAllocation>().ToTable("ManufacturingOrderMaterialAllocations");
        model.Entity<ManufacturingOrderMaterialUsage>().ToTable("ManufacturingOrderMaterialUsages");
        model.Entity<ManufacturingOrderMaterialAllocation>().HasIndex(x => new { x.OrderId, x.BomLineId }).IsUnique();
        model.Entity<ManufacturingOrderMaterialAllocation>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_ManufacturingOrderMaterialAllocations_SharePercent",
                "[SharePercent] > 0 AND [SharePercent] <= 100");
            t.HasCheckConstraint("CK_ManufacturingOrderMaterialAllocations_PlannedQuantity",
                "[PlannedQuantity] > 0");
        });
        model.Entity<ManufacturingOrderMaterialAllocation>().Property(x => x.SharePercent).HasPrecision(8, 4);
        model.Entity<ManufacturingOrderMaterialAllocation>().Property(x => x.PlannedQuantity).HasPrecision(18, 4);
        model.Entity<ManufacturingOrderMaterialAllocation>().HasOne<ManufacturingOrder>()
            .WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderMaterialAllocation>().HasOne<ManufacturingBomLine>()
            .WithMany().HasForeignKey(x => x.BomLineId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ManufacturingOrderMaterialUsage>().ToTable(t =>
            t.HasCheckConstraint("CK_ManufacturingOrderMaterialUsages_Quantity_Positive", "[Quantity] > 0"));
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

    public static decimal CalculatePlannedComponentQuantity(
        decimal orderQuantity, ManufacturingBomLine line, decimal? sharePercent = null)
    {
        if (orderQuantity <= 0 || line.QuantityPer <= 0 || line.ScrapPercent is < 0 or >= 100 ||
            (sharePercent.HasValue && sharePercent.Value is <= 0 or > 100))
            throw new InvalidOperationException("مقدار دستور، ضریب مصرف، ضایعات یا سهم آلترناتیو نامعتبر است.");
        var quantity = orderQuantity * line.QuantityPer / (1m - line.ScrapPercent / 100m);
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
                                    ? CalculatePlannedComponentQuantity(order.PlannedQuantity, line) : 0m),
                            actualQuantity = usedByLine.GetValueOrDefault(line.Id)
                        })
                    }),
                mandatoryComponents = lines.Where(x => string.IsNullOrWhiteSpace(x.AlternativeGroupCode))
                    .Select(line => new
                    {
                        line.Id, line.ComponentItemId, line.QuantityPer, line.ScrapPercent,
                        plannedQuantity = CalculatePlannedComponentQuantity(order.PlannedQuantity, line),
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

        planningApi.MapPost("/manufacturing-orders/{id:guid}/material-options", async (
            Guid id, ManufacturingOrderMaterialAllocationInput input, ProductionDb db, CancellationToken ct) =>
        {
            if (input.Groups is null || input.Groups.Count is < 1 or > 100 ||
                input.Groups.Select(x => x.GroupCode?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Groups.Count ||
                input.Groups.Any(g => string.IsNullOrWhiteSpace(g.GroupCode) || g.GroupCode.Trim().Length > 32 ||
                    g.Shares is null || g.Shares.Count is < 1 or > 100 ||
                    g.Shares.Any(x => x.BomLineId == Guid.Empty || x.SharePercent <= 0 || x.SharePercent > 100) ||
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
            var actualGroupMap = lines.GroupBy(x => x.AlternativeGroupCode!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
            if (actualGroupMap.Count != input.Groups.Count ||
                input.Groups.Any(x => !actualGroupMap.ContainsKey(x.GroupCode.Trim())))
                return Results.BadRequest(new { message = "تمام گروه‌های آلترناتیو BOM باید در یک مرحله تخصیص داده شوند." });

            var allocations = db.Set<ManufacturingOrderMaterialAllocation>();
            var usage = db.Set<ManufacturingOrderMaterialUsage>();
            var allLineIds = lines.Select(x => x.Id).ToArray();
            if (await usage.AnyAsync(x => x.OrderId == id && allLineIds.Contains(x.BomLineId), ct))
                return Results.Conflict(new { message = "پس از اولین مصرف واقعی از هر قطعه آلترناتیو، ترکیب گروه‌ها قفل می‌شود؛ اصلاح باید از مسیر کنترل‌شده مغایرت انجام شود." });

            var prior = await allocations.Where(x => x.OrderId == id).ToListAsync(ct);
            var priorDetails = prior.OrderBy(x => x.BomLineId)
                .Select(x => $"{x.BomLineId}:{x.SharePercent}%").ToArray();
            if (prior.Count > 0) db.RemoveRange(prior);

            var currentDetails = new List<string>();
            foreach (var groupInput in input.Groups)
            {
                var groupCode = groupInput.GroupCode.Trim();
                var groupLines = actualGroupMap[groupCode];
                if (groupLines.Count < 2)
                    return Results.Conflict(new { message = $"گروه {groupCode} کمتر از دو آلترناتیو معتبر دارد." });
                var byId = groupLines.ToDictionary(x => x.Id);
                if (groupInput.Shares.Any(x => !byId.ContainsKey(x.BomLineId)))
                    return Results.BadRequest(new { message = $"همه قطعات تخصیص‌یافته باید عضو گروه {groupCode} باشند." });
                foreach (var share in groupInput.Shares)
                {
                    var line = byId[share.BomLineId];
                    allocations.Add(new ManufacturingOrderMaterialAllocation
                    {
                        OrderId = id,
                        BomLineId = line.Id,
                        SharePercent = share.SharePercent,
                        PlannedQuantity = RequiredQuantity(order, line, share.SharePercent)
                    });
                    currentDetails.Add($"{groupCode}:{line.Id}:{share.SharePercent}%");
                }
            }
            db.ManufacturingOrderAudits.Add(new ManufacturingOrderAudit
            {
                OrderId = id,
                Action = prior.Count > 0 ? "MaterialAlternativesRevised" : "MaterialAlternativesAllocated",
                Details = $"Previous={string.Join(",", priorDetails)};Current={string.Join(",", currentDetails)}"
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { orderId = id, groupCount = input.Groups.Count,
                totalSharePercentPerGroup = 100m, allocationCount = currentDetails.Count });
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
                planned = CalculatePlannedComponentQuantity(order.PlannedQuantity, line);
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
                    planned = CalculatePlannedComponentQuantity(order.PlannedQuantity, line);
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
