const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const read=p=>fs.readFileSync(path.join(root,p),"utf8");
const app=read("app.html");
const production=read("js/production.js");
const crp=fs.readFileSync(path.resolve(root,"../PlanningStageThree.cs"),"utf8");
const capacity=fs.readFileSync(path.resolve(root,"../WorkCenterProductCapacity.cs"),"utf8");
const aps=fs.readFileSync(path.resolve(root,"../FactoryDemandSopAps.cs"),"utf8");
const mpsChange=fs.readFileSync(path.resolve(root,"../FactoryMpsChangeControl.cs"),"utf8");
const program=fs.readFileSync(path.resolve(root,"../Program.cs"),"utf8");
const migration=fs.readFileSync(path.resolve(root,"../Migrations/CI/20261029130000_StageFiftyThreeWorkCenterProductCapacities.cs"),"utf8");
const snapshot=fs.readFileSync(path.resolve(root,"../Migrations/CI/ProductionDbModelSnapshot.cs"),"utf8");

test("the work-center form now defines capacity by product, routing revision, operation and center",()=>{
 assert.match(app,/<section id="centers"/);
 for(const id of ["center-product-capacity-form","center-cap-item","center-cap-center",
  "center-cap-operation","center-cap-rate","center-cap-active","center-product-capacity-list"])
  assert.ok(app.includes('id="'+id+'"'),"missing center product-capacity UI element #"+id);
 assert.match(app,/ظرفیت محصول‌محور هر مرکز کاری/);
 assert.match(app,/ظرفیت عبوری این محصول در عملیات \(واحد\/ساعت\)/);
 assert.match(app,/برای هر عملیات مشخص، ظرفیت عبوری همان محصول/);
});

test("the center page creates and edits capacity overrides using the secured internal API",()=>{
 assert.ok(production.includes("/api/v1/work-center-product-capacities"));
 assert.ok(production.includes("state.productCapacities"));
 assert.ok(production.includes("updateProductCapacityOperations"));
 assert.ok(production.includes("capacityUnitsPerHour"));
 assert.ok(production.includes('existing.id,"PUT",payload'));
 assert.ok(production.includes('"/api/v1/work-center-product-capacities","POST",payload'));
 assert.ok(production.includes("isActive:!x.isActive"));
 assert.match(capacity,/RequireAuthorization\("IpProductionUser"\)/);
 assert.match(capacity,/RequireAuthorization\("IpPlanningWrite"\)/);
});

test("a product capacity is uniquely attached to a real routing operation and cannot be zero",()=>{
 assert.match(capacity,/HasIndex\(x => x\.RoutingOperationId\)\.IsUnique\(\)/);
 assert.match(capacity,/HasOne<RoutingOperation>\(\)\.WithMany\(\)/);
 assert.match(capacity,/input\.CapacityUnitsPerHour is <= 0 or > 1_000_000m/);
 assert.match(capacity,/effectiveRunMinutesPerUnit = 60m \/ row\.CapacityUnitsPerHour/);
 assert.match(capacity,/LoadActiveRates/);
 assert.match(capacity,/ApplyToRoutes/);
});

test("CRP loads product capacity overrides and reports the effective product rate and rate source",()=>{
 assert.match(crp,/LoadActiveRates\(db,ct\)/);
 assert.match(crp,/RunProductCapacityCrp\(version\.Lines,routes,shifts,productCapacityRates\)/);
 assert.match(crp,/RunRoughCutCrp\(version\.Lines,routes,shifts,productCapacityRates\)/);
 assert.match(crp,/HasProductCapacityOverride/);
 assert.match(read("js/planning-stage3.js"),/r\.productCapacityUnitsPerHour/);
 assert.match(read("js/planning-stage3.js"),/r\.hasProductCapacityOverride/);
});

test("S&OP, finite APS and MPS revision impact use the same active product rates",()=>{
 assert.equal((aps.match(/WorkCenterProductCapacityRules\.ApplyToRoutes\(routes,capacityRates\)/g)||[]).length,2);
 assert.match(mpsChange,/LoadActiveRates\(db,ct\)/);
 assert.match(mpsChange,/RunRoughCutCrp\(source\.Lines,routings,shifts,productCapacityRates\)/);
 assert.match(mpsChange,/RunRoughCutCrp\(candidate\.Lines,routings,shifts,productCapacityRates\)/);
});

test("the application and SQL migration carry the same per-operation capacity table and unique key",()=>{
 assert.match(program,/DbSet<WorkCenterProductCapacity>/);
 assert.match(program,/WorkCenterProductCapacityRules\.ConfigureModel\(model\)/);
 assert.match(program,/app\.MapWorkCenterProductCapacities\(\)/);
 assert.match(migration,/name: "WorkCenterProductCapacities"/);
 assert.match(migration,/principalTable: "RoutingOperations"/);
 assert.match(migration,/unique: true/);
 assert.match(snapshot,/ToTable\("WorkCenterProductCapacities"\)/);
 assert.match(snapshot,/HasIndex\("RoutingOperationId"\)\s*\.IsUnique\(\)/);
});
