const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const read=name=>fs.readFileSync(path.resolve(
  __dirname,"../src/IPPCS.Web/wwwroot",name),"utf8");
const readiness=read("factory-release-readiness.html");
const script=read("js/factory-release-readiness.js");
const app=read("app.html"),production=read("js/production.js");
const catalog=read("factory-master-data.html");
const plan=read("control-plan.html");
const qa=read("js/control-plan.js");

test("factory first-run page exposes non-misleading 18-module UAT preparation",()=>{
 assert.match(readiness,/<html lang="fa" dir="rtl">/);
 assert.match(readiness,/id="readiness-setup"/);
 assert.match(readiness,/id="readiness-modules"/);
 assert.match(readiness,/id="readiness-refresh"/);
 assert.match(app,/href="\/factory-release-readiness.html"/);
 assert.match(script,/productionReleaseAuthorized!==false/);
 assert.match(script,/factoryAcceptedModules!==0/);
 assert.match(script,/data\.modules\.length!==18/);
 assert.match(script,/secureFetch/);
 assert.doesNotMatch(script,/\.innerHTML\s*=|insertAdjacentHTML/);
});
test("home cards link all modules to actual test screens without fake plant signoff",()=>{
 assert.match(production,/factory-module-link/);
 assert.match(production,/UAT باز/);
 assert.doesNotMatch(production,/\["مدیریت تقاضای تولید"[^\n]*"در دست توسعه"/);
 assert.match(app,/پذیرش نهایی ثبت‌شدهٔ کارخانه/);
});
test("factory item and control-plan units use catalog-managed searchable selects",()=>{
 assert.match(app,/id="item-unit" name="unit" required/);
 assert.match(plan,/id="quality-characteristic-unit"/);
 assert.match(catalog,/<option value="UnitOfMeasure">/);
 assert.match(production,/factory-master-options\?kind=UnitOfMeasure/);
 assert.match(qa,/factory-master-options\?kind=UnitOfMeasure/);
 assert.match(production,/IPPCSChoices\.fill/);
 assert.match(qa,/IPPCSChoices\.fill/);
});
