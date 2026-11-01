(()=>{
"use strict";
const $=id=>document.getElementById(id);
const formatNumber=n=>new Intl.NumberFormat("fa-IR",{maximumFractionDigits:4}).format(Number(n)||0);
const guid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function message(text,failed=false){
 const node=$("recall-message");
 node.hidden=false;
 node.className="notice"+(failed?" error":"");
 node.textContent=text;
}
function asCell(text){
 const td=document.createElement("td");
 td.textContent=String(text??"—");
 return td;
}
function draw(data){
 $("recall-results").hidden=false;
 $("recall-orders").textContent=formatNumber(data.finalAssemblyOrders);
 $("recall-serial-count").textContent=formatNumber(data.registeredSerialCount);
 const unknown=(data.finalLots||[]).filter(x=>Number(x.unserializedGood)>0);
 $("recall-unknown-count").textContent=formatNumber(unknown.length);
 const caution=$("recall-caution");
 caution.hidden=false;
 caution.textContent=unknown.length?
   "هشدار: دست‌کم یک بچ نهایی در این مسیر دارای خروجی سالم فاقد شماره‌سریال ثبت‌شده است. دامنه ریسک را نباید صرفاً به سریال‌های این فهرست محدود کرد.":
   "همه خروجی‌های سالمِ بچ‌های نهاییِ پیدا‌شده در این مسیر دارای شماره‌سریال ثبت‌شده هستند؛ نبود پیوندهای ثبت‌شده همچنان به معنی نبود ریسک نیست.";
 const body=$("recall-lots");body.replaceChildren();
 if(!data.finalLots?.length){
  const tr=document.createElement("tr"),empty=asCell(
   "در مسیر ثبت‌شده، بچ نهایی شناسایی نشد؛ این موضوع بی‌خطر بودن بچ مبدأ را ثابت نمی‌کند.");
  empty.colSpan=6;empty.className="empty";tr.append(empty);body.append(tr);
 }
 for(const row of data.finalLots||[]){
  const tr=document.createElement("tr");
  for(const value of [
    row.orderNumber,row.lotCode,row.confirmationId,
    formatNumber(row.recordedGood),formatNumber(row.registeredSerials),
    formatNumber(row.unserializedGood)]){
   const td=asCell(value);
   if(value===row.confirmationId)td.dir="ltr";
   tr.append(td);
  }
  body.append(tr);
 }
 const serials=$("recall-serials");serials.replaceChildren();
 if(data.serialNumbers?.length){
  const list=document.createElement("ul");
  for(const value of data.serialNumbers){
   const li=document.createElement("li");
   li.textContent=value;li.dir="ltr";list.append(li);
  }
  serials.append(list);
 }else{
  const p=document.createElement("p");
  p.textContent="هیچ سریالی در مسیر مستند ثبت نشده است؛ این به معنی تولید نشدن محصول نیست.";
  serials.append(p);
 }
 $("recall-disclaimer").textContent=data.disclaimer||"";
 $("recall-trace-count").textContent=
  "بچ‌های موجود در شجره: "+formatNumber(data.genealogyNodes)+
  " | پیوندهای ثبت‌شده: "+formatNumber(data.genealogyEdges);
}
async function search(event){
 event.preventDefault();
 const source=$("recall-source").value.trim();
 const depth=Number($("recall-depth").value);
 if(!guid.test(source)||!Number.isInteger(depth)||depth<1||depth>16){
  message("شناسه معتبر GUID و عمق بین ۱ تا ۱۶ لازم است.",true);
  return;
 }
 const button=$("recall-submit");button.disabled=true;
 $("recall-results").hidden=true;
 message("در حال بررسی شجره و دفتر سریال‌های ثبت‌شده...");
 try{
  const res=await IPPCSSecurity.secureFetch(
   "/api/v1/factory-recall/source-lot/"+encodeURIComponent(source)+
   "?depth="+encodeURIComponent(String(depth)),{cache:"no-store"});
  const data=await res.json().catch(()=>({}));
  if(!res.ok){
   throw new Error(res.status===404?"بچ انتخاب‌شده در گزارش‌های تولید یافت نشد.":
    data.message||"گزارش به دلیل نقص داده یا محدودیت بررسی قابل تأیید نیست.");
  }
  draw(data);
  message("گزارش فقط بر اساس شواهد ثبت‌شده IPPCS تهیه شد؛ هشدارهای اعتبار شجره را بخوانید.");
 }catch(error){
  message(error.message||"دریافت گزارش ناموفق بود.",true);
 }finally{button.disabled=false;}
}
document.addEventListener("DOMContentLoaded",()=>{
 $("recall-form").addEventListener("submit",search);
 const value=new URLSearchParams(location.search).get("lot")||"";
 if(guid.test(value))$("recall-source").value=value;
});
})();
