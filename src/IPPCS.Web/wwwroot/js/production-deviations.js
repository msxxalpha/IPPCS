(()=>{
"use strict";
const $=s=>document.querySelector(s);
const data={mps:[],orders:[],items:[],deviations:[],lines:[],pegRows:[],masters:{},page:1,pageSize:20};
let searchToken=0,searchTimer=null;
const notice=(message,error=false)=>{
 const el=$("#notice");el.hidden=false;el.textContent=message;
 el.className="notice"+(error?" error":"");
 el.scrollIntoView({block:"nearest",behavior:"smooth"});
};
async function api(path,method="GET",payload){
 const response=await IPPCSSecurity.secureFetch("/api/v1"+path,{
  method,headers:payload===undefined?{}:{"Content-Type":"application/json"},
  body:payload===undefined?undefined:JSON.stringify(payload)
 });
 const raw=await response.text();let body;
 try{body=raw?JSON.parse(raw):null;}catch{body=null;}
 if(!response.ok)throw Error(body?.message||raw||"خطای دریافت اطلاعات ("+response.status+")");
 return body;
}
function fill(select,rows,label,placeholder="انتخاب کنید"){
 IPPCSChoices.fill(select,rows,{value:"id",label,placeholder});
}
function fillCode(select,rows,placeholder="انتخاب کنید"){
 IPPCSChoices.fill(select,rows,{value:"code",label:x=>x.code+" — "+x.label,placeholder});
}
function title(kind,code){
 const option=(data.masters[kind]||[]).find(x=>x.code===String(code));
 return option?.label||"کد "+code;
}
function active(kind){return (data.masters[kind]||[]).filter(x=>x.isActive);}
function formatCase(x){return x.description.slice(0,75)+" · "+["باز","دارای اقدام","مختومه"][x.status];}
function asOption(kind,code){
 return (data.masters[kind]||[]).find(x=>x.code===code&&x.isActive)?.label||"";
}
function refreshLine(){
 const version=data.mps.find(x=>x.id===$("#version").value);
 data.lines=version?.lines||[];
 fill($("#line"),data.lines,x=>{
  const item=data.items.find(p=>p.id===x.itemId);
  return (item?item.code+" - "+item.name:"کالا "+x.itemId.slice(0,8))+
   " | "+x.neededOnJalali+" | "+x.quantity;
 });
 refreshOrders();
}
function refreshOrders(){
 const line=data.pegRows.find(x=>x.id===$("#line").value);
 const ids=new Set((line?.orders||[]).map(x=>x.orderId));
 fill($("#order"),data.orders.filter(x=>ids.has(x.id)&&x.status!==5),
  x=>x.number+" | "+(x.plannedQuantity??"")+" واحد","بدون دستور مرتبط");
}
async function refreshPegging(){
 const id=$("#version").value;
 data.pegRows=id?(await api("/production-pegging/mps/"+encodeURIComponent(id))).rows:[];
 refreshOrders();
}
async function loadMasters(){
 for(const kind of ["DeviationCategory","RootCauseCategory","ActionOwner"]){
  data.masters[kind]=await api("/factory-master-options?kind="+kind+"&includeInactive=true");
 }
 fillCode($("#category"),active("DeviationCategory"));
 data.categoryPicker=IPPCSChoices.multiple($("#deviation-categories"),
  data.masters.DeviationCategory,{value:"code",
   label:x=>x.label+" ("+x.code+")",onChange:filtersChanged});
 fillCode($("#action-owner"),active("ActionOwner"),"انتخاب نقش مسئول");
 fillCode($("#revise-owner"),active("ActionOwner"),"انتخاب نقش مسئول");
 fillCode($("#cause-group"),active("RootCauseCategory"));
 fillCode($("#revise-cause-group"),active("RootCauseCategory"));
 const noOwner=active("ActionOwner").length===0;
 if(noOwner)notice("برای ثبت اقدام اصلاحی، مدیر سامانه باید ابتدا نقش‌های مسئول پیگیری را در اطلاعات پایه تعریف کند.",true);
}
function fillCaseSelectors(){
 fill($("#action-case"),data.deviations.filter(x=>x.status===0),formatCase);
 fill($("#close-case"),data.deviations.filter(x=>x.status===1),formatCase);
 fill($("#revise-case"),data.deviations.filter(x=>x.status===1),formatCase);
 fill($("#escalate-case"),data.deviations.filter(x=>x.status!==2),formatCase);
 fill($("#effectiveness-case"),data.deviations.filter(x=>
  x.status===2&&!x.effectiveness?.independentlyVerified),formatCase);
}
let caseToken=0,caseTimer=null;
async function loadCaseChoices(){
 const token=++caseToken,term=$("#case-search").value.trim();
 const guid=/^[0-9a-f]{8}-[0-9a-f-]{27,36}$/i.test(term);
 let rows=[],total=0;
 if(guid){
  try{rows=[await api("/production-deviations/"+encodeURIComponent(term))];total=rows.length;}
  catch(e){if(!e.message.includes("404"))throw e;}
 }else{
  const url="/production-deviations/search?page=1&pageSize=100"+
   (term?"&text="+encodeURIComponent(term):"");
  const result=await api(url);rows=result.rows;total=result.total;
 }
 if(token!==caseToken)return;
 data.deviations=rows;
 fillCaseSelectors();
 $("#case-search-count").textContent=total.toLocaleString("fa-IR")+
  " پرونده یافت شد؛ "+rows.length.toLocaleString("fa-IR")+
  " گزینه قابل انتخاب است."+(total>rows.length?" عبارت دقیق‌تری وارد کنید.":"");
}
async function renderCases(){
 await loadCaseChoices();
 await loadTable();
}
function make(tag,text){const x=document.createElement(tag);if(text!==undefined)x.textContent=String(text);return x;}
function td(tr,label,value){
 const cell=make("td");
 cell.dataset.label=label;
 if(value instanceof Node)cell.append(value);else cell.textContent=String(value??"—");
 tr.append(cell);return cell;
}
function renderTable(rows){
 const host=$("#deviation-table");host.replaceChildren();
 if(!rows.length){host.append(make("p","پرونده‌ای مطابق فیلترهای انتخابی پیدا نشد."));return;}
 const table=make("table");table.className="factory-cases-table";
 const thead=make("thead"),head=make("tr");
 for(const label of ["نوع","شرح انحراف","وضعیت","اثربخشی","مسئول","موعد شمسی","مشاهده"]){
  head.append(make("th",label));
 }
 thead.append(head);table.append(thead);
 const body=make("tbody");
 for(const x of rows){
  const row=make("tr");
  td(row,"نوع",title("DeviationCategory",x.category));
  td(row,"شرح",x.description);
  td(row,"وضعیت",["باز","دارای اقدام","مختومه"][x.status]||"—");
  td(row,"اثربخشی",x.effectiveness?.state==="Verified"?"تأیید مستقل":
   x.effectiveness?.state==="AwaitingIndependentReview"?"منتظر بازبینی":
   x.effectiveness?.state==="ReopenedForCorrection"?"رد اثربخشی":
   x.effectiveness?.state==="InvalidEvidence"?"شواهد نامعتبر":"—");
  td(row,"مسئول",x.responsible||"—");
  td(row,"موعد",x.actionDueJalali||"—");
  const button=make("button","جزئیات");
  button.type="button";button.className="btn-outline";
  button.setAttribute("aria-expanded","false");
  button.setAttribute("aria-label","مشاهده جزئیات پرونده "+x.description.slice(0,40));
  const detailRow=make("tr");detailRow.className="factory-detail-row";detailRow.hidden=true;
  const detailCell=make("td");detailCell.colSpan=7;
  const details=make("div");details.className="factory-case-details";
  const entries=[
   ["شناسه پرونده",x.id],
   ["طبقه علت ریشه‌ای",x.rootCauseCategoryCode?title("RootCauseCategory",x.rootCauseCategoryCode):"ثبت متنی قدیمی"],
   ["کد مسئول",x.responsibleCode||"ثبت متنی قدیمی"],
   ["علت ریشه‌ای",x.rootCause||"—"],
   ["اقدام اصلاحی",x.correctiveAction||"—"],
   ["تعداد سوابق",(x.events||[]).length],
   ["تعداد تشدید پیگیری",(x.events||[]).filter(e=>e.details?.startsWith("ESCALATED:")).length]
  ];
  for(const [label,value] of entries){
   const div=make("div");div.append(make("strong",label),make("span",value));details.append(div);
  }
  detailCell.append(details);detailRow.append(detailCell);
  button.onclick=()=>{
   detailRow.hidden=!detailRow.hidden;
   button.setAttribute("aria-expanded",String(!detailRow.hidden));
  };
  td(row,"مشاهده",button);body.append(row,detailRow);
 }
 table.append(body);const wrap=make("div","factory-responsive-table");wrap.append(table);host.append(wrap);
}
async function loadTable(){
 const token=++searchToken;
 const pageSize=Number($("#deviation-page-size").value);
 const params=new URLSearchParams({page:String(data.page),pageSize:String(pageSize)});
 for(const [input,key] of [["#deviation-search","text"],["#deviation-status","status"]]){
  const value=$(input).value.trim();if(value)params.set(key,value);
 }
 const categories=data.categoryPicker?.values()||[];
 if(categories.length)params.set("categories",categories.join(","));
 const page=await api("/production-deviations/search?"+params.toString());
 if(token!==searchToken)return;
 renderTable(page.rows);
 $("#deviation-count").textContent=page.total.toLocaleString("fa-IR")+" پرونده | صفحه "+
   page.page.toLocaleString("fa-IR")+" از "+page.totalPages.toLocaleString("fa-IR");
 $("#deviation-page").textContent=page.page.toLocaleString("fa-IR");
 $("#deviation-prev").disabled=!page.hasPrevious;
 $("#deviation-next").disabled=!page.hasNext;
}
function filtersChanged(){
 data.page=1;clearTimeout(searchTimer);
 searchTimer=setTimeout(()=>loadTable().catch(e=>notice(e.message,true)),250);
}
function prefillRevision(){
 const row=data.deviations.find(x=>x.id===$("#revise-case").value);
 if(!row)return;
 $("#revise-cause").value=row.rootCause||"";
 $("#revise-corrective").value=row.correctiveAction||"";
 $("#revise-date").value=row.actionDueJalali||"";
 $("#revise-owner").value=row.responsibleCode&&asOption("ActionOwner",row.responsibleCode)?
  row.responsibleCode:"";
 $("#revise-cause-group").value=row.rootCauseCategoryCode&&
  asOption("RootCauseCategory",row.rootCauseCategoryCode)?
  row.rootCauseCategoryCode:"";
}
function selectedOwner(id){
 const code=$(id).value;const owner=active("ActionOwner").find(x=>x.code===code);
 if(!owner)throw Error("مسئول پیگیری را از اطلاعات پایه انتخاب کنید.");
 return owner;
}
async function reviewFollowup(){
 const id=$("#escalate-case").value;
 if(!id)throw Error("پرونده را انتخاب کنید.");
 const parts=new Intl.DateTimeFormat("en-US-u-ca-persian-nu-latn",{
  timeZone:"Asia/Tehran",year:"numeric",month:"2-digit",day:"2-digit"
 }).formatToParts(new Date());
 const date=["year","month","day"].map(k=>parts.find(x=>x.type===k).value).join("/");
 const report=await api("/production-deviations/"+id+"/follow-up?asOfJalali="+encodeURIComponent(date));
 const host=$("#followup-summary");host.replaceChildren();
 host.append(make("p","تعداد تشدیدها: "+report.escalationCount+
  " | اقدام معوق: "+(report.isOverdue?"بله":"خیر")+
  " | آخرین دلیل: "+(report.lastEscalationReason||"—")+
  " | تعداد رویدادها: "+report.eventCount));
}
async function refreshPending(){
 const response=await api("/production-deviations/effectiveness-pending");
 const host=$("#effectiveness-pending");host.replaceChildren();
 host.append(make("p","پرونده‌های مختومه بدون تأیید مستقل: "+response.count));
 if(!response.rows.length)return;
 const table=make("table"),header=make("tr");
 for(const label of ["پرونده","وضعیت","تاریخ اختتام"])header.append(make("th",label));
 table.append(header);
 for(const row of response.rows){
  const tr=make("tr");
  for(const value of [row.description,row.state,row.closedUtc?
   new Date(row.closedUtc).toLocaleString("fa-IR"):"—"])tr.append(make("td",value));
  table.append(tr);
 }
 const wrap=make("div","factory-responsive-table");wrap.append(table);host.append(wrap);
}
function submit(form,handler){
 $(form).onsubmit=async event=>{
  event.preventDefault();
  const button=$(form).querySelector('[type="submit"]');
  if(!$(form).reportValidity())return;
  button.disabled=true;
  try{await handler();await renderCases();}
  catch(e){notice(e.message,true);}
  finally{button.disabled=false;}
 };
}
async function boot(){
 const profile=await IPPCSSecurity.secureFetch("/auth/me");
 if(profile.status===401){IPPCSSecurity.goLogin();return;}
 if(!profile.ok)throw Error("دسترسی کاربر قابل احراز نیست.");
 const user=await profile.json(),roles=user.roles||[];
 const planner=roles.includes("Administrator")||roles.includes("Planner");
 const quality=roles.includes("Administrator")||roles.includes("QualityInspector");
 for(const selector of ["#create","#action","#revise","#escalate","#close"]){
  if(!planner)$(selector).hidden=true;
 }
 if(!quality)$("#effectiveness-form").hidden=true;
 await loadMasters();
 [data.mps,data.orders,data.items]=await Promise.all([
  api("/mps"),api("/manufacturing-orders"),api("/items")]);
 fill($("#version"),data.mps.filter(x=>x.state===1),x=>x.code);
 $("#version").onchange=async()=>{
  refreshLine();
  try{await refreshPegging();}catch(e){notice(e.message,true);}
 };
 $("#line").onchange=refreshOrders;
 refreshLine();
 if($("#version").value)await refreshPegging();
 $("#case-search").oninput=()=>{
  clearTimeout(caseTimer);
  caseTimer=setTimeout(()=>loadCaseChoices().catch(e=>notice(e.message,true)),280);
 };
 for(const input of ["#deviation-search","#deviation-status","#deviation-page-size"]){
  $(input).addEventListener(input==="#deviation-search"?"input":"change",filtersChanged);
 }
 $("#deviation-prev").onclick=()=>{if(data.page>1){data.page--;loadTable().catch(e=>notice(e.message,true));}};
 $("#deviation-next").onclick=()=>{data.page++;loadTable().catch(e=>notice(e.message,true));};
 $("#revise-case").onchange=prefillRevision;
 submit("#create",async()=>{
  if(!$("#line").value||!$("#category").value)throw Error("ردیف برنامه و نوع انحراف را انتخاب کنید.");
  await api("/production-deviations","POST",{
   mpsLineId:$("#line").value,orderId:$("#order").value||null,
   category:Number($("#category").value),description:$("#description").value.trim()
  });
  $("#description").value="";notice("انحراف تولید ثبت شد.");
 });
 submit("#action",async()=>{
  const owner=selectedOwner("#action-owner");
  if(!$("#cause-group").value)throw Error("طبقه علت ریشه‌ای را تعیین کنید.");
  await api("/production-deviations/"+$("#action-case").value+"/action","POST",{
   rootCause:$("#cause").value.trim(),correctiveAction:$("#corrective").value.trim(),
   responsible:owner.label,responsibleCode:owner.code,
   rootCauseCategoryCode:$("#cause-group").value,actionDueJalali:$("#action-date").value
  });notice("اقدام با کدهای اطلاعات پایه ثبت شد.");
 });
 submit("#revise",async()=>{
  const owner=selectedOwner("#revise-owner");
  if(!$("#revise-cause-group").value)throw Error("طبقه علت ریشه‌ای جدید را انتخاب کنید.");
  await api("/production-deviations/"+$("#revise-case").value+"/revise-action","POST",{
   rootCause:$("#revise-cause").value.trim(),
   correctiveAction:$("#revise-corrective").value.trim(),
   responsible:owner.label,responsibleCode:owner.code,
   rootCauseCategoryCode:$("#revise-cause-group").value,
   actionDueJalali:$("#revise-date").value,changeReason:$("#revise-reason").value.trim()
  });notice("بازنگری با نگهداری سابقه قبلی ثبت شد.");
 });
 submit("#escalate",async()=>{
  await api("/production-deviations/"+$("#escalate-case").value+"/escalate","POST",{
   reason:$("#escalate-reason").value.trim(),reviewByJalali:$("#escalate-date").value||null
  });notice("تشدید پیگیری ثبت شد؛ اعلان خارجی ایجاد نشده است.");
 });
 submit("#close",async()=>{
  await api("/production-deviations/"+$("#close-case").value+"/close","POST",{
   verification:$("#verification").value.trim()
  });notice("اختتام اولیه ثبت شد؛ تأیید مستقل باقی است.");await refreshPending();
 });
 $("#followup-review").onclick=()=>reviewFollowup().catch(e=>notice(e.message,true));
 $("#effectiveness-load").onclick=()=>refreshPending().catch(e=>notice(e.message,true));
 submit("#effectiveness-form",async()=>{
  const id=$("#effectiveness-case").value;
  if(!id)throw Error("پرونده مختومه را انتخاب کنید.");
  const r=await api("/production-deviations/"+id+"/effectiveness/review","POST",{
   effective:$("#effectiveness-result").value==="true",
   evidenceReference:$("#effectiveness-ref").value.trim(),
   observation:$("#effectiveness-observation").value.trim()
  });
  $("#effectiveness-summary").textContent=r.message+" | مدرک: "+r.evidenceReference;
  notice(r.message);await refreshPending();
 });
 await renderCases();await refreshPending();
 const selected=new URLSearchParams(window.location.search).get("case");
 if(selected&&!data.deviations.some(x=>x.id===selected)){
  try{
   const old=await api("/production-deviations/"+encodeURIComponent(selected));
   data.deviations.push(old);fillCaseSelectors();
  }catch{ /* An invalid or inaccessible deep link does not reveal case data. */ }
 }
 if(selected&&data.deviations.some(x=>x.id===selected&&x.status!==2)){
  $("#escalate-case").value=selected;
  if(data.deviations.some(x=>x.id===selected&&x.status===1)){
   $("#revise-case").value=selected;prefillRevision();
  }
  reviewFollowup().catch(e=>notice(e.message,true));
 }
}
if(document.readyState==="loading")
 document.addEventListener("DOMContentLoaded",()=>boot().catch(e=>notice(e.message,true)));
else boot().catch(e=>notice(e.message,true));
})();
