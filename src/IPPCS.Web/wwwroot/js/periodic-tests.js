(()=> {
 "use strict";
 const $=selector=>document.querySelector(selector);
 const state={items:[],units:[],plans:[],runs:[]};
 const fmt=value=>value===null||value===undefined?"—":Number(value).toLocaleString("fa-IR",{maximumFractionDigits:6});
 function message(text,error=false){
  const el=$("#test-notice");el.hidden=false;el.className="notice"+(error?" error":"");el.textContent=text;
  el.scrollIntoView({behavior:"smooth",block:"nearest"});
 }
 function rangeMessage(text,error=false){
  const el=$("#test-schedule-summary");el.hidden=false;el.className="notice"+(error?" error":"");el.textContent=text;
 }
 async function api(path,method="GET",body){
  const options={method,headers:{}};
  if(body!==undefined){options.headers["Content-Type"]="application/json";options.body=JSON.stringify(body);}
  const response=await window.IPPCSSecurity.secureFetch("/api/v1"+path,options);
  const raw=await response.text();let data=null;
  try{data=raw?JSON.parse(raw):null;}catch{data=null;}
  if(!response.ok)throw new Error(data?.message||data?.detail||data?.title||raw||("خطای HTTP "+response.status));
  return data;
 }
 function choices(selector,rows,value,label,placeholder){
  IPPCSChoices.fill($(selector),rows,{value,label,placeholder});
 }
 function table(target,columns,rows,action){
  const host=$(target);host.replaceChildren();
  const wrap=document.createElement("div");wrap.className="table-wrap";
  const t=document.createElement("table"),head=document.createElement("thead"),headRow=document.createElement("tr");
  for(const [title] of columns){const th=document.createElement("th");th.textContent=title;headRow.append(th);}
  if(action){const th=document.createElement("th");th.textContent="عملیات";headRow.append(th);}
  head.append(headRow);t.append(head);const body=document.createElement("tbody");
  if(!rows.length){
   const tr=document.createElement("tr"),td=document.createElement("td");
   td.colSpan=columns.length+(action?1:0);td.className="empty";td.textContent="رکوردی برای نمایش وجود ندارد.";tr.append(td);body.append(tr);
  }
  for(const row of rows){
   const tr=document.createElement("tr");
   if(row.outcome==="Failed")tr.classList.add("test-result-failed");
   for(const [,field] of columns){
    const td=document.createElement("td");const value=typeof field==="function"?field(row):row[field];
    td.textContent=value===null||value===undefined?"—":String(value);tr.append(td);
   }
   if(action){
    const td=document.createElement("td"),button=document.createElement("button");
    button.className="btn-outline";button.type="button";button.textContent=action.label(row);
    button.disabled=action.disabled?.(row)||false;
    button.onclick=async()=>{
     button.disabled=true;
     try{await action.run(row);}
     catch(error){message(error.message,true);}
     finally{button.disabled=action.disabled?.(row)||false;}
    };
    td.append(button);tr.append(td);
   }
   body.append(tr);
  }
  t.append(body);wrap.append(t);host.append(wrap);
 }
 const parseNumber=value=>{
  if(value===null||value===undefined||String(value).trim()==="")return null;
  const n=Number(IPPCSJalali.toLatin(String(value).trim()));
  return Number.isFinite(n)?n:null;
 };
 function cadenceText(plan){
  return plan.cadence==="Annual"?"سالانه":("هر "+plan.intervalMonths+" ماه");
 }
 function outcomeText(value){
  return ({None:"ثبت‌نشده",Passed:"قبول",Failed:"مردود",Inconclusive:"نامعین"})[value]||value||"—";
 }
 function statusText(row){
  if(row.state==="Completed")return "تکمیل‌شده";
  return row.scheduleStatus==="Overdue"?"سررسید گذشته":"برنامه‌ریزی‌شده";
 }
 function renderPlans(){
  table("#test-plan-table",[
   ["کد","code"],["برنامه آزمون","name"],["محصول",x=>x.itemCode+" — "+x.itemName],
   ["تناوب",cadenceText],["اولین سررسید","firstDueJalali"],["اعتبار تا",x=>x.validThroughJalali||"بدون پایان"],
   ["روش آزمون","methodReference"],["معیار پذیرش","acceptanceCriteria"],["واحد",x=>x.unit||"غیرعددی"],
   ["حد پایین",x=>x.minimum===null?"—":fmt(x.minimum)],["حد بالا",x=>x.maximum===null?"—":fmt(x.maximum)],
   ["نمونه در هر اجرا","sampleCount"],["وضعیت",x=>x.isActive?"فعال":"غیرفعال"]
  ],state.plans,{
   label:()=> "غیرفعال‌سازی",disabled:x=>!x.isActive,
   run:async x=>{
    if(!window.confirm("برنامه آزمون غیرفعال شود؟ سوابق و سررسیدهای قبلی حفظ می‌شوند."))return;
    await api("/periodic-product-tests/plans/"+x.id+"/deactivate","POST");
    await loadPlans();message("برنامه آزمون غیرفعال شد؛ تاریخچه حفظ شد.");
   }
  });
  $("#test-plan-count").textContent=state.plans.filter(x=>x.isActive).length.toLocaleString("fa-IR");
 }
 function renderRuns(){
  table("#test-run-table",[
   ["سررسید شمسی","dueJalali"],["کد برنامه","planCode"],["نام آزمون","planName"],
   ["محصول",x=>x.itemCode+" — "+x.itemName],["تناوب",cadenceText],
   ["وضعیت",statusText],["تاریخ اجرا",x=>x.performedOnJalali||"—"],
   ["نمونه/ارجاع","sampleReference"],["مقدار",x=>x.measuredValue===null?"—":fmt(x.measuredValue)],
   ["نتیجه",x=>outcomeText(x.outcome)],["مرجع گزارش","reportReference"],
   ["توضیحات","notes"],["ثبت‌کننده",x=>x.recordedBy||"—"]
  ],state.runs);
  $("#test-overdue-count").textContent=state.runs.filter(x=>x.scheduleStatus==="Overdue").length.toLocaleString("fa-IR");
  $("#test-failed-count").textContent=state.runs.filter(x=>x.outcome==="Failed").length.toLocaleString("fa-IR");
  const open=state.runs.filter(x=>x.state==="Scheduled");
  choices("#test-run-select",open,"id",x=>x.dueJalali+" | "+x.planCode+" — "+x.planName+" | "+x.itemCode,
   "سررسید انجام‌نشده را انتخاب کنید");
 }
 async function loadPlans(){
  state.plans=await api("/periodic-product-tests/plans");
  renderPlans();
 }
 function validateRange(){
  const from=$("#test-range-from").value.trim(),through=$("#test-range-through").value.trim();
  if(!IPPCSJalali.parse(from)||!IPPCSJalali.parse(through))
   throw new Error("تاریخ شروع و پایان شمسی معتبر لازم است.");
  const first=IPPCSJalali.parse(from),last=IPPCSJalali.parse(through);
  const firstKey=first?first.y*10000+first.m*100+first.d:0;
  const lastKey=last?last.y*10000+last.m*100+last.d:0;
  if(!first||!last||lastKey<firstKey)
   throw new Error("تاریخ پایان باید برابر یا بعد از تاریخ شروع باشد.");
  return {fromJalali:from,throughJalali:through};
 }
 async function loadRuns(){
  const range=validateRange();
  const data=await api("/periodic-product-tests/runs?"+new URLSearchParams(range).toString());
  state.runs=data;renderRuns();
  return data;
 }
 async function generateDue(){
  const range=validateRange();
  const result=await api("/periodic-product-tests/schedule","POST",range);
  rangeMessage("سررسیدهای ایجادشده: "+fmt(result.created)+" | از قبل موجود و حفظ‌شده: "+
   fmt(result.alreadyExisted)+" | بازه: "+result.fromJalali+" تا "+result.throughJalali+
   " | "+result.disclaimer);
  await loadRuns();
 }
 function setCadence(){
  const annual=$("#test-cadence").value==="Annual",interval=$("#test-interval");
  // Readonly keeps the annual 12-month value in FormData; disabled controls are omitted.
  interval.disabled=false;
  interval.max=annual?"12":"11";
  interval.readOnly=annual;
  if(annual)interval.value="12";
  else if(Number(interval.value)===12||!interval.value)interval.value="3";
 }
 function toggleNotes(){
  const required=["Failed","Inconclusive"].includes($("#test-outcome").value);
  $("#test-result-notes").required=required;
 }
 function registerForm(id,work,successText,after){
  const form=$(id);
  form.addEventListener("submit",async event=>{
   event.preventDefault();if(!form.reportValidity())return;
   const button=form.querySelector('button[type="submit"]');
   if(button)button.disabled=true;
   try{await work(new FormData(form));message(successText);form.reset();setCadence();toggleNotes();if(after)await after();}
   catch(error){message(error.message,true);}
   finally{if(button)button.disabled=false;}
  });
 }
 async function bootstrap(){
  const [items,units]=await Promise.all([
   api("/items"),
   api("/factory-master-options?kind=UnitOfMeasure")
  ]);
  state.items=items;state.units=units;
  const products=items.filter(x=>Number(x.type)===1||Number(x.type)===2);
  choices("#test-item",products,"id",x=>x.code+" — "+x.name,"محصول نهایی یا نیمه‌ساخته را انتخاب کنید");
  choices("#test-unit",units,"code",x=>x.code+" — "+x.label,"بدون واحد (آزمون غیرعددی)");
  await loadPlans();
 }
 function wire(){
  $("#test-cadence").addEventListener("change",setCadence);
  $("#test-outcome").addEventListener("change",toggleNotes);
  $("#test-generate-due").addEventListener("click",async()=>{
   const button=$("#test-generate-due");button.disabled=true;
   try{await generateDue();}
   catch(error){rangeMessage(error.message,true);}
   finally{button.disabled=false;}
  });
  $("#test-refresh-runs").addEventListener("click",async()=>{
   const button=$("#test-refresh-runs");button.disabled=true;
   try{await loadRuns();rangeMessage("سررسیدهای ثبت‌شده در بازه انتخابی بازخوانی شدند.");}
   catch(error){rangeMessage(error.message,true);}
   finally{button.disabled=false;}
  });
  registerForm("#test-plan-form",async form=>{
   const cadence=String(form.get("cadence"));
   const interval=Number(IPPCSJalali.toLatin(String(form.get("intervalMonths"))));
   const unit=String(form.get("unit")||"").trim();
   const minimum=parseNumber(form.get("minimum")),maximum=parseNumber(form.get("maximum"));
   const sampleCount=Number(IPPCSJalali.toLatin(String(form.get("sampleCount"))));
   await api("/periodic-product-tests/plans","POST",{
    code:String(form.get("code")).trim(),name:String(form.get("name")).trim(),
    itemId:String(form.get("itemId")),cadence,intervalMonths:interval,
    firstDueJalali:String(form.get("firstDue")).trim(),
    validThroughJalali:String(form.get("validThrough")||"").trim()||null,
    methodReference:String(form.get("methodReference")).trim(),
    acceptanceCriteria:String(form.get("acceptanceCriteria")).trim(),
    unit:unit||null,minimum,maximum,sampleCount
   });
  },"برنامه آزمون ثبت شد. سررسیدها در گام دوم به‌صورت کنترل‌شده تولید می‌شوند.",async()=>{
   await loadPlans();
   const from=$("#test-range-from").value.trim(),through=$("#test-range-through").value.trim();
   if(from&&through)await loadRuns();
  });
  registerForm("#test-result-form",async form=>{
   const id=String(form.get("runId")||"");
   if(!id)throw new Error("یک سررسید انجام‌نشده انتخاب کنید.");
   await api("/periodic-product-tests/runs/"+id+"/result","POST",{
    performedOnJalali:String(form.get("performedOn")).trim(),
    sampleReference:String(form.get("sampleReference")).trim(),
    measuredValue:parseNumber(form.get("measuredValue")),
    outcome:String(form.get("outcome")),
    reportReference:String(form.get("reportReference")||"").trim()||null,
    notes:String(form.get("notes")||"").trim()||null
   });
  },"نتیجه ثبت شد؛ سابقه رخداد دیگر قابل بازنویسی نیست.",async()=>{
   await loadRuns();
  });
  setCadence();toggleNotes();
 }
 wire();
 bootstrap().catch(error=>message("بارگذاری اطلاعات آزمون انجام نشد: "+error.message,true));
})();
