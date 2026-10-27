(()=>{
"use strict";
const $=id=>document.getElementById(id);
let diagnostic=null;
let initialAnchorHandled=false;
const selected=(id)=>$(id)?.value?.trim()||"";
const format=n=>Number(n||0).toLocaleString("fa-IR");
const paths=new Set(["/planning.html","/planning-advanced.html","/execution.html"]);
function el(tag,text,className){
 const element=document.createElement(tag);
 if(text!==undefined)element.textContent=String(text);
 if(className)element.className=className;
 return element;
}
function message(text,error=false){
 const e=$("analytics-message");e.hidden=false;
 e.className="notice"+(error?" error":"");e.textContent=text;
}
async function api(path,method="GET",body){
 const result=await IPPCSSecurity.secureFetch("/api/v1"+path,{
  method,headers:body===undefined?{}:{"Content-Type":"application/json"},
  body:body===undefined?undefined:JSON.stringify(body)
 });
 let data;const text=await result.text();
 try{data=text?JSON.parse(text):null;}catch{data=null;}
 if(!result.ok)throw Error(data?.message||data?.detail||text?.slice(0,240)||
  "خطای دریافت نتیجه آزمایشی (HTTP "+result.status+")");
 return data;
}
function select(id,rows,label,preferred){
 const target=$(id),old=target.value;
 IPPCSChoices.fill(target,rows,{value:"id",label,placeholder:"انتخاب کنید"});
 if(rows.some(x=>x.id===old))target.value=old;
 else if(rows.some(x=>x.id===preferred))target.value=preferred;
}
function checkView(check){
 const item=el("div",undefined,"factory-analytic-check");
 const symbol=el("span",check.ready?"✓":"!");
 symbol.className=check.ready?"factory-analytic-done":"factory-analytic-missing";
 const text=el("div");
 text.append(el("strong",check.title+" — "+format(check.count)+" / "+format(check.minimum)),
  el("p",check.ready?"حداقل داده برای اجرای آزمون ثبت شده است.":check.advice));
 item.append(symbol,text);
 return item;
}
function display(){
 if(!diagnostic)return;
 const data=diagnostic;
 const container=$("analytics-cards");container.replaceChildren();
 $("analytics-total").textContent=String(data.modules.length).toLocaleString("fa-IR");
 $("analytics-ready").textContent=format(data.readyForGuidedTrial);
 $("analytics-evidence").textContent=format(data.recordedEvidenceModules);
 for(const module of data.modules){
  const box=el("article",undefined,"factory-uat-card factory-analytic-card");
  box.id="analytics-module-"+module.number;
  box.append(el("h4",String(module.number).padStart(2,"0")+" — "+module.title));
  const status=el("p",module.canRun?
   module.hasRecordedEvidence?"ورودی‌ها موجودند؛ شواهد قبلی نیز ثبت شده‌اند.":
    "ورودی پایه موجود است؛ هنوز نتیجهٔ قبلی ثبت نشده است.":
   "پیش‌نیازهای داده کامل نشده‌اند.");
  status.className=module.canRun?"factory-analytics-ready":"factory-analytics-blocked";
  box.append(status);
  const checks=el("div",undefined,"factory-analytic-checks");
  module.checks.forEach(check=>checks.append(checkView(check)));box.append(checks);
  box.append(el("p","شواهد قابل شمارش: "+format(module.evidenceCount)+
   " — "+module.evidenceKind));
  box.append(el("p",module.warning,"factory-analytic-warning"));
  const actions=el("div",undefined,"factory-analytic-actions");
  const button=el("button","اجرای آزمون محاسباتی / مشاهده نتیجه","btn-main");
  button.type="button";button.disabled=!module.canRun;
  const link=el("a","ورود به فرم اصلی ماژول","btn-outline factory-analytic-link");
  link.href=paths.has(module.page)?module.page+"#"+module.section:"/planning.html";
  actions.append(button,link);
  box.append(actions);
  const result=el("div",undefined,"factory-analytic-result");
  result.setAttribute("role","status");result.setAttribute("aria-live","polite");
  result.hidden=true;
  box.append(result);
  button.onclick=async()=>{
   button.disabled=true;result.hidden=false;result.replaceChildren();
   result.append(el("p","در حال بررسی دادهٔ انتخاب‌شده…"));
   try{
    const data=await run(module.liveTestRoute);
    result.replaceChildren();
    const status=el("strong","نتیجهٔ سرویس دریافت شد؛ اعتبارسنجی کارخانه هنوز باز است.");
    result.append(status);
    const raw=JSON.stringify(data,null,2);
    const bounded=raw.length>18000?
      raw.slice(0,18000)+"\n... بخش انتهایی برای خوانایی حذف شد؛ گزارش کامل را از صفحهٔ اصلی ماژول بررسی کنید.":
      raw;
    const summary=el("pre",bounded,"factory-analytic-json");
    result.append(summary);
   }catch(error){
    result.replaceChildren();
    result.append(el("strong","آزمون محاسباتی با خطا یا دادهٔ ناکافی مواجه شد."),
      el("p",error.message));
   }finally{button.disabled=false;}
  };
  container.append(box);
 }
 if(!initialAnchorHandled){
  const hash=decodeURIComponent(location.hash.slice(1));
  if(/^analytics-module-(2|3|5|6|7|14|16|17)$/.test(hash)){
   const card=document.getElementById(hash);
   if(card)card.scrollIntoView({block:"start"});
  }
  initialAnchorHandled=true;
 }
}
function assertGuid(guid,label){
 if(!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(guid||""))
  throw Error("برای "+label+" یک رکورد معتبر از لیست انتخاب کنید.");
 return encodeURIComponent(guid);
}
function requireDate(id){
 const date=selected(id);
 if(!/^\d{4}\/\d{2}\/\d{2}$/.test(IPPCSJalali.toLatin(date)))
  throw Error("تاریخ شمسی آزمایش را با تقویم انتخاب کنید.");
 return encodeURIComponent(IPPCSJalali.toLatin(date));
}
function sample(id,label){
 if(!diagnostic?.examples?.[id])throw Error("سابقهٔ "+label+
  " ثبت نشده است. ابتدا از صفحهٔ اصلی ماژول یک محاسبهٔ آزمایشی انجام دهید.");
 return assertGuid(diagnostic.examples[id],label);
}
async function run(route){
 const mps=()=>assertGuid(selected("analytics-mps"),"MPS");
 const item=()=>assertGuid(selected("analytics-item"),"محصول");
 const order=()=>assertGuid(selected("analytics-order"),"دستور ساخت");
 const date=()=>requireDate("analytics-date");
 switch(route){
 case "forecast-backtest":
  return api("/factory-demand/backtest/"+item()+"?throughJalali="+
   requireDate("analytics-closed")+"&lookback=2&months=2");
 case "sop-snapshot":{
  const results=await api("/factory-sop");
  if(!results.length)throw Error("هنوز Snapshot S&OP ثبت نشده است؛ از فرم S&OP محاسبه و ثبت کنید.");
  return {snapshotCount:results.length,recent:results[0],
   warning:"این عدد نتیجه آزمون پذیرش کارخانه نیست؛ فقط آخرین Snapshot داخلی ثبت‌شده است."};
 }
 case "mrp-snapshot":
  return api("/mrp/runs/"+sample("recentMrpRunId","محاسبه MRP"));
 case "crp-preview":
  return api("/crp/"+mps()+"/preview");
 case "aps-snapshot":
  return api("/factory-aps/runs/"+sample("recentApsRunId","APS"));
 case "oee-clock":
  return api("/oee/clock-verified?dateJalali="+date());
 case "whatif-live":{
  const horizon=Number(IPPCSJalali.toLatin(selected("analytics-horizon")));
  const factor=Number(IPPCSJalali.toLatin(selected("analytics-factor")));
  if(!Number.isInteger(horizon)||horizon<1||horizon>90||
   !Number.isFinite(factor)||factor<=0||factor>2)
   throw Error("افق ۱ تا ۹۰ روز و ضریب ظرفیت (۰ تا ۲] معتبر لازم است.");
  return api("/factory-whatif/capacity","POST",{
   mpsVersionId:decodeURIComponent(mps()),
   startJalali:IPPCSJalali.toLatin(selected("analytics-date")),
   horizonDays:horizon,baselineCapacity:1,scenarioCapacity:factor
  });
 }
 case "cost-variance":
  return api("/factory-cost/orders/"+order()+"/actual-variance");
 default:throw Error("آزمون ماژول انتخاب‌شده معتبر نیست.");
 }
}
async function refresh(){
 const button=$("analytics-refresh");button.disabled=true;
 try{
  const [info,items,plans,orders]=await Promise.all([
   api("/factory-analytical-verification"),
   api("/items"),api("/mps"),api("/manufacturing-orders")
  ]);
  if(info.type!=="DevelopmentAnalyticalVerification"||
     info.releaseAuthorized!==false||info.factoryAccepted!==0||
     !Array.isArray(info.modules)||info.modules.length!==8)
   throw Error("وضعیت گیت‌های آزمون ناقص است و نباید به‌عنوان پذیرش استفاده شود.");
  diagnostic=info;
  select("analytics-item",items.filter(x=>x.type===2),x=>x.code+" — "+x.name,
   info.examples.finishedItemId);
  select("analytics-mps",plans.filter(x=>x.state===1&&x.lines?.length),
   x=>x.code,info.examples.approvedMpsId);
  select("analytics-order",orders,x=>x.number+" — "+
   ["پیش‌نویس","آزاد","در جریان","معلق","کامل","لغوشده"][x.status],
   info.examples.recentOrderId);
  if(!selected("analytics-closed"))
   $("analytics-closed").value=info.examples.closedMonthJalali;
  if(!selected("analytics-date"))
   $("analytics-date").value=info.examples.todayJalali;
  display();message(info.disclaimer);
 }catch(err){message(err.message,true);}
 finally{button.disabled=false;}
}
function init(){
 $("analytics-refresh").addEventListener("click",refresh);
 refresh();
}
if(document.readyState==="loading")
 document.addEventListener("DOMContentLoaded",init);
else init();
})();
