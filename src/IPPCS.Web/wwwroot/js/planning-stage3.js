(()=>{
"use strict";
const $=s=>document.querySelector(s), $$=s=>Array.from(document.querySelectorAll(s));
const state={items:[],centers:[],bom:[],routings:[],mps:[],balances:[],receipts:[]};
const val=(form,k)=>new FormData(form).get(k);
const number=(form,k)=>Number(IPPCSJalali.toLatin(val(form,k)));
const fmt=v=>Number(v).toLocaleString("fa-IR",{maximumFractionDigits:4});
const name=id=>state.items.find(x=>x.id===id)?.name||String(id).slice(0,8);
const stateName=value=>value===1?"تأییدشده":"پیش‌نویس";
function alertText(message,error=false){const el=$("#alert");el.hidden=false;el.className="notice"+(error?" error":"");el.textContent=message;el.scrollIntoView({behavior:"smooth",block:"nearest"});}
async function call(path,method="GET",body){
 const init={method,headers:{}};if(body!==undefined){init.headers["Content-Type"]="application/json";init.body=JSON.stringify(body);}
 const resp=await window.IPPCSSecurity.secureFetch("/api/v1"+path,init);
 const raw=await resp.text();let data;
 try{data=raw?JSON.parse(raw):null;}catch{data=null;}
 if(!resp.ok)throw new Error((data?.message||data?.detail||data?.title||raw||"خطای نامشخص")+" (HTTP "+resp.status+")");
 return data;
}
function options(el,rows,format){
 IPPCSChoices.fill(el,rows,{value:x=>x.id??x.itemId,label:format});
}
function table(target,columns,rows,action){
 const root=$(target);root.replaceChildren();
 const box=document.createElement("div");box.className="table-wrap";
 const t=document.createElement("table"),h=document.createElement("thead"),tr=document.createElement("tr");
 columns.forEach(([title])=>{const th=document.createElement("th");th.textContent=title;tr.append(th);});
 if(action){let th=document.createElement("th");th.textContent="عملیات";tr.append(th);}
 h.append(tr);t.append(h);const tb=document.createElement("tbody");
 if(!rows.length){const empty=document.createElement("tr"),td=document.createElement("td");td.colSpan=columns.length+(action?1:0);td.className="empty";td.textContent="داده‌ای ثبت نشده است.";empty.append(td);tb.append(empty);}
 for(const r of rows){const tr=document.createElement("tr");
  for(const [,fn] of columns){const td=document.createElement("td");const v=typeof fn==="function"?fn(r):r[fn];td.textContent=(v===null||v===undefined)?"—":String(v);tr.append(td);}
  if(action){const td=document.createElement("td"),b=document.createElement("button");b.className="btn-outline";b.type="button";b.textContent=action.text(r);b.disabled=action.disabled?.(r)||false;b.onclick=async()=>{b.disabled=true;try{await action.click(r);}catch(e){alertText(e.message,true);}finally{b.disabled=false;}};td.append(b);tr.append(td);}
  tb.append(tr);
 }
 t.append(tb);box.append(t);root.append(box);
}
function render(){
 $$("[data-item]").forEach(el=>options(el,state.items,x=>x.code+" | "+x.name));
 $$("[data-finished]").forEach(el=>options(el,state.items.filter(x=>x.type===2),x=>x.code+" | "+x.name));
 options($("#bom-revision-select"),state.bom.filter(x=>x.state===0),x=>name(x.parentItemId)+" / "+x.revision);
 options($("#revision-bom-check"),state.bom,x=>name(x.parentItemId)+" / "+x.revision);
 options($("#revision-routing-check"),state.routings,x=>name(x.itemId)+" / "+x.revisionCode);
 options($("#eco-bom-baseline"),state.bom.filter(x=>x.state===1),x=>name(x.parentItemId)+" / "+x.revision);
 options($("#eco-bom-candidate"),state.bom.filter(x=>x.state===0),x=>name(x.parentItemId)+" / "+x.revision);
 options($("#eco-route-baseline"),state.routings.filter(x=>x.state===1),x=>name(x.itemId)+" / "+x.revisionCode);
 options($("#eco-route-candidate"),state.routings.filter(x=>x.state===0),x=>name(x.itemId)+" / "+x.revisionCode);
 options($("#mps-draft-select"),state.mps.filter(x=>x.state===0),x=>x.code);
 options($("#mps-revision-source"),state.mps.filter(x=>x.state===1),x=>x.code);
 options($("#mps-comparison-source"),state.mps.filter(x=>x.state===1),x=>x.code);
 options($("#mps-comparison-candidate"),state.mps,x=>x.code+" / "+(x.state===1?"مصوب":"پیش‌نویس"));
 options($("#calculate-mps"),state.mps.filter(x=>x.state===1),x=>x.code);
 table("#bom-table",[
  ["محصول",r=>name(r.parentItemId)],["نسخه","revision"],["از","validFromJalali"],
  ["تا",r=>r.validUntilJalali||"نامحدود"],["وضعیت",r=>stateName(r.state)],
  ["اجزاء",r=>r.lines.map(x=>name(x.componentItemId)+": "+fmt(x.quantityPer)).join(" ، ")||"—"]
 ],state.bom,{text:r=>r.state===0?"تأیید نسخه":"قفل",disabled:r=>r.state!==0,click:async r=>{await call("/bom-revisions/"+r.id+"/approve","POST");alertText("نسخه BOM تأیید شد.");await load();}});
 table("#mps-table",[
  ["کد برنامه","code"],["وضعیت",r=>stateName(r.state)],
  ["تاریخ ایجاد",r=>new Date(r.createdUtc).toLocaleDateString("fa-IR-u-ca-persian")],
  ["ردیف‌ها",r=>r.lines.map(x=>name(x.itemId)+" × "+fmt(x.quantity)+" ("+x.neededOnJalali+")").join(" | ")||"—"]
 ],state.mps,{text:r=>r.state===0?"تأیید برنامه":"قفل",disabled:r=>r.state!==0,click:async r=>{await call("/mps/"+r.id+"/approve","POST");alertText("نسخه MPS تأیید شد.");await load();}});

 table("#bom-lines-table",[
  ["محصول",r=>name(r.parentItemId)],["نسخه","revision"],["جزء",r=>name(r.componentItemId)],
  ["مقدار",r=>fmt(r.quantityPer)],["ترتیب","sequence"]
 ],state.bom.filter(x=>x.state===0).flatMap(r=>r.lines.map(l=>({...l,parentItemId:r.parentItemId,revision:r.revision,revisionId:r.id}))),
 {text:()=>"حذف جزء",click:async r=>{
  if(!window.confirm("آیا جزء انتخاب‌شده از پیش‌نویس BOM حذف شود؟"))return;
  await call("/bom-revisions/"+r.revisionId+"/lines/"+r.id,"DELETE");
  alertText("جزء پیش‌نویس حذف شد.");await load();
 }});
 table("#mps-lines-table",[
  ["برنامه","code"],["محصول",r=>name(r.itemId)],["تاریخ نیاز","neededOnJalali"],["مقدار",r=>fmt(r.quantity)]
 ],state.mps.filter(x=>x.state===0).flatMap(v=>v.lines.map(l=>({...l,code:v.code,versionId:v.id}))),
 {text:()=>"حذف ردیف",click:async r=>{
  if(!window.confirm("آیا ردیف برنامه پیش‌نویس حذف شود؟"))return;
  await call("/mps/"+r.versionId+"/lines/"+r.id,"DELETE");
  alertText("ردیف برنامه پیش‌نویس حذف شد.");await load();
 }});
 table("#balance-table",[
  ["کالا",r=>name(r.itemId)],["موجودی فرضی",r=>fmt(r.physicalQuantity)],
  ["رزرو",r=>fmt(r.reservedQuantity)],["قرنطینه",r=>fmt(r.quarantinedQuantity)],
  ["ایمنی",r=>fmt(r.safetyQuantity)],["Lead days","leadDays"],
  ["بچ حداقل",r=>fmt(r.minimumBatch)],["بچ مضرب",r=>fmt(r.batchMultiple)],["مبنا","snapshotDateJalali"]
 ],state.balances);
 table("#receipt-table",[
  ["کالا",r=>name(r.itemId)],["تاریخ","availableOnJalali"],
  ["مقدار",r=>fmt(r.quantity)],["مرجع","reference"]
 ],state.receipts,{text:()=>"حذف دریافت",click:async r=>{
  if(!window.confirm("دریافت فرضی حذف شود؟ سوابق محاسبات قبلی محفوظ می‌مانند."))return;
  await call("/planning-receipts/"+r.id,"DELETE");
  alertText("دریافت فرضی حذف شد.");await load();
 }});
}
async function load(){
 const resources=await Promise.all([
  call("/items"),call("/work-centers"),call("/bom-revisions"),call("/routings"),call("/mps"),call("/planning-balances"),call("/planning-receipts")
 ]);
 ["items","centers","bom","routings","mps","balances","receipts"].forEach((k,i)=>state[k]=resources[i]);
 render();
}
function register(formId,fn){const form=$(formId);form.addEventListener("submit",async e=>{
 e.preventDefault();if(!form.reportValidity())return;
 const btn=form.querySelector("[type=submit]");btn.disabled=true;
 try{await fn(form);alertText("اطلاعات ثبت شد.");form.reset();await load();}
 catch(e){alertText(e.message,true);}finally{btn.disabled=false;}
});}
function boot(){
 $$("nav [data-section]").forEach(b=>b.onclick=()=>{
  $$("section.page").forEach(s=>s.hidden=s.id!==b.dataset.section);
  $$("nav [data-section]").forEach(btn=>btn.classList.toggle("nav-active",btn===b));
  $("#title").textContent=b.textContent;$("#alert").hidden=true;
 });
 async function revisionPreflight(kind){
  const id=$(kind==="bom"?"#revision-bom-check":"#revision-routing-check").value;
  if(!id){alertText("یک نسخه برای بررسی انتخاب کنید.",true);return;}
  const path=kind==="bom"?"/factory-revisions/bom/":"/factory-revisions/routings/";
  const report=await call(path+id+"/approval-check");
  table("#revision-preflight-table",[["کد","code"],["شرح","description"]],
   report.issues.length?report.issues:[{code:"آماده",description:"کنترل‌های نسخه برای تصویب مانعی پیدا نکردند."}]);
  alertText(report.canApprove?"نسخه برای تصویب آماده است.":"نسخه هنوز برای تصویب معتبر نیست.",!report.canApprove);
 }
 $("#revision-check-bom").onclick=()=>revisionPreflight("bom").catch(e=>alertText(e.message,true));
 $("#revision-check-routing").onclick=()=>revisionPreflight("routing").catch(e=>alertText(e.message,true));
 async function ecoImpact(type){
  const bom=type==="BOM",before=$(bom?"#eco-bom-baseline":"#eco-route-baseline").value,
    candidate=$(bom?"#eco-bom-candidate":"#eco-route-candidate").value;
  if(!before||!candidate||before===candidate)throw Error("نسخه پیشنهادی و نسخه مصوب مبنا را انتخاب کنید.");
  const route=bom?"/factory-engineering/bom/":"/factory-engineering/routing/";
  const report=await call(route+candidate+"/impact?baselineId="+encodeURIComponent(before));
  table("#eco-impact-summary",[
    ["شاخص","name"],["مقدار","value"]
  ],[
    {name:"نوع تغییر",value:report.kind},
    {name:"آمادگی تصویب عادی",value:report.readyForRegularApproval?"بله":"خیر"},
    {name:"همپوشانی بازه نسخه‌ها",value:report.overlappingEffectiveDates?"دارد":"ندارد"},
    {name:"دستورات در معرض تغییر",value:report.affectedOrders},
    {name:"دستورات آزادشده یا اجراشده",value:report.activeOrCompletedOrders}
  ]);
  table("#eco-impact-issues",[["کد","code"],["شرح","description"]],
    report.issues.length?report.issues:[
      {code:"بدون مانع تشخیص‌داده‌شده",description:"سنجش نسخه پیشنهادی هنوز معادل امضای مهندسی نیست."}
    ]);
  const rows=bom?report.parts.map(x=>({
    key:state.items.find(y=>y.id===x.componentId)?.name||x.componentId.slice(0,8),
    before:fmt(x.oldQuantity)+" / ضایعات "+fmt(x.oldScrapPercent),
    after:fmt(x.newQuantity)+" / ضایعات "+fmt(x.newScrapPercent),
    change:x.changeType
  })):report.operations.map(x=>({
    key:"مرحله "+x.sequence,
    before:x.oldCenter.slice(0,8)+" | "+fmt(x.oldMinutesPerUnit)+" دقیقه | بازده "+fmt(x.oldYield),
    after:x.newCenter.slice(0,8)+" | "+fmt(x.newMinutesPerUnit)+" دقیقه | بازده "+fmt(x.newYield),
    change:x.changeType
  }));
  table("#eco-impact-changes",[
    ["قطعه یا عملیات","key"],["مبنای مصوب","before"],["پیشنهاد جدید","after"],
    ["نوع تغییر","change"]
  ],rows);
  table("#eco-impact-orders",[
    ["شماره دستور","number"],["وضعیت","status"],
    ["تاریخ شروع",x=>new Date(x.plannedStart+"T12:00:00Z").toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})],
    ["ریسک","risk"]
  ],report.orders);
  alertText(report.disclaimer,!report.readyForRegularApproval);
 }
 $("#eco-bom-review").onclick=()=>ecoImpact("BOM").catch(e=>alertText(e.message,true));
 $("#eco-routing-review").onclick=()=>ecoImpact("Routing").catch(e=>alertText(e.message,true));
 register("#bom-version-form",f=>call("/bom-revisions","POST",{
  parentItemId:val(f,"parentItemId"),revision:val(f,"revision"),validFromJalali:val(f,"from"),
  validUntilJalali:val(f,"until")||null
 }));
 register("#bom-line-form",f=>call("/bom-revisions/"+val(f,"revisionId")+"/lines","POST",{
  componentItemId:val(f,"componentItemId"),quantityPer:number(f,"quantityPer"),
  scrapPercent:number(f,"scrapPercent"),sequence:number(f,"sequence")
 }));
 register("#mps-form",f=>call("/mps","POST",{code:val(f,"code")}));
 register("#mps-controlled-revision-form",async f=>{
  const base=val(f,"sourceId");
  const created=await call("/mps/"+base+"/controlled-revision","POST",{
   code:val(f,"code"),reason:val(f,"reason")
  });
  alertText("نسخه "+created.code+" با "+created.clonedLines+
   " ردیف و سابقه علت بازنگری ساخته شد. نسخه اصلی همچنان مصوب است.");
  await load();
  $("#mps-comparison-source").value=base;
  $("#mps-comparison-candidate").value=created.id;
 });
 register("#mps-change-impact-form",async f=>{
  const source=val(f,"sourceId"),candidate=val(f,"candidateId");
  if(source===candidate)throw Error("دو نسخه مستقل انتخاب کنید.");
  const [report,evidence]=await Promise.all([
   call("/mps/compare-change-impact?sourceId="+encodeURIComponent(source)+
    "&candidateId="+encodeURIComponent(candidate)),
   call("/mps/"+candidate+"/change-evidence")
  ]);
  table("#mps-impact-summary",[["شاخص","name"],["مقدار","value"]],[
    {name:"مقدار مبنای مصوب",value:fmt(report.beforeQuantity)},
    {name:"مقدار نسخه تغییر یافته",value:fmt(report.afterQuantity)},
    {name:"خالص تغییر مقدار",value:fmt(report.deltaQuantity)},
    {name:"تعداد اقلام/موعدهای تغییر یافته",value:report.demand.length}
  ]);
  table("#mps-impact-demand",[
   ["کالا",x=>name(x.itemId)],
   ["موعد",x=>new Date(x.neededOn+"T12:00:00Z").toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})],
   ["قبل",x=>fmt(x.beforeQuantity)],["بعد",x=>fmt(x.afterQuantity)],
   ["تغییر",x=>fmt(x.difference)]
  ],report.demand);
  table("#mps-impact-capacity",[
   ["مرکز",x=>state.centers.find(y=>y.id===x.workCenterId)?.name||x.workCenterId.slice(0,8)],
   ["موعد",x=>new Date(x.neededOn+"T12:00:00Z").toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})],
   ["بار قبل (دقیقه)",x=>fmt(x.beforeMinutes)],
   ["بار بعد",x=>fmt(x.afterMinutes)],["تغییر بار",x=>fmt(x.deltaMinutes)],
   ["اضافه‌بار قبل",x=>fmt(x.beforeOverloadMinutes)],
   ["اضافه‌بار بعد",x=>fmt(x.afterOverloadMinutes)]
  ],report.capacity);
  table("#mps-impact-material",[
   ["کالا",x=>name(x.itemId)],
   ["موعد",x=>new Date(x.neededOn+"T12:00:00Z").toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})],
   ["نیاز ناخالص قبل",x=>fmt(x.beforeGross)],["نیاز بعد",x=>fmt(x.afterGross)],
   ["اختلاف ناخالص",x=>fmt(x.grossDifference)],
   ["دریافت پیشنهادی قبل",x=>fmt(x.beforeSuggested)],
   ["دریافت پیشنهادی بعد",x=>fmt(x.afterSuggested)],
   ["اختلاف پیشنهادی",x=>fmt(x.suggestedDifference)]
  ],report.materials);
  const summaries=evidence.events.map(x=>{
    const source=x.original||{};
    return {type:x.algorithmVersion,created:x.calculatedUtc,
     actor:source.actor||source.approvedBy||"نامشخص",
     reason:source.reason||"امضای برنامه مصوب",
     count:source.originalLines?.length||source.lines?.length||0};
  });
  table("#mps-change-evidence",[
    ["سند","type"],["تاریخ",x=>new Date(x.created).toLocaleString("fa-IR-u-ca-persian",{timeZone:"Asia/Tehran"})],
    ["ثبت‌کننده","actor"],["علت","reason"],["تعداد ردیف مبنا","count"]
  ],summaries);
  alertText(report.disclaimer);
 });
 register("#mps-line-form",f=>call("/mps/"+val(f,"mpsId")+"/lines","POST",{
  itemId:val(f,"itemId"),neededOnJalali:val(f,"due"),quantity:number(f,"quantity"),reference:val(f,"reference")||null
 }));
 register("#balance-form",f=>call("/planning-balances/"+val(f,"itemId"),"PUT",{
  itemId:val(f,"itemId"),physicalQuantity:number(f,"physical"),reservedQuantity:number(f,"reserved"),
  quarantinedQuantity:number(f,"quarantined"),safetyQuantity:number(f,"safety"),leadDays:number(f,"lead"),
  minimumBatch:number(f,"min"),batchMultiple:number(f,"multiple"),snapshotDateJalali:val(f,"snapshot")
 }));
 register("#receipt-form",f=>call("/planning-receipts","POST",{
  itemId:val(f,"itemId"),availableOnJalali:val(f,"date"),quantity:number(f,"quantity"),reference:val(f,"reference")||null
 }));
 $("#mrp-run").onclick=async()=>{
  const id=$("#calculate-mps").value;if(!id){alertText("نسخه تأییدشده MPS را انتخاب کنید.",true);return;}
  const btn=$("#mrp-run");btn.disabled=true;
  try{
   const result=await call("/mrp/"+id+"/calculate","POST");
   $("#calculation-title").textContent="MRP خالص — شماره محاسبه: "+result.runId;
   table("#calculation-table",[
    ["کالا",r=>name(r.itemId)],["تاریخ نیاز","neededOnJalali"],
    ["ناخالص",r=>fmt(r.grossRequired)],["موجودی پیش از نیاز",r=>fmt(r.openingAvailable)],
    ["کسری خالص",r=>fmt(r.netShortage)],["مقدار پیشنهادی",r=>fmt(r.suggestedQuantity)],
    ["زمان آزادسازی",r=>r.suggestedReleaseJalali||"—"],["موجودی پایان",r=>fmt(r.closingAvailable)]
   ],result.rows);
   table("#mrp-suggestions",[
     ["قطعه",x=>name(x.itemId)],
     ["تاریخ نیاز","neededOnJalali"],
     ["کسری خالص",x=>fmt(x.netShortage)],
     ["دریافت پیشنهادی",x=>fmt(x.suggestedReceipt)],
     ["مازاد ناشی از بچ",x=>fmt(x.lotSizingSurplus)],
     ["تاریخ آزادسازی",x=>x.releaseJalali||"—"],
     ["هشدار",x=>x.warning||"—"]
   ],result.explanations||[]);
   alertText("نتیجه MRP با تاریخچه ورودی/خروجی ذخیره شد.");
  }catch(e){alertText(e.message,true);}finally{btn.disabled=false;}
 };
 $("#lot-capacity-run").onclick=async()=>{
  const id=$("#calculate-mps").value;
  if(!id){alertText("نسخه تأییدشده MPS را انتخاب کنید.",true);return;}
  const days=Number(IPPCSJalali.toLatin($("#lot-capacity-days").value));
  const factor=Number(IPPCSJalali.toLatin($("#lot-capacity-multiplier").value));
  if(!Number.isInteger(days)||days<1||days>90||!Number.isFinite(factor)||factor<=0||factor>2){
   alertText("افق ۱ تا ۹۰ روز و ضریب ظرفیت بیشتر از صفر تا ۲ وارد کنید.",true);return;
  }
  const button=$("#lot-capacity-run");button.disabled=true;
  try{
   const result=await call("/mrp/"+id+"/lot-capacity-preview?horizonDays="+days+"&capacityMultiplier="+factor);
   const summary=$("#lot-capacity-summary");
   summary.hidden=false;
   summary.textContent="مبنای شمسی: "+result.baselineJalali+
    " | در موعد: "+fmt(result.onTimeLots)+" بچ | دیرکرد: "+fmt(result.lateLots)+
    " بچ | مسدود یا قابل زمان‌بندی نیست: "+fmt(result.blockedLots)+
    " بچ | دستورهای فعال با اشغال محافظه‌کارانه: "+fmt(result.conservativelyBlockedActiveOrders||0)+
    " | بازه‌های واقعی ثبت‌شده: "+fmt(result.recordedOperationClockSessions||0)+
    " | ساعت‌های در حال اجرا: "+fmt(result.runningOperationClockSessions||0)+
    " | رزروهای فعال مراکز: "+fmt(result.activeCapacityReservations||0);
   table("#lot-capacity-lots",[
    ["شناسه بچ","lotKey"],["کالا",x=>name(x.itemId)],["تعداد",x=>fmt(x.quantity)],
    ["شروع پیشنهادی","proposedReleaseJalali"],["موعد ساخت","receiptDueJalali"],
    ["تعداد عملیات",x=>fmt(x.operationsRequired)],["عملیات تخصیص‌یافته",x=>fmt(x.operationsScheduled)],
    ["وضعیت",x=>({OnTime:"قابل اجرا در موعد (پیش‌نمایش)",Late:"با تأخیر",Blocked:"مسدود",Unscheduled:"خارج از ظرفیت"})[x.status]||x.status],
    ["علت","reason"]
   ],result.lots);
   table("#lot-capacity-operations",[
    ["بچ پیشنهادی","lotKey"],["قطعه",x=>name(x.itemId)],
    ["مرکز کاری",x=>state.centers.find(c=>c.id===x.workCenterId)?.name||x.workCenterId.slice(0,8)],
    ["عملیات","operationName"],["شماره مرحله","sequence"],
    ["دقایق",x=>fmt(x.durationMinutes)],["تاریخ شروع","startJalali"],
    ["ساعت شروع","startTime"],["تاریخ پایان","finishJalali"],["ساعت پایان","finishTime"],
    ["دیرکرد",x=>x.late?"بله":"خیر"]
   ],result.allocations);
   table("#lot-capacity-unscheduled",[
    ["کالا",x=>name(x.itemId)],["مرحله","sequence"],["علت","reason"]
   ],result.unscheduled);
   alertText(result.disclaimer);
  }catch(e){alertText(e.message,true);}finally{button.disabled=false;}
 };
 $("#net-pegging-run").onclick=async()=>{
  const id=$("#calculate-mps").value;
  if(!id){alertText("نسخه مصوب MPS را انتخاب کنید.",true);return;}
  const button=$("#net-pegging-run");button.disabled=true;
  try{
   const result=await call("/mrp/"+id+"/net-demand-pegging");
   table("#net-pegging-lots",[
    ["بچ پیشنهادی","lotKey"],
    ["قطعه",x=>name(x.itemId)],
    ["نوع پیشنهاد",x=>x.proposalType==="Manufacturing"?"ساخت داخلی":"تأمین پیشنهادی (بدون عملیات خرید)"],
    ["تاریخ شروع","releaseOnJalali"],
    ["تاریخ تکمیل","receiptOnJalali"],
    ["بچ کل",x=>fmt(x.quantity)],
    ["کسری خالص",x=>fmt(x.netShortage)],
    ["مازاد اندازه بچ",x=>fmt(x.lotSizingSurplus)],
    ["مانده تخصیص نیافته",x=>fmt(x.unallocatedQuantity)],
    ["هشدار",x=>x.releaseBeforeBaseline?"آزادسازی قبل از مبنا":"—"]
   ],result.plannedLots);
   table("#net-pegging-demands",[
    ["شناسه نیاز","demandKey"],
    ["قطعه",x=>name(x.itemId)],
    ["تاریخ نیاز","neededOnJalali"],
    ["تعداد",x=>fmt(x.quantity)],
    ["ردیف اصلی MPS",x=>x.mpsLineId?.slice(0,8)||"—"],
    ["بچ بالادست",x=>x.parentLotKey||"—"],
    ["نسخه BOM",x=>x.bomRevisionId?.slice(0,8)||"—"]
   ],result.demands);
   table("#net-pegging-allocations",[
    ["شناسه نیاز","demandKey"],
    ["کالا",x=>name(x.itemId)],
    ["تاریخ نیاز","neededOnJalali"],
    ["نوع منشأ",x=>({
      PlanningOpeningBalance:"موجودی مبنای داخلی",
      ExpectedReceipt:"دریافت فرضی ثبت‌شده",
      ProposedManufacturingReceipt:"بچ پیشنهادی ساخت",
      ProposedExternalReceipt:"کسری با تأمین پیشنهادی"
    })[x.sourceType]||x.sourceType],
    ["شناسه منشأ","sourceKey"],
    ["تاریخ دسترسی","availableOnJalali"],
    ["مقدار",x=>fmt(x.quantity)],
    ["وضعیت",x=>x.isFirm?"فرض برنامه‌ریزی":"پیشنهاد محاسباتی"]
   ],result.allocations);
   alertText(result.warning);
  }catch(e){alertText(e.message,true);}finally{button.disabled=false;}
 };
 $("#phased-pegging-run").onclick=async()=>{
  const id=$("#calculate-mps").value;
  if(!id){alertText("نسخه مصوب MPS را انتخاب کنید.",true);return;}
  const button=$("#phased-pegging-run");button.disabled=true;
  try{
   const result=await call("/mrp/"+id+"/time-phased-sources");
   table("#phased-allocations",[
    ["ردیف MPS",x=>x.mpsLineId.slice(0,8)],
    ["قطعه",x=>name(x.itemId)],
    ["تاریخ نیاز","neededOnJalali"],
    ["منشأ تأمین",x=>x.sourceType==="PlanningOpeningBalance"?"مانده برنامه‌ریزی مبنا":"دریافت برنامه‌ای"],
    ["تاریخ موجود شدن","availableOnJalali"],
    ["شناسه دریافت",x=>x.receiptId||"—"],
    ["مقدار تخصیص",x=>fmt(x.quantity)],
    ["مسیر BOM","bomPath"]
   ],result.allocations);
   table("#phased-shortages",[
    ["ردیف MPS",x=>x.mpsLineId.slice(0,8)],
    ["قطعه",x=>name(x.itemId)],
    ["موعد نیاز","neededOnJalali"],
    ["مقدار کسری",x=>fmt(x.quantity)],
    ["مسیر BOM","bomPath"]
   ],result.shortages);
   alertText(result.warning);
  }catch(e){alertText(e.message,true);}finally{button.disabled=false;}
 };
 $("#gross-trace-run").onclick=async()=>{
  const id=$("#calculate-mps").value;
  if(!id){alertText("نسخه مصوب MPS را انتخاب کنید.",true);return;}
  const btn=$("#gross-trace-run");btn.disabled=true;
  try{
   const result=await call("/mrp/"+id+"/gross-origins");
   table("#gross-trace-table",[
    ["ردیف MPS",r=>r.mpsLineId.slice(0,8)],
    ["محصول ریشه",r=>name(r.rootItemId)],
    ["قطعه",r=>name(r.componentItemId)],
    ["سطح",r=>r.level],
    ["نیاز ناخالص",r=>fmt(r.grossQuantity)],
    ["تاریخ نیاز","neededOnJalali"],
    ["مسیر BOM","path"]
   ],result.rows);
   alertText(result.warning);
  }catch(e){alertText(e.message,true);}finally{btn.disabled=false;}
 };
 $("#crp-run").onclick=async()=>{
  const id=$("#calculate-mps").value;if(!id){alertText("نسخه MPS را انتخاب کنید.",true);return;}
  const btn=$("#crp-run");btn.disabled=true;
  try{
   const result=await call("/crp/"+id+"/preview");
   $("#calculation-title").textContent="ظرفیت‌سنجی مقدماتی (CRP روزانه)";
   table("#calculation-table",[
    ["مرکز کاری",r=>state.centers.find(x=>x.id===r.workCenterId)?.name||r.workCenterId],["روز","neededOnJalali"],
    ["بار دقیقه",r=>fmt(r.requiredMinutes)],["ظرفیت دقیقه",r=>fmt(r.capacityMinutes)],
    ["اضافه‌بار دقیقه",r=>fmt(r.overloadMinutes)],["درصد بار",r=>r.utilizationPercent===null?"فاقد ظرفیت تعریف‌شده":fmt(r.utilizationPercent)+"٪"]
   ],result.rows);
   alertText("پیش‌نمایش CRP محاسبه شد. جابه‌جایی زمان‌بندی خودکار هنوز انجام نمی‌شود.");
  }catch(e){alertText(e.message,true);}finally{btn.disabled=false;}
 };
 load().catch(e=>alertText("اتصال داده‌های محیط توسعه برقرار نیست: "+e.message,true));
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();
})();
