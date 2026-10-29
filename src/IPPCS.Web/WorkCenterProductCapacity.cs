using Microsoft.EntityFrameworkCore;

/// <summary>
/// Optional product-and-operation throughput override for a particular work center.
/// The routing operation remains the source for setup time and yield; this record
/// supplies the product-specific run rate in processed units/hour.
/// </summary>
public sealed class WorkCenterProductCapacity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoutingOperationId { get; set; }
    public decimal CapacityUnitsPerHour { get; set; }
    public bool IsActive { get; set; } = true;
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed record WorkCenterProductCapacityInput(
    Guid RoutingOperationId, decimal CapacityUnitsPerHour, bool IsActive = true);

public static class WorkCenterProductCapacityRules
{
    public static void ConfigureModel(ModelBuilder model)
    {
        model.Entity<WorkCenterProductCapacity>().HasKey(x => x.Id);
        model.Entity<WorkCenterProductCapacity>().ToTable("WorkCenterProductCapacities",
            table => table.HasCheckConstraint(
                "CK_WorkCenterProductCapacities_CapacityUnitsPerHour_Positive",
                "[CapacityUnitsPerHour] >= 0.0001"));
        model.Entity<WorkCenterProductCapacity>().HasIndex(x => x.RoutingOperationId).IsUnique();
        model.Entity<WorkCenterProductCapacity>().HasIndex(x => new { x.IsActive, x.RoutingOperationId });
        model.Entity<WorkCenterProductCapacity>().Property(x => x.CapacityUnitsPerHour)
            .HasPrecision(18, 4);
        model.Entity<WorkCenterProductCapacity>().Property(x => x.UpdatedBy)
            .HasMaxLength(256);
        model.Entity<WorkCenterProductCapacity>().HasOne<RoutingOperation>().WithMany()
            .HasForeignKey(x => x.RoutingOperationId).OnDelete(DeleteBehavior.Restrict);
    }

    public static async Task<Dictionary<Guid, decimal>> LoadActiveRates(
        ProductionDb db, CancellationToken ct)
    {
        var rows = await db.WorkCenterProductCapacities.AsNoTracking()
            .Where(x => x.IsActive && x.CapacityUnitsPerHour > 0)
            .Select(x => new { x.RoutingOperationId, x.CapacityUnitsPerHour })
            .ToListAsync(ct);
        return rows.ToDictionary(x => x.RoutingOperationId, x => x.CapacityUnitsPerHour);
    }

    /// <summary>
    /// Apply rate overrides only to this in-memory route snapshot. No routing
    /// standards are rewritten and inactive overrides fall back to the routing rate.
    /// </summary>
    public static void ApplyToRoutes(
        IEnumerable<RoutingRevision> routes,
        IReadOnlyDictionary<Guid, decimal> activeRates)
    {
        foreach (var operation in routes.SelectMany(x => x.Operations))
        {
            if (activeRates.TryGetValue(operation.Id, out var unitsPerHour))
            {
                if (unitsPerHour <= 0)
                    throw new InvalidOperationException("ظرفیت محصول‌محور باید مثبت باشد.");
                operation.RunMinutesPerUnit = 60m / unitsPerHour;
            }
        }
    }

    public static IEndpointRouteBuilder MapWorkCenterProductCapacities(
        this IEndpointRouteBuilder routes)
    {
        var read = routes.MapGroup("/api/v1/work-center-product-capacities")
            .RequireAuthorization("IpProductionUser");
        var write = routes.MapGroup("/api/v1/work-center-product-capacities")
            .RequireAuthorization("IpPlanningWrite");

        read.MapGet("", async (ProductionDb db, CancellationToken ct) =>
        {
            var rows = await (
                from cap in db.WorkCenterProductCapacities.AsNoTracking()
                join op in db.RoutingOperations.AsNoTracking() on cap.RoutingOperationId equals op.Id
                join route in db.RoutingRevisions.AsNoTracking() on op.RoutingRevisionId equals route.Id
                join item in db.Items.AsNoTracking() on route.ItemId equals item.Id
                join center in db.WorkCenters.AsNoTracking() on op.WorkCenterId equals center.Id
                orderby item.Code, center.Code, route.RevisionCode, op.Sequence
                select new
                {
                    cap.Id, cap.RoutingOperationId, itemId = item.Id, itemCode = item.Code,
                    itemName = item.Name, itemType = item.Type,
                    routingRevisionId = route.Id, route.RevisionCode, routingState = route.State,
                    operationSequence = op.Sequence, operationName = op.Name,
                    workCenterId = center.Id, workCenterCode = center.Code,
                    workCenterName = center.Name, cap.CapacityUnitsPerHour,
                    effectiveRunMinutesPerUnit = 60m / cap.CapacityUnitsPerHour,
                    cap.IsActive, cap.UpdatedBy, cap.UpdatedUtc
                }).Take(1000).ToListAsync(ct);
            return Results.Ok(rows);
        });

        write.MapPost("", async (WorkCenterProductCapacityInput input,
            ProductionDb db, HttpContext http, CancellationToken ct) =>
        {
            if (input.RoutingOperationId == Guid.Empty ||
                input.CapacityUnitsPerHour is < 0.0001m or > 1_000_000m)
                return Results.BadRequest(new
                {
                    message = "عملیات محصول و ظرفیت عبوری مثبت تا یک میلیون واحد در ساعت لازم است."
                });

            var operation = await db.RoutingOperations.AsNoTracking()
                .Where(x => x.Id == input.RoutingOperationId)
                .Select(x => new { x.Id, x.WorkCenterId, x.RoutingRevisionId,
                    itemId = x.RoutingRevision.ItemId })
                .SingleOrDefaultAsync(ct);
            if (operation is null) return Results.NotFound();

            var itemType = await db.Items.AsNoTracking()
                .Where(x => x.Id == operation.itemId)
                .Select(x => (PlanningItemType?)x.Type).SingleOrDefaultAsync(ct);
            if (itemType is not (PlanningItemType.Finished or PlanningItemType.SemiFinished))
                return Results.BadRequest(new
                {
                    message = "ظرفیت محصول‌محور فقط برای محصول نهایی یا نیمه‌ساخته دارای مسیر ساخت ثبت می‌شود."
                });

            if (await db.WorkCenterProductCapacities.AnyAsync(
                x => x.RoutingOperationId == input.RoutingOperationId, ct))
                return Results.Conflict(new
                {
                    message = "برای این عملیات ظرفیت محصول‌محور قبلاً ثبت شده است؛ همان ردیف را ویرایش کنید."
                });

            var row = new WorkCenterProductCapacity
            {
                RoutingOperationId = input.RoutingOperationId,
                CapacityUnitsPerHour = input.CapacityUnitsPerHour,
                IsActive = input.IsActive,
                UpdatedBy = http.User.Identity?.Name ?? "unknown",
                UpdatedUtc = DateTime.UtcNow
            };
            db.WorkCenterProductCapacities.Add(row);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                return Results.Conflict(new
                {
                    message = "ظرفیت این عملیات هم‌زمان توسط درخواست دیگری ثبت شده است؛ فهرست را بازخوانی کنید."
                });
            }
            return Results.Created($"/api/v1/work-center-product-capacities/{row.Id}",
                new { row.Id, row.RoutingOperationId, row.CapacityUnitsPerHour,
                    effectiveRunMinutesPerUnit = 60m / row.CapacityUnitsPerHour,
                    row.IsActive, row.UpdatedBy, row.UpdatedUtc });
        });

        write.MapPut("/{id:guid}", async (Guid id, WorkCenterProductCapacityInput input,
            ProductionDb db, HttpContext http, CancellationToken ct) =>
        {
            if (input.RoutingOperationId == Guid.Empty ||
                input.CapacityUnitsPerHour is <= 0 or > 1_000_000m)
                return Results.BadRequest(new
                {
                    message = "عملیات محصول و ظرفیت عبوری مثبت تا یک میلیون واحد در ساعت لازم است."
                });
            var row = await db.WorkCenterProductCapacities.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (row is null) return Results.NotFound();
            if (row.RoutingOperationId != input.RoutingOperationId)
                return Results.Conflict(new
                {
                    message = "این سابقه متعلق به عملیات انتخاب‌شده دیگری است؛ عملیات یک سابقه ظرفیت مستقل دارد."
                });
            row.CapacityUnitsPerHour = input.CapacityUnitsPerHour;
            row.IsActive = input.IsActive;
            row.UpdatedBy = http.User.Identity?.Name ?? "unknown";
            row.UpdatedUtc = DateTime.UtcNow;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { message = "ثبت ظرفیت انجام نشد؛ فهرست را بازخوانی کنید." });
            }
            return Results.Ok(new { row.Id, row.RoutingOperationId, row.CapacityUnitsPerHour,
                effectiveRunMinutesPerUnit = 60m / row.CapacityUnitsPerHour,
                row.IsActive, row.UpdatedBy, row.UpdatedUtc });
        });

        return routes;
    }
}
