const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web");
const read=file=>fs.readFileSync(path.join(root,file),"utf8");
const html=read("wwwroot/control-plan.html");
const js=read("wwwroot/js/control-plan.js");
const api=read("FactorySpcEvidence.cs");
test("quality screen exposes audited sample evidence with explicit no-MSA warning",()=>{
 for(const id of ["spc-evidence-summary","spc-evidence-findings","spc-evidence-signals"])
  assert.ok(html.includes('id="'+id+'"'),id);
 assert.match(html,/Gage R&amp;R/);
 assert.match(js,/\/spc\/".*\/evidence/);
 assert.match(js,/evidence\.signals/);
 assert.match(js,/evidence\.findings/);
 assert.doesNotMatch(js,/\.innerHTML\s*=|insertAdjacentHTML/);
});
test("SPC evidence is read only, role protected and bounded; never calls it final capability",()=>{
 assert.match(api,/RequireAuthorization\("IpProductionUser"\)/);
 assert.match(api,/Take\(501\)/);
 assert.match(api,/MinimumReferenceSamples=25/);
 assert.match(api,/MeasurementSystemVerified/);
 assert.match(api,/CapabilityApproved/);
 assert.match(api,/EightOnOneSide/);
 assert.match(api,/SixPointTrend/);
 assert.match(api,/I3Sigma/);
 assert.doesNotMatch(api,/MapPost\(|MapPut\(|MapDelete\(/);
});

test("sampling coverage what-if uses real inspection counts but cannot approve policy, MSA, or release",()=>{
 const page=read("wwwroot/control-plan.html");
 const script=read("wwwroot/js/control-plan.js");
 const api=read("FactorySamplingCoverage.cs");
 for(const id of ["sampling-every-units","sampling-coverage-check","sampling-coverage-summary"])
  assert.ok(page.includes('id="'+id+'"'),id);
 assert.match(script,/samplingCoverage/);
 assert.match(script,/sampling-coverage/);
 assert.match(api,/RequireAuthorization\("IpProductionUser"\)/);
 assert.match(api,/Take\(501\)/);
 for(const flag of ["PerLotSamplingVerified","SamplingPolicyApproved",
   "MeasurementSystemVerified"])assert.ok(api.includes(flag));
 assert.doesNotMatch(api,/MapPost\(|MapPut\(|MapDelete\(/);
});
