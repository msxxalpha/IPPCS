using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ProductionDb>(o =>
{
    var cs = builder.Configuration.GetConnectionString("Production");
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException("Configure ConnectionStrings:Production via user secrets/environment.");
    o.UseSqlServer(cs);
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddIpSecurity();
builder.Services.AddRateLimiter(options=>options.AddFixedWindowLimiter("ip-login",policy=>{
    policy.PermitLimit=8;
    policy.Window=TimeSpan.FromMinutes(2);
    policy.QueueLimit=0;
}));

var app = builder.Build();
// The prototype has no authentication yet. Deny non-development hosting until security is implemented.
if (!app.Environment.IsDevelopment())
    throw new InvalidOperationException("IPPCS prototype cannot run outside Development before authentication and authorization are implemented.");
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseIpCsrfProtection();
app.MapIpAuth();
app.MapLocalAccounts();
app.MapGet("/", () => Results.Redirect("/app.html"));
app.MapGet("/health", () => Results.Ok(new { service = "IPPCS", status = "ready", version = "0.7.1" }));
var api = app.MapGroup("/api/v1").RequireAuthorization("IpProductionUser");

// Items are planning references, NOT warehouse inventory records.
api.MapGet("/items", async (ProductionDb db, CancellationToken ct) =>
    await db.Items.AsNoTracking().OrderBy(x => x.Code).Take(500).ToListAsync(ct));
api.MapPost("/items", async (ItemInput input, ProductionDb db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name))
        return Results.BadRequest("Item code and name are required.");
    var code = input.Code.Trim();
    if (await db.Items.AnyAsync(x => x.Code == code, ct)) return Results.Conflict("Duplicate item code.");
    var item = new PlanningItem { Code = code, Name = input.Name.Trim(), Unit = input.Unit.Trim(), Type = input.Type };
    db.Items.Add(item); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/items/{item.Id}", item);
});
api.MapGet("/items/{id:guid}", async (Guid id, ProductionDb db, CancellationToken ct) =>
    await db.Items.FindAsync([id], ct) is { } item ? Results.Ok(item) : Results.NotFound());

api.MapGet("/work-centers", async (ProductionDb db, CancellationToken ct) =>
    await db.WorkCenters.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct));
api.MapPost("/work-centers", async (WorkCenterInput input, ProductionDb db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Code) || input.NominalUnitsPerHour <= 0)
        return Results.BadRequest("Code and positive nominal capacity are required.");
    var code = input.Code.Trim();
    if (await db.WorkCenters.AnyAsync(x => x.Code == code, ct)) return Results.Conflict("Duplicate work-center code.");
    var center = new WorkCenter { Code = code, Name = input.Name.Trim(), NominalUnitsPerHour = input.NominalUnitsPerHour };
    db.WorkCenters.Add(center); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/work-centers/{center.Id}", center);
});

api.MapGet("/bom/{parentId:guid}", async (Guid parentId, ProductionDb db, CancellationToken ct) =>
    await db.BomComponents.AsNoTracking().Where(x => x.ParentItemId == parentId).OrderBy(x => x.Sequence).ToListAsync(ct));
api.MapPost("/bom", async (BomInput input, ProductionDb db, CancellationToken ct) =>
{
    if (input.ParentItemId == input.ComponentItemId || input.QuantityPer <= 0 || input.ScrapPercent < 0 || input.ScrapPercent >= 100)
        return Results.BadRequest("Invalid BOM component.");
    if (!await db.Items.AnyAsync(x => x.Id == input.ParentItemId, ct) ||
        !await db.Items.AnyAsync(x => x.Id == input.ComponentItemId, ct))
        return Results.BadRequest("Unknown item.");
    // Cycle detection protects multilevel material explosion.
    var edges = await db.BomComponents.AsNoTracking()
        .Select(x => new { x.ParentItemId, x.ComponentItemId }).ToListAsync(ct);
    var visited = new HashSet<Guid>();
    bool ReachesParent(Guid current)
    {
        if (current == input.ParentItemId) return true;
        if (!visited.Add(current)) return false;
        return edges.Where(x => x.ParentItemId == current).Any(x => ReachesParent(x.ComponentItemId));
    }
    if (ReachesParent(input.ComponentItemId)) return Results.BadRequest("BOM cycle detected.");
    if (await db.BomComponents.AnyAsync(x => x.ParentItemId == input.ParentItemId && x.ComponentItemId == input.ComponentItemId, ct))
        return Results.Conflict("Duplicate BOM component.");
    var component = new BomComponent {
        ParentItemId = input.ParentItemId, ComponentItemId = input.ComponentItemId,
        QuantityPer = input.QuantityPer, ScrapPercent = input.ScrapPercent, Sequence = input.Sequence
    };
    db.BomComponents.Add(component); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/bom/{component.ParentItemId}", component);
});

api.MapGet("/production-plans", async (ProductionDb db, CancellationToken ct) =>
    await db.ProductionPlans.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(ct));
api.MapPost("/production-plans", async (PlanInput input, ProductionDb db, CancellationToken ct) =>
{
    if (input.Quantity <= 0 || input.DueDateUtc <= DateTime.UtcNow)
        return Results.BadRequest("Positive quantity and future due date are required.");
    if (!await db.Items.AnyAsync(x => x.Id == input.ItemId, ct)) return Results.BadRequest("Unknown item.");
    var plan = new ProductionPlan { ItemId = input.ItemId, Quantity = input.Quantity, DueDateUtc = input.DueDateUtc.ToUniversalTime() };
    db.ProductionPlans.Add(plan); await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/production-plans/{plan.Id}", plan);
});

// Deterministic, read-only gross-requirements explosion; not yet net MRP.
api.MapGet("/planning/gross-requirements/{planId:guid}", async (Guid planId, ProductionDb db, CancellationToken ct) =>
{
    var plan = await db.ProductionPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId, ct);
    if (plan is null) return Results.NotFound();
    var bom = await db.BomComponents.AsNoTracking().ToListAsync(ct);
    var totals = new Dictionary<Guid, decimal>();
    void Expand(Guid parent, decimal required, HashSet<Guid> ancestors)
    {
        if (!ancestors.Add(parent)) throw new InvalidOperationException("BOM cycle found.");
        foreach (var component in bom.Where(x => x.ParentItemId == parent))
        {
            var quantity = required * component.QuantityPer / (1 - component.ScrapPercent / 100m);
            totals[component.ComponentItemId] = totals.GetValueOrDefault(component.ComponentItemId) + quantity;
            Expand(component.ComponentItemId, quantity, ancestors);
        }
        ancestors.Remove(parent);
    }
    try { Expand(plan.ItemId, plan.Quantity, new HashSet<Guid>()); }
    catch (InvalidOperationException) { return Results.Problem("BOM cycle detected in stored data."); }
    return Results.Ok(new { plan.Id, plan.ItemId, plan.Quantity, requirements = totals.Select(x => new { itemId = x.Key, quantity = x.Value }) });
});

app.MapManufacturingFoundation();
app.MapPlanningStageThree();
app.MapManufacturingExecution();
app.MapQualityAndOee();
app.MapControlPlanEndpoints();
app.MapCrossOrderTrace();
app.MapEndOfLineAcceptance();
app.MapSerialGenealogy();
app.MapInProcessHandoffs();
app.MapGenealogyExplorer();
app.MapFactoryExecutionInsights();
app.MapPlanningReconciliation();
app.MapMpsChangeControl();
app.MapFactoryRevisionGovernance();
app.MapLineSideCorrections();
app.MapDeviationGovernance();
app.MapOrderPegging();
app.MapProductionDeviations();
app.MapDemandTrace();
app.MapTimePhasedPegging();
app.MapNetMrpDemandPegging();
app.MapMrpLotCapacityPreview();
app.MapFactoryOperationClock();
app.MapFactoryCapacityReservations();
app.MapDemandAps();
app.MapOperationsAnalytics();
app.MapFactoryProductionIntelligence();
app.Run();

public sealed class ProductionDb(DbContextOptions<ProductionDb> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<PlanningItem> Items => Set<PlanningItem>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<BomComponent> BomComponents => Set<BomComponent>();
    public DbSet<ProductionPlan> ProductionPlans => Set<ProductionPlan>();
    public DbSet<RoutingRevision> RoutingRevisions => Set<RoutingRevision>();
    public DbSet<RoutingOperation> RoutingOperations => Set<RoutingOperation>();
    public DbSet<WorkShift> WorkShifts => Set<WorkShift>();
    public DbSet<ManufacturingBomRevision> ManufacturingBomRevisions => Set<ManufacturingBomRevision>();
    public DbSet<ManufacturingBomLine> ManufacturingBomLines => Set<ManufacturingBomLine>();
    public DbSet<MpsVersion> MpsVersions => Set<MpsVersion>();
    public DbSet<MpsDemandLine> MpsDemandLines => Set<MpsDemandLine>();
    public DbSet<PlanningBalance> PlanningBalances => Set<PlanningBalance>();
    public DbSet<PlanningExpectedReceipt> PlanningExpectedReceipts => Set<PlanningExpectedReceipt>();
    public DbSet<PlanningCalculation> PlanningCalculations => Set<PlanningCalculation>();
    public DbSet<ManufacturingOrder> ManufacturingOrders => Set<ManufacturingOrder>();
    public DbSet<ManufacturingOrderOperation> ManufacturingOrderOperations => Set<ManufacturingOrderOperation>();
    public DbSet<ManufacturingConfirmation> ManufacturingConfirmations => Set<ManufacturingConfirmation>();
    public DbSet<ManufacturingDowntime> ManufacturingDowntimes => Set<ManufacturingDowntime>();
    public DbSet<ManufacturingOrderAudit> ManufacturingOrderAudits => Set<ManufacturingOrderAudit>();
    public DbSet<ManufacturingQualityHold> ManufacturingQualityHolds => Set<ManufacturingQualityHold>();
    public DbSet<ManufacturingOeePlan> ManufacturingOeePlans => Set<ManufacturingOeePlan>();
    public DbSet<ManufacturingControlPlan> ManufacturingControlPlans => Set<ManufacturingControlPlan>();
    public DbSet<ControlPlanCharacteristic> ControlPlanCharacteristics => Set<ControlPlanCharacteristic>();
    public DbSet<ManufacturingInspectionRequirement> ManufacturingInspectionRequirements => Set<ManufacturingInspectionRequirement>();
    public DbSet<ManufacturingInspectionResult> ManufacturingInspectionResults => Set<ManufacturingInspectionResult>();
    public DbSet<ManufacturingLotGenealogy> ManufacturingLotGenealogies => Set<ManufacturingLotGenealogy>();
    public DbSet<ManufacturingOrderPeg> ManufacturingOrderPegs => Set<ManufacturingOrderPeg>();
    public DbSet<ProductionDeviation> ProductionDeviations => Set<ProductionDeviation>();
    public DbSet<ProductionDeviationEvent> ProductionDeviationEvents => Set<ProductionDeviationEvent>();
    public DbSet<FactoryDemand> FactoryDemands => Set<FactoryDemand>();
    public DbSet<FactorySopReview> FactorySopReviews => Set<FactorySopReview>();
    public DbSet<FactoryApsRun> FactoryApsRuns => Set<FactoryApsRun>();
    public DbSet<FactoryMaterialUsage> FactoryMaterialUsages => Set<FactoryMaterialUsage>();
    public DbSet<FactoryCenterCostRate> FactoryCenterCostRates => Set<FactoryCenterCostRate>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<PlanningItem>().HasIndex(x => x.Code).IsUnique();
        model.Entity<PlanningItem>().Property(x => x.Code).HasMaxLength(64);
        model.Entity<PlanningItem>().Property(x => x.Name).HasMaxLength(256);
        model.Entity<PlanningItem>().Property(x => x.Unit).HasMaxLength(24);
        model.Entity<WorkCenter>().HasIndex(x => x.Code).IsUnique();
        model.Entity<WorkCenter>().Property(x => x.Code).HasMaxLength(64);
        model.Entity<WorkCenter>().Property(x => x.Name).HasMaxLength(256);
        model.Entity<WorkCenter>().Property(x => x.NominalUnitsPerHour).HasPrecision(18, 4);
        model.Entity<BomComponent>().HasIndex(x => new { x.ParentItemId, x.ComponentItemId }).IsUnique();
        model.Entity<BomComponent>().Property(x => x.QuantityPer).HasPrecision(18, 6);
        model.Entity<BomComponent>().Property(x => x.ScrapPercent).HasPrecision(8, 4);
        model.Entity<BomComponent>().HasOne<PlanningItem>().WithMany().HasForeignKey(x => x.ParentItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<BomComponent>().HasOne<PlanningItem>().WithMany().HasForeignKey(x => x.ComponentItemId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductionPlan>().Property(x => x.Quantity).HasPrecision(18, 4);
        model.Entity<ProductionPlan>().HasOne<PlanningItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        ManufacturingFoundationEndpoints.ConfigureManufacturingModel(model);
        PlanningStageThreeEndpoints.ConfigureModel(model);
        ManufacturingExecutionEndpoints.ConfigureModel(model);
        ManufacturingQualityAndOeeEndpoints.ConfigureModel(model);
        ManufacturingControlPlanEndpoints.ConfigureModel(model);
        ManufacturingOrderPegging.ConfigureModel(model);
        ProductionDeviationEndpoints.ConfigureModel(model);
        FactoryDemandApsEndpoints.ConfigureModel(model);
        FactoryOperationsAnalyticsEndpoints.ConfigureModel(model);
    }
}
public enum PlanningItemType { RawMaterial, SemiFinished, Finished, Consumable }
public sealed class PlanningItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "PCS";
    public PlanningItemType Type { get; set; }
}
public sealed class WorkCenter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal NominalUnitsPerHour { get; set; }
}
public sealed class BomComponent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParentItemId { get; set; }
    public Guid ComponentItemId { get; set; }
    public decimal QuantityPer { get; set; }
    public decimal ScrapPercent { get; set; }
    public int Sequence { get; set; }
}
public sealed class ProductionPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime DueDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public record ItemInput(string Code, string Name, string Unit, PlanningItemType Type);
public record WorkCenterInput(string Code, string Name, decimal NominalUnitsPerHour);
public record BomInput(Guid ParentItemId, Guid ComponentItemId, decimal QuantityPer, decimal ScrapPercent, int Sequence);
public record PlanInput(Guid ItemId, decimal Quantity, DateTime DueDateUtc);
