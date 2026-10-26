(()=>{
"use strict";
const $=sel=>document.querySelector(sel);
const $$=sel=>Array.from(document.querySelectorAll(sel));
const state={orders:[],centers:[]};
const fval=(form,key)=>new FormData(form).get(key);
const dec=(form,key)=>Number(IPPCSJalali.toLatin(fval(form,key)));
const n=value=>Number(value).toLocaleString("fa-IR",{maximumFractionDigits:4});
const status=value=>["باز","آزادشده","اسقاط‌شده"][value]||"—";
function message(value,error=false){
 const el=$("#flash");el.hidden=false;el.className="notice"+(error?" error":"");el.textContent=value;
 el.scrollIntoView({behavior:"smooth",block:"nearest"});
}
async function api(path,method="GET",body){
 const config={method,headers:{}};
 if(body!==undefined){config.headers["Content-Type"]="application/json";config.body=JSON.stringify(body);}
 const res=await window.IPPCSSecurity.secureFetch("/api/v1"+path,config);
 const raw=await res.text();let data;
 try{data=raw?JSON.parse(raw):null;}catch{data=null;}
 if(!res.ok)throw new Error(data?.message||data?.detail||raw||"خطای ناشناخته");
 return data;
}
function fill(select,rows,view){
 const selected=select.value;select.replaceChildren();
 const placeholder=document.createElement("option");placeholder.value="";placeholder.textContent="انتخاب کنید";select.append(placeholder);
 rows.forEach(row=>{const option=document.createElement("option");option.value=row.id;option.textContent=view(row);select.append(option);});
 if([...select.options].some(x=>x.value===selected))select.value=selected;
}
function table(target,cols,rows,actions=[]){
 const host=$(target);host.replaceChildren();
 const box=document.createElement("div");box.className="table-wrap";
 const t=document.createElement("table"),h=document.createElement("thead"),header=document.createElement("tr");
 cols.forEach(([title])=>{const th=document.createElement("th");th.textContent=title;header.append(th);});
 if(actions.length){const th=document.createElement("th");th.textContent="تصمیم";header.append(th);}
 h.append(header);t.append(h);const body=document.createElement("tbody");
 if(!rows.length){const tr=document.createElement("tr"),td=document.createElement("td");
  td.className="empty";td.colSpan=cols.length+(actions.length?1:0);td.textContent="اطلاعاتی برای نمایش وجود ندارد.";
  tr.append(td);body.append(tr);
 }
 rows.forEach(row=>{
  const tr=document.createElement("tr");
  cols.forEach(([,fn])=>{const td=document.createElement("td"),value=typeof fn==="function"?fn(row):row[fn];td.textContent=value??"—";tr.append(td);});
  if(actions.length){const td=document.createElement("td");
   actions.filter(a=>!a.show||a.show(row)).forEach(a=>{
    const button=document.createElement("button");button.type="button";button.className="btn-outline";
    button.textContent=a.label;button.onclick=async()=>{button.disabled=true;
     try{await a.run(row);}catch(err){message(err.message,true);}finally{button.disabled=false;}
    };
    td.append(button,document.createTextNode(" "));
   });tr.append(td);
  }
  body.append(tr);
 });
 t.append(body);box.append(t);host.append(box);
}
async function load(){
 [state.orders,state.centers]=await Promise.all([api("/manufacturing-orders"),api("/work-centers")]);
 fill($("#quality-order"),state.orders.filter(x=>x.status===1||x.status===2),x=>x.number);
 fill($("#quality-view-order"),state.orders,x=>x.number);
 fill($("#oee-workcenter"),state.centers,x=>x.code+" | "+x.name);
 reloadOps();
}
function reloadOps(){
 const order=state.orders.find(x=>x.id===$("#quality-order").value);
 fill($("#quality-operation"),order?.operations||[],x=>x.sequence+" • "+x.name);
}
async function refreshQuality(){
 const id=$("#quality-view-order").value;
 if(!id){message("لطفاً یک دستور برای مشاهده انتخاب کنید.",true);return;}
 const [wip,holds]=await Promise.all([
  api("/manufacturing-orders/"+id+"/wip"),api("/manufacturing-orders/"+id+"/quality-holds")
 ]);
 table("#wip-table",[
  ["ترتیب","sequence"],["عملیات","operationName"],["سالم",x=>n(x.receivedGood)],
  ["ضایعات",x=>n(x.scrap)],["دوباره‌کاری",x=>n(x.rework)],
  ["قرنطینه",x=>n(x.pendingQuality)],["اسقاط پس از کنترل",x=>n(x.rejectedQuality)],
  ["آزاد برای مرحله بعد",x=>n(x.forwardAvailable)],
  ["ثبت شده در مرحله بعد",x=>n(x.nextStageReported)],
  ["در انتظار مرحله بعد",x=>n(x.awaitingNextOperation)]
 ],wip.rows);
 table("#quality-table",[
  ["عملیات",x=>x.operationId.slice(0,8)],["مقدار",x=>n(x.quantity)],
  ["دلیل","reason"],["وضعیت",x=>status(x.status)],["نظر نهایی",x=>x.dispositionNote||"—"],
  ["ثبت‌کننده","raisedBy"]
 ],holds,[
  {label:"آزادسازی کیفی",show:x=>x.status===0,run:x=>decide(id,x,1)},
  {label:"اسقاط کیفی",show:x=>x.status===0,run:x=>decide(id,x,2)}
 ]);
}
async function decide(orderId,hold,decision){
 const note=window.prompt(decision===1?"علت تأیید و آزادسازی کیفی را بنویسید:":"علت اسقاط کیفی را بنویسید:");
 if(note===null)return;
 if(!note.trim()||note.trim().length>500){message("شرح تصمیم الزامی است.",true);return;}
 await api("/manufacturing-orders/"+orderId+"/quality-holds/"+hold.id+"/disposition?decision="+decision,
  "POST",{note:note.trim()});
 message("تعیین تکلیف کیفی ثبت شد و مقدار WIP به‌روزرسانی شد.");
 await load();await refreshQuality();
}
function register(formId,action){
 const form=$(formId);
 form.addEventListener("submit",async event=>{
  event.preventDefault();if(!form.reportValidity())return;
  const submit=form.querySelector('button[type="submit"]');submit.disabled=true;
  try{await action(form);message("اطلاعات ثبت شد.");form.reset();await load();}
  catch(err){message(err.message,true);}finally{submit.disabled=false;}
 });
}
function percent(v){return v===null||v===undefined?"نامشخص":n(v)+"٪";}
async function oee(){
 const date=$("#oee-date").value;if(!IPPCSJalali.parse(date)){message("تاریخ شمسی گزارش معتبر نیست.",true);return;}
 const data=await api("/oee/daily?dateJalali="+encodeURIComponent(date));
 table("#oee-table",[
  ["مرکز کاری","workCenter"],["زمان برنامه",x=>x.plannedMinutes===null?"ثبت نشده":n(x.plannedMinutes)],
  ["دقیقه توقف",x=>n(x.stoppedMinutes)],["سالم",x=>n(x.good)],
  ["ضایعات",x=>n(x.scrap)],["دوباره‌کاری",x=>n(x.rework)],
  ["دسترس‌پذیری",x=>percent(x.availabilityPercent)],
  ["عملکرد",x=>percent(x.performancePercent)],["کیفیت",x=>percent(x.qualityPercent)],
  ["OEE",x=>percent(x.oeePercent)],["ملاحظات",x=>x.warning||"—"]
 ],data.rows);
 message("شاخص‌های مقدماتی OEE بر اساس تاریخ محلی کارخانه محاسبه شدند.");
}
function boot(){
 $("#quality-order").addEventListener("change",reloadOps);
 $("#quality-refresh").addEventListener("click",()=>refreshQuality().catch(err=>message(err.message,true)));
 $("#oee-calculate").addEventListener("click",()=>oee().catch(err=>message(err.message,true)));
 register("#quality-form",f=>api("/manufacturing-orders/"+fval(f,"orderId")+"/quality-holds","POST",{
  operationId:fval(f,"operationId"),quantity:dec(f,"quantity"),reason:fval(f,"reason")
 }));
 register("#oee-plan-form",f=>api("/oee/plans","PUT",{
  workCenterId:fval(f,"workCenterId"),productionDateJalali:fval(f,"date"),
  plannedMinutes:dec(f,"planned"),note:fval(f,"note")||null
 }));
 $$('nav [data-page="quality"], nav [data-page="oee"]').forEach(button=>
  button.addEventListener("click",()=>load().catch(err=>message(err.message,true))));
 load().catch(err=>message("اطلاعات کیفیت و OEE قابل دریافت نیست: "+err.message,true));
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();
})();