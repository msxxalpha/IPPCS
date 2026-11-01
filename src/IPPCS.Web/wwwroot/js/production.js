(() => {
  "use strict";
  // Navigation reflects implemented test screens, not a plant acceptance claim.
  const modules = [
    ["اطلاعات پایه، BOM و Routing","ساختار مهندسی نسخه‌دار و تغییرات ECO","قابل آزمایش؛ UAT باز","/app.html"],
    ["تقاضا و پیش‌بینی","تقاضای داخلی و ارزیابی دقت ماهانه","قابل آزمایش؛ UAT باز","/planning-advanced.html"],
    ["برنامه‌ریزی کلان S&OP","سناریوهای تراز تقاضا و ظرفیت","قابل آزمایش؛ UAT باز","/planning-advanced.html"],
    ["برنامه اصلی تولید MPS","برنامه مصوب و بازنگری کنترل‌شده","قابل آزمایش؛ UAT باز","/planning.html"],
    ["نیازسنجی مواد MRP","پیشنهاد خالص چندسطحی و منشأ فرضیات","قابل آزمایش؛ UAT باز","/planning.html"],
    ["ظرفیت‌سنجی CRP","ظرفیت روزانه، شیفت شب و گلوگاه","قابل آزمایش؛ UAT باز","/planning.html"],
    ["زمان‌بندی APS","جدول محدود منابع و گانت با هشدار","قابل آزمایش؛ UAT باز","/planning-advanced.html"],
    ["مدیریت دستورات ساخت","آزادسازی، تعلیق، پیگیری و تاریخچه","قابل آزمایش؛ UAT باز","/execution.html"],
    ["جریان نیمه‌ساخته و WIP","تأیید عملیات، ضایعات و انتقال بچ","قابل آزمایش؛ UAT باز","/execution.html"],
    ["مونتاژ نهایی و EOL","کنترل پایان خط و منشأ بچ مونتاژ","قابل آزمایش؛ UAT باز","/control-plan.html"],
    ["مصرف مواد پای خط","گزارش محلی مصرف، مغایرت و اصلاح","قابل آزمایش؛ UAT باز","/execution.html"],
    ["انحراف و CAPA","علت مرجع و اثربخشی مستقل اقدام","قابل آزمایش؛ UAT باز","/production-deviations.html"],
    ["کنترل کیفیت و SPC","کنترل پلن، اندازه‌گیری و قرنطینه","قابل آزمایش؛ UAT باز","/control-plan.html"],
    ["توقفات و OEE","توقف زمان‌دار و تطبیق شاخص‌ها","قابل آزمایش؛ UAT باز","/execution.html"],
    ["شجره بچ و سریال","ردیابی، دامنه اثر و فراخوان محدود","قابل آزمایش؛ UAT باز","/factory-recall.html"],
    ["What-if و سناریوها","سنجش فرضیات بدون تغییر برنامه مصوب","قابل آزمایش؛ UAT باز","/planning-advanced.html"],
    ["هزینه تبدیل","نرخ داخلی و مغایرت هزینه واقعی/استاندارد","قابل آزمایش؛ UAT باز","/planning-advanced.html"],
    ["داشبورد و شاخص‌ها","گزارش‌های تحلیلی تولید، کیفیت و CAPA","قابل آزمایش؛ UAT باز","/factory-reports.html"]
  ];
  const state={items:[],centers:[],routings:[],shifts:[],plans:[],productCapacities:[]};
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
    const response=await window.IPPCSSecurity.secureFetch(url,options);
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
    IPPCSChoices.fill(select,rows,{
      placeholder,value:"id",
      label:row=>`${row.code||row.revisionCode||row.name} — ${row.name||row.revisionCode||""}`
    });
  }
  const stringDate=iso=>{
    const date=new Date(iso);
    return Number.isNaN(date.getTime())?"—":date.toLocaleDateString("fa-IR-u-ca-persian",{timeZone:"UTC",year:"numeric",month:"2-digit",day:"2-digit"});
  };
  async function loadItemUnitChoices(){
    const rows=await api("/api/v1/factory-master-options?kind=UnitOfMeasure");
    const control=$("#item-unit");
    IPPCSChoices.fill(control,rows,{value:"code",
      label:unit=>unit.code+" — "+unit.label,
      placeholder:"واحد اندازه‌گیری را انتخاب کنید"});
    if(rows.some(x=>x.code==="PCS"))control.value="PCS";
    if(!rows.length)notice("واحد اندازه‌گیری در اطلاعات پایه وجود ندارد؛ مدیر سامانه باید آن را ثبت کند.",true);
  }
  function displayItems(){
    table($("#item-list"),[["کد", "code"],["عنوان", "name"],["واحد", "unit"],["نوع",x=>["مواد اولیه","نیمه‌ساخته","محصول نهایی","مصرفی"][x.type]||x.type]],state.items);
    $("[data-items]").forEach(x=>fillSelect(x,state.items));
    const products=state.items.filter(x=>x.type===1||x.type===2);
    IPPCSChoices.fill($("#center-cap-item"),products,{
      value:"id",label:x=>x.code+" — "+x.name,
      placeholder:"محصول نهایی یا نیمه‌ساخته را انتخاب کنید"
    });
  }
  function displayCenters(){
    table($("#center-list"),[["کد","code"],["شرح مرکز","name"],["ظرفیت مرجع عمومی / ساعت",x=>num(x.nominalUnitsPerHour)]],state.centers);
    $("[data-centers]").forEach(x=>fillSelect(x,state.centers));
  }
  function updateProductCapacityOperations(){
    const itemId=$("#center-cap-item").value;
    const centerId=$("#center-cap-center").value;
    const options=[];
    for(const route of state.routings.filter(x=>x.itemId===itemId&&x.state!==2)){
      for(const operation of route.operations||[]){
        if(operation.workCenterId!==centerId)continue;
        options.push({
          id:operation.id,itemId:route.itemId,workCenterId:operation.workCenterId,
          routeId:route.id,revisionCode:route.revisionCode,routingState:route.state,
          sequence:operation.sequence,operationName:operation.name,
          name:operation.name
        });
      }
    }
    const select=$("#center-cap-operation");
    IPPCSChoices.fill(select,options,{
      value:"id",
      label:x=>`نسخه ${x.revisionCode} · عملیات ${x.sequence}: ${x.operationName} · ${["پیش‌نویس","مصوب","جایگزین"][x.routingState]||x.routingState}`,
      placeholder:options.length?"عملیات مسیر این محصول را انتخاب کنید":
        "برای این محصول و مرکز، عملیاتی در مسیر ساخت پیدا نشد"
    });
    syncProductCapacityEditor();
  }
  function syncProductCapacityEditor(){
    const operationId=$("#center-cap-operation").value;
    const row=state.productCapacities.find(x=>x.routingOperationId===operationId);
    $("#center-cap-rate").value=row?String(row.capacityUnitsPerHour):"";
    $("#center-cap-active").checked=row?row.isActive:true;
    $("#center-cap-save").textContent=row?"به‌روزرسانی ظرفیت محصول‌محور":"ثبت ظرفیت محصول‌محور";
  }
  function displayProductCapacities(){
    table($("#center-product-capacity-list"),[
      ["محصول",x=>x.itemCode+" — "+x.itemName],
      ["مرکز کاری",x=>x.workCenterCode+" — "+x.workCenterName],
      ["نسخه مسیر","revisionCode"],
      ["عملیات",x=>x.operationSequence+": "+x.operationName],
      ["ظرفیت عبوری (واحد/ساعت)",x=>num(x.capacityUnitsPerHour)],
      ["زمان معادل (دقیقه/واحد)",x=>num(x.effectiveRunMinutesPerUnit)],
      ["وضعیت",x=>x.isActive?"فعال؛ استفاده در CRP/APS":"غیرفعال؛ رجوع به Routing"],
      ["آخرین تغییر",x=>stringDate(x.updatedUtc)]
    ],state.productCapacities,{
      label:x=>x.isActive?"غیرفعال‌سازی":"فعال‌سازی",
      run:async x=>{
        await api("/api/v1/work-center-product-capacities/"+x.id,"PUT",{
          routingOperationId:x.routingOperationId,
          capacityUnitsPerHour:x.capacityUnitsPerHour,isActive:!x.isActive
        });
        notice(x.isActive?"ظرفیت اختصاصی غیرفعال شد؛ محاسبات به زمان Routing برمی‌گردند.":
          "ظرفیت اختصاصی فعال شد و در محاسبات CRP و APS اعمال می‌شود.");
        await reload();
      }
    });
  }
  async function displayBom(){
    const parent=$("#bom-parent").value;const data=parent?await api(`/api/v1/bom/${parent}`):[];
    table($("#bom-list"),[["ترتیب","sequence"],["جزء",x=>nameBy(state.items,x.componentItemId)],["مصرف",x=>num(x.quantityPer)],["ضایعات %",x=>num(x.scrapPercent)]],data);
  }
  function displayRoutings(){
    fillSelect($("#print-routing-select"),state.routings);
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
    IPPCSChoices.fill($("#print-plan-select"),state.plans,{
      value:"id",label:x=>nameBy(state.items,x.itemId)+" · "+num(x.quantity)+" · "+stringDate(x.dueDateUtc)
    });
    table($("#plan-list"),[["محصول",x=>nameBy(state.items,x.itemId)],["مقدار",x=>num(x.quantity)],["موعد شمسی",x=>stringDate(x.dueDateUtc)]],state.plans,
      {label:()=>"محاسبه نیاز مواد",run:async row=>{
        const data=await api(`/api/v1/planning/gross-requirements/${row.id}`);
        $("#requirements-panel").hidden=false;
        table($("#requirements-list"),[["قطعه",x=>nameBy(state.items,x.itemId)],["نیاز ناخالص",x=>num(x.quantity)]],data.requirements);
        $("#requirements-panel").scrollIntoView({behavior:"smooth"});
      }});
  }
  async function reload(){
    const paths=["/api/v1/items","/api/v1/work-centers","/api/v1/routings",
      "/api/v1/work-shifts","/api/v1/production-plans",
      "/api/v1/work-center-product-capacities"];
    const keys=["items","centers","routings","shifts","plans","productCapacities"];
    const result=await Promise.all(paths.map(p=>api(p)));
    keys.forEach((k,i)=>{state[k]=result[i];});
    displayItems();displayCenters();displayRoutings();displayShifts();displayPlans();
    displayProductCapacities();updateProductCapacityOperations();await displayBom();
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
    window.addEventListener("ippps:excel-import-complete",()=>reload().then(()=>notice("ورود گروهی انجام شد و فهرست‌های فرم به‌روزرسانی شدند.")).catch(e=>notice(e.message,true)));
    for(const [button,select,kind] of [
      ["#print-routing-button","#print-routing-select","routing"],
      ["#print-plan-button","#print-plan-select","plan"]
    ]){
      $(button).addEventListener("click",()=>{
        const id=$(select).value;
        if(!id){notice("ابتدا برنامه یا نسخه مسیر را انتخاب کنید.",true);return;}
        window.open("/factory-print.html?"+new URLSearchParams({
          kind,id,back:"/app.html"
        }).toString(),"_blank","noopener");
      });
    }
    const grid=$("#module-grid");
    modules.forEach(([title,description,status,page],i)=>{
      const card=document.createElement("article");card.className="module-card";
      const h=document.createElement("b");h.textContent=`${String(i+1).padStart(2,"0")} · ${title}`;
      const p=document.createElement("span");p.textContent=description;
      const badge=document.createElement("span");badge.className="tag";badge.textContent=status;
      const link=document.createElement("a");link.href=page;
      link.className="factory-module-link";link.textContent="ورود به فرم آزمایشی";
      card.append(h,p,document.createElement("br"),badge,link);
      if([2,3,5,6,7,14,16,17].includes(i+1)){
        const trial=document.createElement("a");
        trial.href="/factory-analytical-verification.html#analytics-module-"+(i+1);
        trial.className="factory-module-link";
        trial.textContent="آزمون محاسبات و داده‌های واقعی";
        card.append(trial);
      }
      grid.append(card);
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
    $("#center-cap-item").addEventListener("change",updateProductCapacityOperations);
    $("#center-cap-center").addEventListener("change",updateProductCapacityOperations);
    $("#center-cap-operation").addEventListener("change",syncProductCapacityEditor);
    $("#bom-parent").onchange=()=>displayBom().catch(e=>notice(e.message,true));
    formHandler("#item-form", f=>api("/api/v1/items","POST",{
      code:fval(f,"code"),name:fval(f,"name"),unit:fval(f,"unit"),type:Number(fval(f,"type"))
    }));
    formHandler("#center-form",f=>api("/api/v1/work-centers","POST",{
      code:fval(f,"code"),name:fval(f,"name"),nominalUnitsPerHour:decimal(f,"rate")
    }));
    formHandler("#center-product-capacity-form",async f=>{
      const routingOperationId=String(fval(f,"routingOperationId")||"");
      if(!routingOperationId)throw new Error("ابتدا محصول، مرکز و عملیات معتبر را انتخاب کنید.");
      const payload={routingOperationId,capacityUnitsPerHour:decimal(f,"capacityUnitsPerHour"),
        isActive:$("#center-cap-active").checked};
      const existing=state.productCapacities.find(x=>x.routingOperationId===routingOperationId);
      if(existing)await api("/api/v1/work-center-product-capacities/"+existing.id,"PUT",payload);
      else await api("/api/v1/work-center-product-capacities","POST",payload);
    });
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
    $("#shift-capacity-review").onclick=async()=>{
      const center=$("#shift-capacity-center").value;
      const date=$("#shift-capacity-from").value.trim();
      const days=Number(IPPCSJalali.toLatin($("#shift-capacity-days").value));
      if(!center||!date||!Number.isInteger(days)||days<1||days>31){
        notice("مرکز، تاریخ شمسی و افق ۱ تا ۳۱ روز معتبر لازم است.",true);
        return;
      }
      try{
        const d=await api("/api/v1/work-shifts/capacity?workCenterId="+
          encodeURIComponent(center)+"&startJalali="+encodeURIComponent(date)+
          "&days="+days);
        table("#shift-capacity-table",[
          ["تاریخ شمسی","dateJalali"],
          ["روز",x=>dayNames[x.weekday]],
          ["ظرفیت مفید (دقیقه)",x=>num(x.effectiveMinutes)],
          ["شیفت‌های آغازشده در روز",x=>num(x.shiftCountStartingToday)],
          ["انتقال زمان از شیفت شب قبل",x=>x.hasPreviousNight?"دارد":"ندارد"]
        ],d.rows);
        notice(d.disclaimer);
      }catch(e){notice(e.message,true);}
    };
    formHandler("#plan-form",f=>api("/api/v1/production-plans/jalali","POST",{
      itemId:fval(f,"itemId"),quantity:decimal(f,"quantity"),dueDateJalali:fval(f,"due")
    }));
    loadItemUnitChoices().catch(e=>notice("فهرست واحدهای اندازه‌گیری بارگذاری نشد: "+e.message,true));
    reload().catch(e=>notice("دریافت داده‌ها امکان‌پذیر نیست. ابتدا دیتابیس توسعه و Migrationها را آماده کنید. "+e.message,true));
  }
  if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",start);else start();
})();
