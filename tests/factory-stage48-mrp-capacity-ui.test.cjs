const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const html=fs.readFileSync(path.join(root,"planning-advanced.html"),"utf8");
const js=fs.readFileSync(path.join(root,"js/planning-advanced.js"),"utf8");
const stageThreeHtml=fs.readFileSync(path.join(root,"planning.html"),"utf8");
const stageThreeJs=fs.readFileSync(path.join(root,"js/planning-stage3.js"),"utf8");

test("net MRP lot capacity preview is reachable from the planning workspace",()=>{
 for(const id of ["mrp-capacity-preview-form","mrp-capacity-summary",
  "mrp-capacity-lots","mrp-capacity-allocations","mrp-capacity-unscheduled"])
  assert.match(html,new RegExp('id="'+id+'"'),"missing MRP capacity UI element #"+id);
 assert.match(html,/data-mps required/);
 assert.match(html,/name="horizon" type="number" min="1" max="90"/);
 assert.match(html,/name="capacity" type="number" min="0.1" max="2"/);
 assert.match(js,/addForm\("#mrp-capacity-preview-form"/);
 assert.match(js,/\/mrp\/".*lot-capacity-preview/);
});

test("MRP capacity preview displays lot-level status, dates, allocations and unscheduled reasons",()=>{
 assert.match(html,/id="mrp-capacity-evidence"/);
 for(const text of ["r.productCapacityEvidence","routingRevisionCode","x.routingRunMinutesPerUnit",
  "x.effectiveRunMinutesPerUnit","x.capacityUnitsPerHour","x.hasProductCapacityOverride",
  "r.onTimeLots","r.lateLots","r.blockedLots","r.activeCapacityReservations",
  "r.runningOperationClockSessions","r.lots","r.allocations","r.unscheduled",
  "proposedReleaseJalali","receiptDueJalali","operationsScheduled","r.disclaimer"])
  assert.ok(js.includes(text),"missing preview result binding: "+text);
 assert.match(js,/در انتظار/);
 assert.match(js,/زمان‌بندی‌نشده/);
});

test("CRP exposes product-dependent output capacity from route setup, unit time and yield",()=>{
 assert.match(html,/id="crp-product-capacity-table"/);
 assert.match(js,/\/crp\/".*product-capacity-preview/);
 for(const field of ["x.setupMinutes","x.runMinutesPerUnit","x.yieldPercent",
  "x.standaloneCapacityUnits","x.requiredMinutes","x.availableMinutes"])
  assert.ok(js.includes(field),"missing product-capacity result field "+field);
 assert.match(html,/ظرفیت خروجی وابسته به محصول و عملیات/);
 assert.match(html,/ظرفیت ترکیبی قابل‌تعهد نیست/);
});

test("MRP capacity preview stays read-only and does not create firm production orders",()=>{
 const form=html.slice(html.indexOf('id="mrp-capacity-preview-form"'),
  html.indexOf('id="mrp-capacity-summary"'));
 assert.match(form,/پیش‌نمایش/);
 assert.match(form,/دستور ساخت یا رزرو واقعی ایجاد نمی‌کند/);
 assert.doesNotMatch(js,/api\("\/manufacturing-orders".*POST/);
});

test("product-dependent CRP is directly reachable from the MPS/MRP/CRP form",()=>{
 assert.match(stageThreeHtml,/id="product-capacity-run"/);
 assert.match(stageThreeHtml,/id="product-capacity-summary"/);
 assert.match(stageThreeHtml,/id="product-capacity-table"/);
 assert.match(stageThreeJs,/\$\("#product-capacity-run"\)\.onclick/);
 assert.match(stageThreeJs,/\/crp\/"\+id\+"\/product-capacity-preview/);
 for(const field of ["r.setupMinutes","r.runMinutesPerUnit","r.yieldPercent",
  "r.productCapacityUnitsPerHour","r.hasProductCapacityOverride",
  "r.requiredMinutes","r.availableMinutes","r.standaloneCapacityUnits"])
  assert.ok(stageThreeJs.includes(field),"missing product-capacity display field "+field);
});
