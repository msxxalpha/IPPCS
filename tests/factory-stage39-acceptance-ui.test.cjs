const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const read=p=>fs.readFileSync(path.join(__dirname,"../src/IPPCS.Web",p),"utf8");
const ui=read("wwwroot/factory-release-readiness.html");
const js=read("wwwroot/js/factory-release-readiness.js");
const code=read("FactoryReleasePriorityQueue.cs");
const eol=read("FactoryEndOfLineAcceptance.cs");

test("readiness page makes ECO/CAPA/EOL/APS exceptions navigable but never treats them as acceptance",()=>{
 for(const id of ["priority-total","priority-critical","priority-high",
  "priority-refresh","priority-list","priority-disclaimer"])
  assert.ok(ui.includes('id="'+id+'"'),id);
 assert.match(js,/factory-release-readiness\/priority-queue/);
 assert.match(js,/productionReleaseAuthorized!==false/);
 assert.match(js,/factoryAcceptedModules!==0/);
 assert.match(js,/textContent/);
 assert.doesNotMatch(js,/\.innerHTML\s*=|insertAdjacentHTML/);
});
test("read-only priority queue consumes recorded ECO CAPA EOL and APS evidence",()=>{
 assert.match(code,/RequireAuthorization\("IpProductionUser"\)/);
 for(const text of ["FactoryEcoApprovals","ProductionDeviations","ManufacturingOrders",
   "CertificateMatches","FactoryApsRuns","IndependentlyVerified","productionReleaseAuthorized=false"])
   assert.ok(code.includes(text),text);
 assert.match(code,/Take\(501\)/);
 assert.match(code,/Conflict/);
 assert.match(eol,/SHA256=/);
 assert.match(eol,/CreateCertificate\(actor,finalLots\)/);
 assert.doesNotMatch(code,/MapPost\(|MapPut\(|MapDelete\(/);
});
