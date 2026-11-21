(()=>{
"use strict";
const $=id=>document.getElementById(id);
let all=[],admin=false;
function msg(t,error=false){const e=$("master-notice");e.hidden=false;
 e.className="notice"+(error?" error":"");e.textContent=t;}
async function api(url,method="GET",body){
 const response=await IPPCSSecurity.secureFetch("/api/v1"+url,{
  method,headers:body===undefined?{}:{"Content-Type":"application/json"},
  body:body===undefined?undefined:JSON.stringify(body)
 });
 const raw=await response.text();let data=null;
 try{data=raw?JSON.parse(raw):null;}catch{data=null;}
 if(!response.ok)throw new Error(data?.message||"خطا در ثبت اطلاعات پایه ("+response.status+")");
 return data;
}
function make(tag,className){const el=document.createElement(tag);if(className)el.className=className;return el;}
function cell(tr,child){const td=make("td");if(child instanceof Node)td.append(child);
 else td.textContent=String(child??"—");tr.append(td);return td;}
function render(){
 const q=$("master-query").value.trim().toLocaleLowerCase("fa");
 const showInactive=$("master-inactive").checked;
 const rows=all.filter(x=>(showInactive||x.isActive)&&
  (!q||(x.code+" "+x.label).toLocaleLowerCase("fa").includes(q)));
 $("master-total").textContent=all.length.toLocaleString("fa-IR");
 $("master-active").textContent=all.filter(x=>x.isActive).length.toLocaleString("fa-IR");
 $("master-disabled").textContent=all.filter(x=>!x.isActive).length.toLocaleString("fa-IR");
 const host=$("master-list");host.replaceChildren();
 if(!rows.length){const p=make("p");p.textContent="گزینه‌ای مطابق فیلتر یافت نشد.";host.append(p);return;}
 const wrap=make("div","table-wrap"),table=make("table","factory-master-table");
 const thead=make("thead"),header=make("tr");
 for(const label of ["کد ثابت","عنوان فارسی","ترتیب","وضعیت","عملیات"]){
  const th=make("th");th.textContent=label;header.append(th);
 }
 thead.append(header);table.append(thead);
 const body=make("tbody");
 for(const x of rows){
  const tr=make("tr");
  cell(tr,x.code);
  const input=make("input");
  input.value=x.label;input.maxLength=100;input.required=true;
  input.setAttribute("aria-label","عنوان "+x.code);
  input.disabled=!admin;
  cell(tr,input);
  const sort=make("input");
  sort.type="number";sort.min="0";sort.max="10000";sort.value=String(x.sortOrder);
  sort.setAttribute("aria-label","ترتیب "+x.code);sort.disabled=!admin;
  sort.className="factory-sort";
  cell(tr,sort);
  const check=make("input");
  check.type="checkbox";check.checked=x.isActive;check.disabled=!admin;
  check.setAttribute("aria-label","فعال بودن "+x.code);
  const checkWrap=make("label","factory-inline-check");
  checkWrap.append(check,document.createTextNode("فعال"));
  cell(tr,checkWrap);
  if(admin){
   const button=make("button","btn-outline");
   button.type="button";button.textContent="ذخیره";
   button.onclick=async()=>{
    button.disabled=true;
    try{
     const label=input.value.trim(),sortOrder=Number(sort.value);
     if(label.length<2||label.length>100||
      !Number.isInteger(sortOrder)||sortOrder<0||sortOrder>10000)
      throw new Error("عنوان ۲ تا ۱۰۰ حرف و ترتیب صحیح ۰ تا ۱۰۰۰۰ لازم است.");
     await api("/factory-master-options/"+x.id,"PUT",
       {label,sortOrder,isActive:check.checked});
     msg("تغییرات گزینه «"+x.code+"» ذخیره شد.");
     await reload();
    }catch(e){msg(e.message,true);}finally{button.disabled=false;}
   };cell(tr,button);
  }else cell(tr,"فقط مدیر سامانه");
  body.append(tr);
 }
 table.append(body);wrap.append(table);host.append(wrap);
}
async function reload(){
 const kind=$("master-kind").value;
 all=await api("/factory-master-options?kind="+encodeURIComponent(kind)+"&includeInactive=true");
 render();
 $("master-code").readOnly=kind==="DeviationCategory";
 $("master-code").placeholder=kind==="DeviationCategory"?"کدهای ۰ تا ۶ از قبل تعریف شده‌اند":
  kind==="UnitOfMeasure"?"مثلاً N، PCS یا KG":"مثلاً SHIFT_LEAD";
}
async function reasonReport(event){
 event.preventDefault();
 const form=$("master-reason-report");
 if(!form.reportValidity())return;
 const kind=$("reason-report-kind").value,
  fromJalali=$("reason-report-from").value.trim(),
  throughJalali=$("reason-report-through").value.trim();
 const params=new URLSearchParams({kind,fromJalali,throughJalali});
 const result=await api("/factory-master-options/production-reason-summary?"+params);
 const host=$("reason-report-result");host.replaceChildren();
 const summary=make("p");
 summary.textContent=result.warning;
 host.append(summary);
 const wrap=make("div","table-wrap"),table=make("table"),head=make("tr");
 for(const heading of ["کد","عنوان مرجع","وضعیت","تعداد رخداد","مقدار جمع‌شده"]){
  const th=make("th");th.textContent=heading;head.append(th);
 }
 table.append(head);
 for(const row of result.rows){
  const tr=make("tr");
  for(const value of [row.code,row.label,row.active?"فعال":"غیرفعال/قدیمی",
   row.records.toLocaleString("fa-IR"),
   row.measuredQuantity.toLocaleString("fa-IR")]){
   const td=make("td");td.textContent=value;tr.append(td);
  }
  table.append(tr);
 }
 if(!result.rows.length){
  const p=make("p");p.textContent="در بازه انتخاب‌شده رخدادی وجود ندارد.";host.append(p);
 }
 wrap.append(table);host.append(wrap);
}
async function boot(){
 const response=await IPPCSSecurity.secureFetch("/auth/me");
 if(response.status===401){IPPCSSecurity.goLogin();return;}
 if(!response.ok)throw Error("احراز نقش کاربر ممکن نیست.");
 const user=await response.json();
 admin=(user.roles||[]).includes("Administrator");
 $("master-create").hidden=!admin;
 $("master-kind").onchange=()=>{ $("master-query").value="";reload().catch(e=>msg(e.message,true));};
 $("master-query").oninput=render;
 $("master-inactive").onchange=render;
 $("master-reload").onclick=()=>reload().catch(e=>msg(e.message,true));
 $("master-reason-report").onsubmit=event=>reasonReport(event).catch(e=>msg(e.message,true));
 $("master-create").onsubmit=async event=>{
  event.preventDefault();
  const button=$("master-add");button.disabled=true;
  try{
   const kind=$("master-kind").value;
   if(kind==="DeviationCategory")throw Error("دسته‌های ثابت انحراف از قبل در اطلاعات پایه وجود دارند.");
   const code=$("master-code").value.trim(),label=$("master-label").value.trim(),
    sortOrder=Number($("master-sort").value);
   const valid=kind==="UnitOfMeasure"?
    /^[A-Za-z][A-Za-z0-9_-]{0,39}$/.test(code):
    /^[A-Za-z0-9][A-Za-z0-9_-]{1,39}$/.test(code);
   if(!valid)
    throw Error("کد ثابت نامعتبر است؛ واحدهای SI مثل N یک‌حرفی مجازند و سایر کدها حداقل ۲ نویسه دارند.");
   await api("/factory-master-options","POST",{kind,code,label,sortOrder});
   $("master-code").value="";$("master-label").value="";
   msg("گزینهٔ جدید ثبت شد و اکنون برای انتخاب کاربران فعال است.");
   await reload();
  }catch(e){msg(e.message,true);}finally{button.disabled=false;}
 };
 await reload();
}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",()=>boot().catch(e=>msg(e.message,true)));
else boot().catch(e=>msg(e.message,true));
})();
