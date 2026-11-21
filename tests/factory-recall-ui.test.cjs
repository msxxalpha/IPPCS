const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const page=fs.readFileSync(path.resolve(__dirname,"../src/IPPCS.Web/wwwroot/factory-recall.html"),"utf8");
const js=fs.readFileSync(path.resolve(__dirname,"../src/IPPCS.Web/wwwroot/js/factory-recall.js"),"utf8");
const app=fs.readFileSync(path.resolve(__dirname,"../src/IPPCS.Web/wwwroot/app.html"),"utf8");
test("read-only recall page is discoverable and RTL with result containers",()=>{
 assert.match(page,/<html lang="fa" dir="rtl">/);
 assert.match(page,/id="recall-form"/);
 assert.match(page,/id="recall-lots"/);
 assert.match(page,/id="recall-disclaimer"/);
 assert.match(app,/href="\/factory-recall.html"/);
});
test("recall UI uses authenticated fetch, safe DOM and warnings for incomplete evidence",()=>{
 assert.match(js,/IPPCSSecurity\.secureFetch/);
 assert.match(js,/textContent/);
 assert.doesNotMatch(js,/\.innerHTML\s*=|insertAdjacentHTML/);
 assert.match(js,/unserializedGood/);
 assert.match(js,/results"\)\.hidden=true/);
});
