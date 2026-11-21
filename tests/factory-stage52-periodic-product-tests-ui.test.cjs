const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const read=p=>fs.readFileSync(path.join(root,p),"utf8");
const html=read("periodic-tests.html");
const js=read("js/periodic-tests.js");
const app=read("app.html");
const source=fs.readFileSync(path.resolve(root,"../FactoryPeriodicProductTests.cs"),"utf8");
const migration=fs.readFileSync(path.resolve(root,"../Migrations/CI/20261029120000_StageFiftyTwoPeriodicProductTests.cs"),"utf8");
const snapshot=fs.readFileSync(path.resolve(root,"../Migrations/CI/ProductionDbModelSnapshot.cs"),"utf8");

test("periodic and annual product tests are accessible as one dedicated page in the grouped test menu",()=>{
 assert.match(app,/<div class="nav-group-title">۵\. آزمایش‌ها و تست‌های دوره‌ای\/سالانه<\/div>/);
 assert.ok(app.includes('href="/periodic-tests.html"'));
 assert.match(html,/<html lang="fa" dir="rtl">/);
 assert.match(html,/<title>IPPCS \| برنامه آزمون‌های ادواری و سالانه محصول<\/title>/);
 for(const id of ["test-plan-form","test-range-form","test-generate-due",
  "test-refresh-runs","test-result-form","test-plan-table","test-run-table"])
  assert.ok(html.includes('id="'+id+'"'),"missing test workflow UI element #"+id);
});

test("UI is wired to internal planning-only APIs and shared Jalali/unit controls",()=>{
 for(const asset of ["/js/security-client.js","/js/jalali-datepicker.js",
  "/js/factory-choices.js","/js/periodic-tests.js"])
  assert.ok(html.includes('src="'+asset+'"'),"missing page asset "+asset);
 for(const endpoint of ["/periodic-product-tests/plans","/periodic-product-tests/schedule",
  "/periodic-product-tests/runs","/periodic-product-tests/runs/"])
  assert.ok(js.includes(endpoint),"missing client endpoint "+endpoint);
 assert.match(js,/IPPCSJalali\.parse/);
 assert.match(js,/kind=UnitOfMeasure/);
 assert.match(html,/data-jalali-trigger/);
 assert.match(js,/interval\.readOnly=annual/);
 assert.match(js,/interval\.max=annual\?"12":"11"/);
 assert.match(js,/interval\.disabled=false/,"annual cadence must remain included in FormData");
});

test("Jalali recurrence, annual cadence and idempotent schedule rules are implemented server-side",()=>{
 assert.match(source,/public static DateOnly AddPersianMonths/);
 assert.match(source,/public static IReadOnlyList<DateOnly> DueDates/);
 assert.match(source,/FactoryPeriodicTestCadence\.Annual/);
 assert.match(source,/alreadyExisted = skipped/);
 assert.match(source,/IsolationLevel\.Serializable/);
 assert.match(source,/IPPCS:PeriodicProductTestSchedule/);
 assert.match(source,/HasIndex\(x => new \{ x\.PlanId, x\.DueOn \}\)\.IsUnique\(\)/);
});

test("numerical test results cannot pass beyond specification limits and completed evidence is immutable",()=>{
 assert.match(source,/ValidateOutcome\(plan, input\.MeasuredValue, outcome, notes\)/);
 assert.match(source,/نتیجه نمی‌تواند قبول یا نامعین باشد/);
 assert.match(source,/نتیجه آزمون فقط یک بار و پیش از تکمیل رخداد ثبت می‌شود/);
 assert.match(source,/RecordedBy = http\.User\.Identity/);
 assert.match(source,/Outcome = outcome/);
});

test("new SQL tables, constraints and EF snapshot are aligned, without external-system dependencies",()=>{
 for(const table of ["FactoryPeriodicTestPlans","FactoryPeriodicTestRuns"]){
  assert.ok(migration.includes('name: "'+table+'"'),"missing migration table "+table);
  assert.ok(snapshot.includes('ToTable("'+table+'")'),"missing model snapshot entity "+table);
 }
 assert.match(migration,/IX_FactoryPeriodicTestRuns_PlanId_DueOn/);
 assert.match(snapshot,/HasIndex\("PlanId", "DueOn"\)\s*\.IsUnique\(\)/);
 assert.match(source,/Calibration remains outside IPPCS/);
 assert.equal(source.includes("http://") || source.includes("https://"),false,
  "the test workflow must not add external-system HTTP integrations");
});
