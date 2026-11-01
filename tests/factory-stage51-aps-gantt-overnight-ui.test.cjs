const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const gantt=require(path.join(root,"js/aps-gantt-segments.js"));
const html=fs.readFileSync(path.join(root,"planning-advanced.html"),"utf8");
const js=fs.readFileSync(path.join(root,"js/planning-advanced.js"),"utf8");
const api=fs.readFileSync(path.resolve(root,"../FactoryDemandSopAps.cs"),"utf8");

function allocation(overrides={}){
 return {
  itemId:"item-1",workCenterId:"center-1",sequence:10,quantity:100,
  operationName:"Overnight operation",durationMinutes:480,late:false,
  startDateIso:"2026-10-29",finishDateIso:"2026-10-30",
  startJalali:"1405/08/07",finishJalali:"1405/08/08",
  startTime:"22:00",finishTime:"06:00",...overrides
 };
}

test("overnight APS operation is visible on both days instead of a zero-width bar",()=>{
 const segments=gantt.buildSegments([allocation()]);
 assert.equal(segments.length,2);
 assert.deepEqual(segments.map(x=>x.dayIso),["2026-10-29","2026-10-30"]);
 assert.deepEqual(segments.map(x=>[x.segmentStartMinutes,x.segmentFinishMinutes]),[
  [1320,1440],[0,360]
 ]);
 assert.equal(segments[0].continuesFromPrevious,false);
 assert.equal(segments[0].continuesNext,true);
 assert.equal(segments[1].continuesFromPrevious,true);
 assert.equal(segments[1].continuesNext,false);
 assert.ok(segments.every(x=>x.segmentFinishMinutes>x.segmentStartMinutes));
});

test("a same-day operation remains one accurate segment",()=>{
 const segments=gantt.buildSegments([allocation({
  startDateIso:"2026-10-29",finishDateIso:"2026-10-29",
  startTime:"08:30",finishTime:"09:45",
  startJalali:"1405/08/07",finishJalali:"1405/08/07"
 })]);
 assert.equal(segments.length,1);
 assert.equal(segments[0].segmentStartMinutes,510);
 assert.equal(segments[0].segmentFinishMinutes,585);
 assert.equal(segments[0].continuesFromPrevious,false);
 assert.equal(segments[0].continuesNext,false);
});

test("multi-day operations are split into full-day middle segments",()=>{
 const segments=gantt.buildSegments([allocation({
  finishDateIso:"2026-10-31",finishJalali:"1405/08/09",finishTime:"03:00"
 })]);
 assert.equal(segments.length,3);
 assert.deepEqual(segments.map(x=>[x.segmentStartMinutes,x.segmentFinishMinutes]),[
  [1320,1440],[0,1440],[0,180]
 ]);
 assert.ok(segments[1].continuesFromPrevious&&segments[1].continuesNext);
});

test("an operation finishing exactly at midnight does not create a zero-width next-day bar",()=>{
 const segments=gantt.buildSegments([allocation({
  finishDateIso:"2026-10-30",finishJalali:"1405/08/08",finishTime:"00:00"
 })]);
 assert.equal(segments.length,1);
 assert.deepEqual([segments[0].segmentStartMinutes,segments[0].segmentFinishMinutes],[1320,1440]);
});

test("invalid or reversed timestamps fail rather than rendering a misleading Gantt",()=>{
 assert.throws(()=>gantt.buildSegments([allocation({
  startDateIso:"2026-10-30",finishDateIso:"2026-10-29"
 })]));
 assert.throws(()=>gantt.buildSegments([allocation({
  startDateIso:"2026-10-29",finishDateIso:"2026-10-29",
  startTime:"14:00",finishTime:"12:00"
 })]));
});

test("APS endpoint returns ISO dates and the UI uses day segments for Gantt bars",()=>{
 assert.ok(html.includes('/js/aps-gantt-segments.js'));
 assert.ok(api.includes("startDateIso=x.StartDate.ToString"));
 assert.ok(api.includes("finishDateIso=x.FinishDate.ToString"));
 assert.ok(js.includes("window.IPPCSApsGantt.buildSegments(allocations)"));
 assert.ok(!js.includes("Math.max(0,(finish-start)"));
 assert.ok(js.includes("row.segmentStartMinutes"));
 assert.ok(js.includes("row.segmentFinishMinutes"));
});
