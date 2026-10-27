(()=>{
"use strict";
const $=id=>document.getElementById(id);
const state={routings:[],boms:[],orders:[],selected:new Set(),previewSignature:null,cutoverSignature:null};
const revisionList=()=> $("engineering-kind").value==="BOM"?state.boms:state.routings;
const ownerOf=rev=>$("engineering-kind").value==="BOM"?rev?.parentItemId:rev?.itemId;
function title(rev){
 const first=rev.validFromJalali||rev.effectiveFromJalali||"";
 const last=rev.validUntilJalali||rev.effectiveToJalali||"باز";
 const code=rev.revision||rev.revisionCode||"";
 return code+" · "+first+" تا "+last+(rev.state===1?" · مصوب":rev.state===2?" · منسوخ":" · پیش‌نویس");
}
function notice(text,isError=false){
 const n=$("engineering-message");n.hidden=false;
 n.className="notice"+(isError?" error":"");n.textContent=text;
}
async function api(path,method="GET",payload){
 const conf={method,headers:{}};
 if(payload!==undefined){conf.headers["Content-Type"]="application/json";conf.body=JSON.stringify(payload);}
 const response=await IPPCSSecurity.secureFetch("/api/v1"+path,conf);
 const raw=await response.text();
 let data=null;try{data=raw?JSON.parse(raw):null;}catch{data={message:raw};}
 if(!response.ok) {
   const error=new Error(data?.message||"خطای سرویس");
   error.details=data;throw error;
 }
 return data;
}
function clearPreview(){
 state.previewSignature=null;$("engineering-apply").disabled=true;
 $("engineering-results").replaceChildren();
}
function option(value,text){
 const o=document.createElement("option");o.value=value;o.textContent=text;return o;
}
function renderBaseline(){
 const el=$("engineering-baseline"),current=el.value;
 el.replaceChildren(option("","انتخاب نسخه مبدأ"));
 for(const r of revisionList().filter(x=>x.state===1||x.state===2))
  el.append(option(r.id,title(r)));
 if([...el.options].some(x=>x.value===current))el.value=current;
 renderCandidate();renderOrders();
}
function renderCandidate(){
 const b=revisionList().find(x=>x.id===$("engineering-baseline").value);
 const el=$("engineering-candidate"),current=el.value;
 el.replaceChildren(option("","انتخاب نسخه مقصد مصوب"));
 if(b)for(const r of revisionList().filter(x=>x.state===1&&
    x.id!==b.id&&ownerOf(x)===ownerOf(b)))
   el.append(option(r.id,title(r)));
 if([...el.options].some(x=>x.value===current))el.value=current;
 renderCutoverCandidates(b);
 clearPreview();
}
function renderOrders(){
 const baseline=$("engineering-baseline").value;
 const kind=$("engineering-kind").value;
 const items=state.orders.filter(o=>o.status===0&&o.operations.length===0&&
   (kind==="BOM"?o.bomRevisionId:o.routingRevisionId)===baseline);
 state.selected=new Set([...state.selected].filter(id=>items.some(x=>x.id===id)));
 const container=$("engineering-orders");container.replaceChildren();
 const table=document.createElement("table");
 const thead=document.createElement("thead");const h=document.createElement("tr");
 ["انتخاب","شماره دستور","تاریخ شروع شمسی","موعد","مقدار"].forEach(n=>{
  const th=document.createElement("th");th.textContent=n;h.append(th);
 });thead.append(h);table.append(thead);
 const body=document.createElement("tbody");
 for(const order of items){
  const tr=document.createElement("tr"),cell=document.createElement("td");
  const cb=document.createElement("input");cb.type="checkbox";cb.checked=state.selected.has(order.id);
  cb.setAttribute("aria-label","انتخاب دستور "+order.number);
  cb.addEventListener("change",()=>{
   if(cb.checked){if(state.selected.size>=100){cb.checked=false;notice("حداکثر ۱۰۰ دستور در هر انتقال مجاز است.",true);return;}state.selected.add(order.id);}
   else state.selected.delete(order.id);
   updateCount();clearPreview();
  });
  cell.append(cb);tr.append(cell);
  for(const value of [order.number,order.plannedStartJalali,order.dueOnJalali,order.plannedQuantity]){
   const td=document.createElement("td");td.textContent=String(value??"—");tr.append(td);
  }
  body.append(tr);
 }
 if(!items.length){
  const tr=document.createElement("tr"),td=document.createElement("td");
  td.colSpan=5;td.textContent="دستور پیش‌نویس واجد نمایش برای این نسخه در ۲۰۰ رکورد اخیر وجود ندارد.";
  tr.append(td);body.append(tr);
 }
 table.append(body);
 const wrap=document.createElement("div");wrap.className="table-wrap";wrap.append(table);container.append(wrap);
 updateCount();clearPreview();
}
function updateCount(){$("engineering-selected").textContent=state.selected.size+" دستور انتخاب شده است.";}
function payload(dryRun){
 const baselineId=$("engineering-baseline").value;
 const candidateId=$("engineering-candidate").value;
 const reason=$("engineering-reason").value.trim();
 if(!baselineId||!candidateId||!reason||state.selected.size===0)
  throw new Error("نسخه مبدأ، نسخه مقصد مصوب، دلیل تغییر و دست‌کم یک دستور را تعیین کنید.");
 if(reason.length>300||state.selected.size>100)throw new Error("محدودیت تعداد دستور یا طول دلیل رعایت نشده است.");
 return {kind:$("engineering-kind").value,baselineId,candidateId,
  orderIds:[...state.selected].sort(),reason,dryRun};
}
function renderResult(data){
 const host=$("engineering-results");host.replaceChildren();
 if(!data?.rows)return;
 const summary=document.createElement("p");
 summary.textContent=data.rows.length+" دستور کنترل شد. "+
   (data.rows.every(x=>x.canRebind)?"همه پیش‌شرط‌ها برقرارند.":"حداقل یک دستور قابل انتقال نیست.");
 host.append(summary);
 const table=document.createElement("table"),head=document.createElement("tr");
 ["دستور","شروع","نتیجه","علت"].forEach(t=>{const th=document.createElement("th");th.textContent=t;head.append(th);});
 table.append(head);
 for(const r of data.rows){
  const tr=document.createElement("tr");
  for(const t of [r.orderNumber,r.plannedStartJalali,r.canRebind?"قابل انتقال":"مسدود",
    (r.issues||[]).map(x=>x.description).join("؛ ")||"—"]){
   const td=document.createElement("td");td.textContent=t;tr.append(td);
  }table.append(tr);
 }
 const wrap=document.createElement("div");wrap.className="table-wrap";wrap.append(table);host.append(wrap);
}
async function refresh(preserveBaseline=false){
 const [routings,boms,orders]=await Promise.all([
  api("/routings"),api("/bom-revisions"),api("/manufacturing-orders")
 ]);
 state.routings=routings;state.boms=boms;state.orders=orders;
 state.selected.clear();if(!preserveBaseline)$("engineering-baseline").value="";
 renderBaseline();
}
async function preview(){
 try{
  const request=payload(true);clearPreview();
  const response=await api("/factory-engineering/draft-rebind","POST",request);
  renderResult(response);
  state.previewSignature=JSON.stringify(payload(false));
  $("engineering-apply").disabled=false;
  notice("پیش‌نمایش موفق است. پیش از ثبت، شماره دستورها و نسخه مقصد را بررسی کنید.");
 }catch(e){
  renderResult(e.details);
  notice(e.message,true);
 }
}
async function apply(){
 try{
  const request=payload(false);
  if(JSON.stringify(request)!==state.previewSignature)
   throw new Error("انتخاب‌ها تغییر کرده‌اند؛ ابتدا پیش‌نمایش جدید بگیرید.");
  if(!window.confirm("انتقال نسخه این دستورات پیش‌نویس ثبت شود؟ این اقدام در تاریخچه ثبت می‌شود."))return;
  $("engineering-apply").disabled=true;
  const response=await api("/factory-engineering/draft-rebind","POST",request);
  await refresh();renderResult(response);
  notice(response.applied+" دستور با تاریخچه حسابرسی به نسخه مصوب منتقل شد.");
 }catch(e){renderResult(e.details);notice(e.message,true);clearPreview();}
}
document.addEventListener("DOMContentLoaded",async()=>{
 $("engineering-kind").addEventListener("change",()=>{state.selected.clear();clearPreview();renderBaseline();});
 $("engineering-baseline").addEventListener("change",()=>{
  state.selected.clear();renderCandidate();renderOrders();
 });
 $("engineering-candidate").addEventListener("change",clearPreview);
 $("engineering-reason").addEventListener("input",clearPreview);
 $("engineering-preview").addEventListener("click",preview);
 $("engineering-apply").addEventListener("click",apply);
 try{await refresh();}catch(e){notice("دریافت داده‌ها ناموفق بود: "+e.message,true);}
});
})();