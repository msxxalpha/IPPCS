const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web");
const read=x=>fs.readFileSync(path.join(root,x),"utf8");

test("professional source-linked Persian RTL print documents cover all required operations",()=>{
 const html=read("wwwroot/factory-print.html");
 const css=read("wwwroot/css/factory-print.css");
 const js=read("wwwroot/js/factory-print.js");
 const api=read("FactoryPrintDocuments.cs");
 for(const id of ["print-document","print-action","print-title","print-status",
  "print-user","print-meta","print-tables","print-disclaimer","print-watermark"])
  assert.ok(html.includes('id="'+id+'"'),id);
 assert.match(html,/dir="rtl"/);
 assert.match(css,/@page ippcs-report/);
 assert.match(css,/A3 landscape/);
 assert.match(css,/@page ippcs-order/);
 assert.match(css,/display:table-header-group/);
 assert.match(css,/break-inside:avoid/);
 assert.match(css,/print-color-adjust:exact/);
 assert.match(css,/signoff-grid/);
 assert.match(api,/RequireAuthorization\("IpProductionUser"\)/);
 for(const kind of ['"order"','"mps"','"plan"','"bom"','"routing"','"control-plan"'])
  assert.ok(api.includes("case "+kind+":"),"No print contract: "+kind);
 for(const kind of ['"report"','"aps"','"eol"'])
  assert.ok(js.includes(kind),"No client print workflow: "+kind);
 assert.doesNotMatch(js,/\.innerHTML\s*=|insertAdjacentHTML/);
 assert.match(js,/textContent/);
 assert.match(js,/officiallyApproved/);
 assert.match(js,/window\.print/);
});
test("print actions are wired to actual source data and not blank print pages",()=>{
 const files={
  "wwwroot/js/execution.js":["نسخه چاپی دستور","kind:\"order\""],
  "wwwroot/js/planning-stage3.js":["#print-mps-select","#print-bom-select"],
  "wwwroot/js/production.js":["#print-routing-button","#print-plan-button"],
  "wwwroot/js/control-plan.js":["#print-control-plan-button","#print-eol-certificate"],
  "wwwroot/js/factory-reports.js":["report-print","dataset.fromJalali"],
  "wwwroot/js/planning-advanced.js":["#print-aps-select"]
 };
 for(const [file,terms] of Object.entries(files)){
  const content=read(file);
  for(const term of terms)assert.ok(content.includes(term),file+" missing "+term);
 }
});
