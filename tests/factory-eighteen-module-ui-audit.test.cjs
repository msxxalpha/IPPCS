const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const pages=[
 "app.html","planning.html","planning-advanced.html","mps-pegging.html",
 "execution.html","control-plan.html","engineering-rebind.html",
 "factory-recall.html","production-deviations.html",
 "factory-master-data.html","factory-release-readiness.html",
 "factory-analytical-verification.html","factory-reports.html","system-users.html","login.html"
];
const text=file=>fs.readFileSync(path.join(root,file),"utf8");
test("all 14 entry pages serving the 18 modules are Persian RTL, accessible and load real assets",()=>{
 for(const file of pages){
  const html=text(file);
  assert.match(html,/<html[^>]*lang="fa"/,file);
  assert.match(html,/<html[^>]*dir="rtl"/,file);
  assert.match(html,/<meta[^>]*viewport/,file);
  assert.match(html,/<title>[^<]+<\/title>/,file);
  for(const src of html.matchAll(/<script[^>]*src="(\/[^"]+)"/g)){
   assert.ok(fs.existsSync(path.join(root,src[1].slice(1))),
    file+" references missing script "+src[1]);
  }
  for(const src of html.matchAll(/href="(\/[^"#?]+\.html)(?:#[^"]*)?"/g)){
   assert.ok(fs.existsSync(path.join(root,src[1].slice(1))),
    file+" references missing page "+src[1]);
  }
 }
});
test("all 18 modules are explicitly declared and link to existing test screens",()=>{
 const source=fs.readFileSync(path.resolve(root,"../FactoryReleaseReadiness.cs"),"utf8");
 const match=[...source.matchAll(/new\((\d+),"[^"]+","(\/[^"]+\.html)"/g)];
 assert.equal(match.length,18,"each of the 18 modules needs a navigable UI route");
 assert.deepEqual(match.map(x=>Number(x[1])),Array.from({length:18},(_,i)=>i+1));
 for(const x of match)assert.ok(fs.existsSync(path.join(root,x[2].slice(1))),x[2]);
 assert.match(source,/productionReleaseAuthorized=false/);
 assert.match(source,/factoryAcceptedModules=0/);
});
test("factory entry forms select audit-critical codes from managed basic-data lists",()=>{
 const execution=text("execution.html"),planning=text("planning-advanced.html");
 const qa=text("control-plan.html"),deviations=text("production-deviations.html");
 const main=text("app.html");
 const forms=[
  [main,/id="item-unit" name="unit"/],
  [qa,/id="quality-characteristic-unit"/],
  [execution,/id="stop-reason-code"/],
  [execution,/id="timed-stop-reason-code"/],
  [execution,/id="quality-reason-code"/],
  [planning,/id="aps-blackout-reason-code"/],
  [deviations,/id="action-owner"/],
  [deviations,/id="cause-group"/],
  [deviations,/id="revise-owner"/],
  [deviations,/id="revise-cause-group"/]
 ];
 for(const [document,selector] of forms)assert.match(document,selector);
 for(const p of ["app.html","execution.html","control-plan.html",
                  "planning-advanced.html","production-deviations.html"]){
  assert.match(text(p),/src="\/js\/factory-choices\.js"/,p);
 }
 const js=text("js/planning-advanced.js");
 assert.match(js,/CapacityBlackoutReason/);
 assert.match(js,/reasonCode/);
 assert.doesNotMatch(js,/\$\("#aps-blackout-reason"\)/);
});
