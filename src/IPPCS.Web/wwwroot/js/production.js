(() => {
  "use strict";
  const modules = [
    ["داده‌های پایه تولید","اقلام، BOM، مسیر ساخت و مراکز کاری","پایه در حال توسعه"],
    ["مدیریت تقاضای تولید","ورود نیازهای تولید و پیش‌بینی برنامه‌ای","در دست توسعه"],
    ["برنامه‌ریزی کلان S&OP","تراز تقاضا، ظرفیت و موجودی برنامه‌ای","در دست توسعه"],
    ["برنامه اصلی تولید MPS","نسخه‌ها و برنامه تولید محصول نهایی","پایه در حال توسعه"],
    ["نیازسنجی مواد MRP","انفجار BOM، کسری خالص و Pegging","فقط نیاز ناخالص"],
    ["ظرفیت‌سنجی CRP","بارگذاری مراکز، محدودیت و تقویم","پایه تقویم منابع"],
    ["زمان‌بندی پیشرفته APS","گانت، قیود، گلوگاه و بهینه‌سازی","در دست توسعه"],
    ["دستورات تولید","صدور، آزادسازی و اختتام ساخت","در دست توسعه"],
    ["کنترل نیمه‌ساخته","عملیات چندمرحله‌ای و جریان WIP","در دست توسعه"],
    ["کنترل مونتاژ نهایی","برنامه خطوط و تأیید مونتاژ","در دست توسعه"],
    ["مواد پای خط","تخصیص، مصرف و برگشت در عملیات تولید","در دست توسعه"],
    ["تحقق برنامه تولید","انحراف برنامه، دلایل و اقدامات","در دست توسعه"],
    ["کنترل کیفیت تولید","قرنطینه، ضایعات و دوباره‌کاری","در دست توسعه"],
    ["توقفات و OEE","دسترس‌پذیری، عملکرد و کیفیت","در دست توسعه"],
    ["ردیابی ساخت","ردیابی بچ، عملیات و محصول","در دست توسعه"],
    ["سناریوسازی تولید","What-if و شبیه‌سازی محدودیت‌ها","در دست توسعه"],
    ["تحلیل هزینه و بهره‌وری","هزینه تبدیل، ضایعات و ظرفیت","در دست توسعه"],
    ["داشبورد و هوشمندسازی","شاخص‌ها، هشدارها و توصیه‌ها","در دست توسعه"]
  ];
  const state={items:[],centers:[],routings:[],shifts:[],plans:[]};
  const $=sel=>document.querySelector(sel);
  const $$=sel=>Array.from(document.querySelectorAll(sel));
  const nameBy = (rows,id) => rows.find(x=>x.id===id)?.name || rows.find(x=>x.id===id)?.code || String(id).slice(0,8);
  const num = val=>Number(val).toLocaleString("fa-IR");
  const fval=(form,name)=>new FormData(form).get(name);
  const decimal=(form,name)=>Number(IPPCSJalali.toLatin(fval(form,name)));
  const dayNames=["یکشنبه","دوشنبه","سه‌شنبه","چهارشنبه","پنجشنبه","جمعه","شنبه"];
  function notice(text,error=false){
    const el=$("#message");el.hidden=false;el.className="notice"+(error?" error":"");el.textContent=text;el.scrollIntoView({behavior:"smooth",block:"nearest"});
  }
  async function api(url,method="GET",payload){
    const options={method,headers:{}};
    if(payload!==undefined){options.headers["Content-Type"]="application/json";options.body=JSON.stringify(payload);}
    const response=await fetch(url,options);
    if(response.ok){const text=await response.text();return text?JSON.parse(text):null;}
    let error;
    try { const raw=await response.text();let data=JSON.parse(raw); error=data.message||data.title||data.detail||raw; } catch {error="پاسخ سرور نامعتبر است.";}
    throw new Error(`خطای ${response.status}: ${error}`);
  }
  function table(host,columns,records,action) {
    host.replaceChildren();
    const box=document.createElement("div");box.className="table-wrap";
    const t=document.createElement("table");const thead=document.createElement("thead");const head=document.createElement("tr");
    columns.forEach(([title])=>{const th=document.createElement("th");th.textContent=title;head.append(th);});
    if(action){const th=document.createElement("th");th.textContent="عملیات";head.append(th);}
    thead.append(head);t.append(thead);
    const body=document.createElement("tbody");
    if(!records.length){const tr=document.createElement("tr"),td=document.createElement("td");td.className="empty";td.colSpan=columns.length+(action?1:0);td.textContent="رکوردی برای نمایش وجود ندارد.";tr.append(td);body.append(tr);}
    records.forEach(row=>{
      const tr=document.createElement("tr");
      columns.forEach(([,key])=>{
        const td=document.createElement("td");const value=typeof key==="function"?key(row):row[key];
        td.textContent=value===undefined||value===null?"—":String(value);tr.append(td);
      });
      if(action){const td=document.createElement("td");const button=document.createElement("button");button.className="btn-outline";button.type="button";button.textContent=action.label(row);button.disabled=action.disabled?.(row)||false;button.onclick=()=>action.run(row).catch(e=>notice(e.message,true));td.append(button);tr.append(td);}
      body.append(tr);
    });
    t.append(body);box.append(t);host.append(box);
  }
  function fillSelect(select,rows,placeholder="انتخاب کنید"){
    const prev=select.value;select.replaceChildren();
    const blank=document.createElement("option");blank.value="";blank.textContent=placeholder;select.append(blank);
    rows.forEach(row=>{const opt=document.createElement("option");opt.value=row.id;opt.textContent=`${row.code||row.revisionCode||row.name} — ${row.name||row.revisionCode||""}`;select.append(opt);});
    if([...select.options].some(o=>o.value===prev))select.value=prev;
  }
  const stringDate=iso=>{
    const date=new Date(iso);
    return Number.isNaN(date.getTime())?"—":date.toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"UTC",year:"numeric",month:"2-digit",day:"2-digit"});
  };
  function displayItems(){
    table($("#item-list"),[["کد", "code"],["عنوان", "name"],["واحد", "unit"],["نوع",x=>["مواد اولیه","نیمه‌ساخته","محصول نهایی","مصرفی"][x.type]||x.type]],state.items);
    $$("[data-items]").forEach(x=>fillSelect(x,state.items));
  }
  function displayCenters(){
    table($("#center-list"),[["کد","code"],["شرح مرکز","name"],["ظرفیت اسمی / ساعت",x=>num(x.nominalUnitsPerHour)]],state.centers);
    $$("[data-centers]").forEach(x=>fillSelect(x,state.centers));
  }
  async function displayBom(){
    const parent=$("#bom-parent").value;const data=parent?await api(`/api/v1/bom/${parent}`):[];
    table($("#bom-list"),[["ترتیب","sequence"],["جزء",x=>nameBy(state.items,x.componentItemId)],["مصرف",x=>num(x.quantityPer)],["ضایعات %",x=>num(x.scrapPercent)]],data);
  }
  function displayRoutings(){
    fillSelect($("#operation-route"),state.routings.filter(x=>x.state===0).map(x=>({id:x.id,code:x.revisionCode,name:nameBy(state.items,x.itemId)})));
    table($("#routing-list"),
      [["محصول",x=>nameBy(state.items,x.itemId)],["نسخه","revisionCode"],["شروع اعتبار","effectiveFromJalali"],
        ["پایان اعتبار",x=>x.effectiveToJalali||"بدون پایان"],["وضعیت",x=>["پیش‌نویس","تأییدشده","جایگزین‌شده"][x.state]||x.state],
        ["عملیات",x=>(x.operations||[]).map(o=>`${o.sequence}: ${o.name} (${nameBy(state.centers,o.workCenterId)})`).join(" | ")||"—"]],
      state.routings, {label:r=>r.state===0?"تأیید مسیر":"نهایی شده",disabled:r=>r.state!==0,run:async r=>{
        await api(`/api/v1/routings/${r.id}/approve`,"POST");notice("نسخه مسیر تأیید شد.");await reload();
      }});
  }
  function displayShifts(){
    table($("#shift-list"),[["مرکز کاری",x=>nameBy(state.centers,x.workCenterId)],["روز",x=>dayNames[x.dayOfWeek]],
      ["از",x=>String(x.startTime).slice(0,5)],["تا",x=>String(x.endTime).slice(0,5)],["ضریب ظرفیت %",x=>num(x.effectiveCapacityPercent)]],state.shifts);
  }
  function displayPlans(){
    table($("#plan-list"),[["محصول",x=>nameBy(state.items,x.itemId)],["مقدار",x=>num(x.quantity)],["موعد شمسی",x=>stringDate(x.dueDateUtc)]],state.plans,
      {label:()=>"محاسبه نیاز مواد",run:async row=>{
        const data=await api(`/api/v1/planning/gross-requirements/${row.id}`);
        $("#requirements-panel").hidden=false;
        table($("#requirements-list"),[["قطعه",x=>nameBy(state.items,x.itemId)],["نیاز ناخالص",x=>num(x.quantity)]],data.requirements);
        $("#requirements-panel").scrollIntoView({behavior:"smooth"});
      }});
  }
  async function reload(){
    const paths=["/api/v1/items","/api/v1/work-centers","/api/v1/routings","/api/v1/work-shifts","/api/v1/production-plans"];
    const keys=["items","centers","routings","shifts","plans"];
    const result=await Promise.all(paths.map(p=>api(p)));
    keys.forEach((k,i)=>{state[k]=result[i];});
    displayItems();displayCenters();displayRoutings();displayShifts();displayPlans();await displayBom();
  }
  function formHandler(id,send){
    const form=$(id);
    form.addEventListener("submit",async e=>{
      e.preventDefault();
      if(!form.reportValidity())return;
      const button=form.querySelector('[type="submit"]');button.disabled=true;
      try {await send(form);notice("اطلاعات با موفقیت ثبت شد.");form.reset();await reload();}
      catch(ex){notice(ex.message,true);}finally{button.disabled=false;}
    });
  }
  function start(){
    const grid=$("#module-grid");
    modules.forEach(([title,description,status],i)=>{
      const card=document.createElement("div");card.className="module-card";
      const h=document.createElement("b");h.textContent=`${String(i+1).padStart(2,"0")} · ${title}`;
      const p=document.createElement("span");p.textContent=description;
      const badge=document.createElement("span");badge.className="tag";badge.textContent=status;
      card.append(h,p,document.createElement("br"),badge);grid.append(card);
    });
    $$("#primary-nav button").forEach(b=>b.addEventListener("click",()=>{
      const page=b.dataset.page;
      $$(".page").forEach(section=>section.hidden=section.id!==page);
      $$("#primary-nav button").forEach(btn=>btn.classList.toggle("nav-active",btn===b));
      $("#page-title").textContent=b.textContent;
      $("#side").classList.remove("open");
      $("#message").hidden=true;
    }));
    $("#menu-toggle").onclick=()=>$("#side").classList.toggle("open");
    $("#bom-parent").onchange=()=>displayBom().catch(e=>notice(e.message,true));
    formHandler("#item-form", f=>api("/api/v1/items","POST",{
      code:fval(f,"code"),name:fval(f,"name"),unit:fval(f,"unit"),type:Number(fval(f,"type"))
    }));
    formHandler("#center-form",f=>api("/api/v1/work-centers","POST",{
      code:fval(f,"code"),name:fval(f,"name"),nominalUnitsPerHour:decimal(f,"rate")
    }));
    formHandler("#bom-form",f=>api("/api/v1/bom","POST",{
      parentItemId:fval(f,"parent"),componentItemId:fval(f,"component"),
      quantityPer:decimal(f,"quantity"),scrapPercent:decimal(f,"scrap"),sequence:decimal(f,"sequence")
    }));
    formHandler("#routing-form",f=>api("/api/v1/routings","POST",{
      itemId:fval(f,"itemId"),revisionCode:fval(f,"revisionCode"),
      effectiveFromJalali:fval(f,"from"),effectiveToJalali:fval(f,"to")||null
    }));
    formHandler("#operation-form",f=>api(`/api/v1/routings/${fval(f,"routeId")}/operations`,"POST",{
      sequence:decimal(f,"sequence"),name:fval(f,"name"),workCenterId:fval(f,"centerId"),
      setupMinutes:decimal(f,"setup"),runMinutesPerUnit:decimal(f,"run"),yieldPercent:decimal(f,"yield")
    }));
    formHandler("#shift-form",f=>api("/api/v1/work-shifts","POST",{
      workCenterId:fval(f,"centerId"),dayOfWeek:decimal(f,"day"),
      startTime:fval(f,"start"),endTime:fval(f,"end"),effectiveCapacityPercent:decimal(f,"capacity")
    }));
    formHandler("#plan-form",f=>api("/api/v1/production-plans/jalali","POST",{
      itemId:fval(f,"itemId"),quantity:decimal(f,"quantity"),dueDateJalali:fval(f,"due")
    }));
    reload().catch(e=>notice("دریافت داده‌ها امکان‌پذیر نیست. ابتدا دیتابیس توسعه و Migrationها را آماده کنید. "+e.message,true));
  }
  if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",start);else start();
})();
