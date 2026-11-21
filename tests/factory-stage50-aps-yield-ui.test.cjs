const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const read=p=>fs.readFileSync(path.join(root,p),"utf8");
const html=read("planning-advanced.html");
const js=read("js/planning-advanced.js");
const endpoint=fs.readFileSync(path.resolve(root,"../FactoryDemandSopAps.cs"),"utf8");
const sizing=fs.readFileSync(path.resolve(root,"../FactoryApsTaskExpansion.cs"),"utf8");

test("APS explicitly discloses cumulative-yield sizing and displays quantity processed at each operation",()=>{
 assert.match(html,/بازده تجمعی عملیات پایین‌دست/);
 assert.ok(js.includes('["مقدار ورودی مرحله",x=>format(x.quantity)]'));
});

test("APS endpoint routes every approved MPS line through cumulative-yield task expansion",()=>{
 assert.ok(endpoint.includes("FactoryApsTaskExpansion.Expand(line,active[0])"));
 assert.ok(!endpoint.includes("line.Quantity*100m/op.YieldPercent"));
});

test("upstream APS operation quantities divide by the yield of this and every downstream operation",()=>{
 assert.ok(sizing.includes("cumulativeYield *= operations[j].YieldPercent / 100m;"));
 assert.ok(sizing.includes("var operationQuantity = line.Quantity / cumulativeYield;"));
 assert.ok(sizing.includes("operation.RunMinutesPerUnit * operationQuantity"));
 assert.ok(sizing.includes("operationQuantity, route.Id, operation.Sequence"));
});
