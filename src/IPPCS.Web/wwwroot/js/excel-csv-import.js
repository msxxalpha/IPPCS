(() => {
  "use strict";
  const apiRoot = "/api/v1";
  const specs = [
    {
      form: "#bom-version-form", title: "ورود گروهی نسخه‌های BOM از فایل Excel/CSV",
      file: "ippcs-bom-revisions-template.csv",
      headers: ["parentCode","revision","validFromJalali","validUntilJalali"],
      optional: ["validUntilJalali"],
      sample: [["FG-001","A","1405/01/01",""]],
      help: "این فایل فقط نسخه‌های پیش‌نویس BOM را ایجاد می‌کند؛ اجزا باید جداگانه وارد و نسخه از مسیر کنترل مهندسی تصویب شود.",
      map: (r,ctx) => ({parentItemId:ctx.items.find(x=>x.code.toLowerCase()===r.parentCode.toLowerCase())?.id,revision:r.revision.trim(),validFromJalali:normalizeDate(r.validFromJalali),validUntilJalali:r.validUntilJalali?normalizeDate(r.validUntilJalali):null}),
      validate: (r,ctx) => {
        const parent=ctx.items.find(x=>x.code.toLowerCase()===r.parentCode.toLowerCase());
        if(!parent)return "کد کالای والد در اقلام پیدا نشد.";
        if(!r.revision||r.revision.trim().length>32||!validDate(r.validFromJalali))return "کد نسخه و تاریخ شروع معتبر لازم است.";
        if(r.validUntilJalali&&(!validDate(r.validUntilJalali)||compareJalaliDates(r.validUntilJalali,r.validFromJalali)<0))return "تاریخ پایان نامعتبر یا پیش از تاریخ شروع است.";
        const key=parent.id+"|"+r.revision.trim().toLowerCase();
        if(ctx.bomRevisions.some(x=>x.parentItemId===parent.id&&String(x.revision).toLowerCase()===r.revision.trim().toLowerCase())||ctx.seenBom.has("revision|"+key))return "کد نسخه برای این کالای والد تکراری است.";
        ctx.seenBom.add("revision|"+key);return "";
      },
      url: () => apiRoot+"/bom-revisions",
      refresh: true
    },
    {
      form: "#mps-form", title: "ورود گروهی سرنسخه‌های MPS از فایل Excel/CSV",
      file: "ippcs-mps-versions-template.csv",
      headers: ["mpsCode"],
      sample: [["MPS-1405-W32"]],
      help: "این فایل فقط سرنسخه‌های پیش‌نویس برنامه را می‌سازد؛ ردیف‌های تقاضا، کنترل و تصویب برنامه جداگانه انجام می‌شود.",
      map: r => ({code:r.mpsCode.trim()}),
      validate: (r,ctx) => {
        const code=String(r.mpsCode||"").trim();
        if(!code||code.length>64)return "کد نسخه MPS معتبر لازم است.";
        if(ctx.mps.some(x=>String(x.code).toLowerCase()===code.toLowerCase())||ctx.seenMps.has("version|"+code.toLowerCase()))return "کد نسخه MPS تکراری است.";
        ctx.seenMps.add("version|"+code.toLowerCase());return "";
      },
      url: () => apiRoot+"/mps",
      refresh: true
    },
    {
      form: "#item-form", title: "ورود گروهی اقلام از فایل Excel/CSV",
      file: "ippcs-items-template.csv",
      headers: ["code","name","unit","type"],
      sample: [["ITEM-001","شرح کالا","PCS","0"]],
      help: "طبقه کالا: 0=مواد اولیه، 1=نیمه‌ساخته، 2=محصول نهایی، 3=مواد مصرفی. واحد باید از اطلاعات پایه فعال سامانه باشد.",
      map: r => ({code:r.code, name:r.name, unit:r.unit, type:number(r.type)}),
      validate: (r, ctx) => {
        if (!r.code || r.code.length > 64 || !r.name || r.name.length > 256 || !r.unit || ![0,1,2,3].includes(number(r.type)))
          return "کد، شرح، واحد و طبقه معتبر لازم است.";
        if (ctx.items.some(x => x.code.toLowerCase() === r.code.toLowerCase())) return "کد کالا از قبل وجود دارد؛ ردیف تکراری وارد نشد.";
        return "";
      },
      url: () => apiRoot + "/items",
      refresh: true
    },
    {
      form: "#center-form", title: "ورود گروهی مراکز کاری از فایل Excel/CSV",
      file: "ippcs-work-centers-template.csv",
      headers: ["code","name","nominalUnitsPerHour"],
      sample: [["WC-001","نام مرکز کاری","100"]],
      help: "ظرفیت اسمی باید عددی مثبت و حداکثر یک میلیون واحد در ساعت باشد.",
      map: r => ({code:r.code, name:r.name, nominalUnitsPerHour:number(r.nominalUnitsPerHour)}),
      validate: (r, ctx) => {
        const n=number(r.nominalUnitsPerHour);
        if (!r.code || r.code.length>64 || !r.name || r.name.length>256 || !Number.isFinite(n) || n<=0 || n>1000000) return "کد، نام و ظرفیت مثبت معتبر لازم است.";
        if (ctx.centers.some(x => x.code.toLowerCase() === r.code.toLowerCase())) return "کد مرکز کاری از قبل وجود دارد یا در ردیف‌های قبلی فایل ثبت شده است.";
        return "";
      },
      url: () => apiRoot + "/work-centers",
      refresh: true
    },
    {
      form: "#bom-form", title: "ورود گروهی اجزای BOM از فایل Excel/CSV",
      file: "ippcs-bom-template.csv",
      headers: ["parentCode","componentCode","quantityPer","scrapPercent","sequence"],
      sample: [["FG-001","RM-001","2.5","1.5","10"]],
      help: "کد والد و جزء باید قبلاً در اقلام ثبت شده باشند. این فرم، BOM ساده موجود را تکمیل می‌کند؛ برای BOM نسخه‌دار از فرم نسخه‌بندی BOM استفاده کنید.",
      map: (r,ctx) => ({parentItemId:ctx.items.find(x=>x.code.toLowerCase()===r.parentCode.toLowerCase())?.id, componentItemId:ctx.items.find(x=>x.code.toLowerCase()===r.componentCode.toLowerCase())?.id, quantityPer:number(r.quantityPer), scrapPercent:number(r.scrapPercent), sequence:number(r.sequence)}),
      validate: (r,ctx) => {
        const p=ctx.items.find(x=>x.code.toLowerCase()===r.parentCode.toLowerCase()), c=ctx.items.find(x=>x.code.toLowerCase()===r.componentCode.toLowerCase());
        if (!p || !c) return "کد والد یا جزء در اقلام پیدا نشد.";
        if (p.id===c.id) return "والد و جزء نمی‌توانند یک کالا باشند.";
        const q=number(r.quantityPer), s=number(r.scrapPercent), seq=number(r.sequence);
        if (!Number.isFinite(q)||q<=0||!Number.isFinite(s)||s<0||s>=100||!Number.isInteger(seq)||seq<0) return "مصرف باید مثبت، ضایعات بین ۰ تا کمتر از ۱۰۰ و ترتیب عدد صحیح نامنفی باشد.";
        if (ctx.bom.some(x=>x.parentItemId===p.id&&x.componentItemId===c.id) || ctx.seenBom.has(p.id+"|"+c.id)) return "این ترکیب والد/جزء تکراری است.";
        ctx.seenBom.add(p.id+"|"+c.id);
        return "";
      },
      url: () => apiRoot + "/bom",
      refresh: true
    },
    {
      form: "#plan-form", title: "ورود گروهی برنامه‌های تولید از فایل Excel/CSV",
      file: "ippcs-production-plans-template.csv",
      headers: ["itemCode","quantity","dueDateJalali"],
      sample: [["FG-001","250","1405/09/15"]],
      help: "کد محصول باید در اقلام موجود باشد؛ تاریخ شمسی را به شکل YYYY/MM/DD وارد کنید. این برنامه مقدماتی است و جایگزین MPS مصوب نیست.",
      map: (r,ctx) => ({itemId:ctx.items.find(x=>x.code.toLowerCase()===r.itemCode.toLowerCase())?.id, quantity:number(r.quantity), dueDateJalali:normalizeDate(r.dueDateJalali)}),
      validate: (r,ctx) => {
        if (!ctx.items.some(x=>x.code.toLowerCase()===r.itemCode.toLowerCase())) return "کد محصول در اقلام پیدا نشد.";
        if (!Number.isFinite(number(r.quantity))||number(r.quantity)<=0) return "مقدار باید عددی مثبت باشد.";
        if (!/^\d{4}[\/\-]\d{1,2}[\/\-]\d{1,2}$/.test(normalizeDate(r.dueDateJalali))) return "تاریخ شمسی باید به شکل YYYY/MM/DD باشد.";
        return "";
      },
      url: () => apiRoot + "/production-plans/jalali",
      refresh: true
    }
    ,
    {
      form: "#routing-form", title: "ورود گروهی نسخه‌های مسیر ساخت از فایل Excel/CSV",
      file: "ippcs-routing-revisions-template.csv",
      headers: ["itemCode","revisionCode","effectiveFromJalali","effectiveToJalali"],
      optional: ["effectiveToJalali"],
      sample: [["FG-001","A","1405/01/01",""]],
      help: "کالا باید از قبل تعریف شده باشد. تاریخ پایان اختیاری است. نسخه‌ها ابتدا به صورت پیش‌نویس ثبت می‌شوند و برای تصویب باید کنترل‌های مهندسی را بگذرانند.",
      map: (r,ctx) => ({itemId:ctx.items.find(x=>x.code.toLowerCase()===r.itemCode.toLowerCase())?.id, revisionCode:r.revisionCode, effectiveFromJalali:normalizeDate(r.effectiveFromJalali), effectiveToJalali:r.effectiveToJalali?normalizeDate(r.effectiveToJalali):null}),
      validate: (r,ctx) => {
        if (!ctx.items.some(x=>x.code.toLowerCase()===r.itemCode.toLowerCase())) return "کد محصول در اقلام پیدا نشد.";
        if (!r.revisionCode || r.revisionCode.length>32 || !validDate(r.effectiveFromJalali)) return "کد نسخه و تاریخ شروع شمسی معتبر لازم است.";
        if (r.effectiveToJalali && !validDate(r.effectiveToJalali)) return "تاریخ پایان باید شمسی و به شکل YYYY/MM/DD باشد.";
        return "";
      },
      url: () => apiRoot + "/routings",
      refresh: true
    },
    {
      form: "#operation-form", title: "ورود گروهی عملیات مسیر ساخت از فایل Excel/CSV",
      file: "ippcs-routing-operations-template.csv",
      headers: ["itemCode","revisionCode","centerCode","sequence","name","setupMinutes","runMinutesPerUnit","yieldPercent"],
      sample: [["FG-001","A","WC-001","10","عملیات نمونه","5","0.75","98"]],
      help: "محصول و نسخه مسیر باید به نسخه پیش‌نویس موجود اشاره کنند؛ مرکز کاری باید از قبل ثبت شده باشد. عملیات به نسخه مصوب اضافه نمی‌شود.",
      map: (r,ctx) => {
        const route=ctx.routings.find(x=>ctx.items.find(i=>i.id===x.itemId)?.code.toLowerCase()===r.itemCode.toLowerCase()&&String(x.revisionCode).toLowerCase()===r.revisionCode.toLowerCase());
        const center=ctx.centers.find(x=>x.code.toLowerCase()===r.centerCode.toLowerCase());
        return {routeId:route?.id, payload:{sequence:number(r.sequence),name:r.name,workCenterId:center?.id,setupMinutes:number(r.setupMinutes),runMinutesPerUnit:number(r.runMinutesPerUnit),yieldPercent:number(r.yieldPercent)}};
      },
      validate: (r,ctx) => {
        const route=ctx.routings.find(x=>ctx.items.find(i=>i.id===x.itemId)?.code.toLowerCase()===r.itemCode.toLowerCase()&&String(x.revisionCode).toLowerCase()===r.revisionCode.toLowerCase());
        if (!route) return "نسخه مسیر برای این محصول پیدا نشد.";
        if (route.state!==0) return "فقط به نسخه مسیر پیش‌نویس می‌توان عملیات افزود.";
        if (!ctx.centers.some(x=>x.code.toLowerCase()===r.centerCode.toLowerCase())) return "کد مرکز کاری پیدا نشد.";
        if (!Number.isInteger(number(r.sequence))||number(r.sequence)<1||!r.name||r.name.length>180||!Number.isFinite(number(r.setupMinutes))||number(r.setupMinutes)<0||!Number.isFinite(number(r.runMinutesPerUnit))||number(r.runMinutesPerUnit)<=0||!Number.isFinite(number(r.yieldPercent))||number(r.yieldPercent)<=0||number(r.yieldPercent)>100) return "ترتیب، نام، زمان آماده‌سازی، زمان واحد و بازده معتبر لازم است.";
        return "";
      },
      url: (r,ctx) => apiRoot + "/routings/" + ctx.routings.find(x=>ctx.items.find(i=>i.id===x.itemId)?.code.toLowerCase()===r.itemCode.toLowerCase()&&String(x.revisionCode).toLowerCase()===r.revisionCode.toLowerCase())?.id + "/operations",
      map: (r,ctx) => {
        const route=ctx.routings.find(x=>ctx.items.find(i=>i.id===x.itemId)?.code.toLowerCase()===r.itemCode.toLowerCase()&&String(x.revisionCode).toLowerCase()===r.revisionCode.toLowerCase());
        const center=ctx.centers.find(x=>x.code.toLowerCase()===r.centerCode.toLowerCase());
        return {sequence:number(r.sequence),name:r.name,workCenterId:center?.id,setupMinutes:number(r.setupMinutes),runMinutesPerUnit:number(r.runMinutesPerUnit),yieldPercent:number(r.yieldPercent)};
      },
      refresh: true
    },
    {
      form: "#shift-form", title: "ورود گروهی تقویم شیفت مراکز کاری از فایل Excel/CSV",
      file: "ippcs-work-shifts-template.csv",
      headers: ["centerCode","dayOfWeek","startTime","endTime","effectiveCapacityPercent"],
      sample: [["WC-001","6","07:00","15:00","85"]],
      help: "روز هفته: 0=یکشنبه تا 6=شنبه. شیفت شب با پایان زودتر از شروع پشتیبانی می‌شود؛ همپوشانی‌ها توسط سرور رد می‌شوند.",
      map: (r,ctx) => ({workCenterId:ctx.centers.find(x=>x.code.toLowerCase()===r.centerCode.toLowerCase())?.id,dayOfWeek:number(r.dayOfWeek),startTime:r.startTime,endTime:r.endTime,effectiveCapacityPercent:number(r.effectiveCapacityPercent)}),
      validate: (r,ctx) => {
        if (!ctx.centers.some(x=>x.code.toLowerCase()===r.centerCode.toLowerCase())) return "کد مرکز کاری پیدا نشد.";
        if (!Number.isInteger(number(r.dayOfWeek))||number(r.dayOfWeek)<0||number(r.dayOfWeek)>6) return "روز هفته باید عدد صحیح ۰ تا ۶ باشد.";
        if (!/^([01]\\d|2[0-3]):[0-5]\\d$/.test(r.startTime)||!/^([01]\\d|2[0-3]):[0-5]\\d$/.test(r.endTime)||r.startTime===r.endTime) return "ساعت شروع و پایان باید معتبر و متفاوت باشند.";
        if (!Number.isFinite(number(r.effectiveCapacityPercent))||number(r.effectiveCapacityPercent)<=0||number(r.effectiveCapacityPercent)>100) return "ضریب ظرفیت باید بیشتر از صفر و حداکثر ۱۰۰ باشد.";
        return "";
      },
      url: () => apiRoot + "/work-shifts",
      refresh: true
    }
    ,
    {
      form: "#bom-line-form", title: "ورود گروهی اجزای BOM نسخه‌دار",
      file: "ippcs-bom-revision-lines-template.csv",
      headers: ["parentCode","revision","componentCode","sequence","quantityPer","scrapPercent"],
      sample: [["FG-001","A","RM-001","10","2.5","1.5"]],
      help: "نسخه BOM باید از قبل ایجاد شده و پیش‌نویس باشد. این ورود، BOM ساده را تغییر نمی‌دهد و به نسخه مصوب دست نمی‌زند.",
      map: (r,ctx) => {
        const parent=ctx.items.find(x=>x.code.toLowerCase()===r.parentCode.toLowerCase());
        const revision=ctx.bomRevisions.find(x=>x.parentItemId===parent?.id&&String(x.revision).toLowerCase()===r.revision.toLowerCase());
        const component=ctx.items.find(x=>x.code.toLowerCase()===r.componentCode.toLowerCase());
        return {componentItemId:component?.id,sequence:number(r.sequence),quantityPer:number(r.quantityPer),scrapPercent:number(r.scrapPercent),revisionId:revision?.id};
      },
      validate: (r,ctx) => {
        const parent=ctx.items.find(x=>x.code.toLowerCase()===r.parentCode.toLowerCase());
        const rev=ctx.bomRevisions.find(x=>x.parentItemId===parent?.id&&String(x.revision).toLowerCase()===r.revision.toLowerCase());
        const component=ctx.items.find(x=>x.code.toLowerCase()===r.componentCode.toLowerCase());
        if(!rev)return "نسخه BOM برای کالای والد پیدا نشد.";
        if(rev.state!==0)return "فقط به نسخه BOM پیش‌نویس می‌توان جزء افزود.";
        if(!component)return "کد جزء در اقلام پیدا نشد.";
        if(component.id===parent?.id)return "والد و جزء نمی‌توانند یک کالا باشند.";
        if(!Number.isInteger(number(r.sequence))||number(r.sequence)<=0||!Number.isFinite(number(r.quantityPer))||number(r.quantityPer)<=0||!Number.isFinite(number(r.scrapPercent))||number(r.scrapPercent)<0||number(r.scrapPercent)>=100)return "ترتیب، مصرف و درصد ضایعات معتبر لازم است.";
        if(ctx.bomRevisions.find(x=>x.id===rev.id)?.lines?.some(x=>x.componentItemId===component.id||x.sequence===number(r.sequence)))return "جزء یا شماره ترتیب در نسخه از قبل وجود دارد.";
        const componentKey=rev.id+"|component|"+component.id, sequenceKey=rev.id+"|sequence|"+number(r.sequence);
        if(ctx.seenBom.has(componentKey)||ctx.seenBom.has(sequenceKey))return "جزء یا شماره ترتیب در همین فایل تکراری است.";
        ctx.seenBom.add(componentKey);ctx.seenBom.add(sequenceKey);return "";
      },
      url: (r,ctx) => apiRoot+"/bom-revisions/"+ctx.bomRevisions.find(x=>x.parentItemId===ctx.items.find(i=>i.code.toLowerCase()===r.parentCode.toLowerCase())?.id&&String(x.revision).toLowerCase()===r.revision.toLowerCase())?.id+"/lines",
      refresh: true
    },
    {
      form: "#mps-line-form", title: "ورود گروهی ردیف‌های برنامه اصلی تولید (MPS)",
      file: "ippcs-mps-lines-template.csv",
      headers: ["mpsCode","itemCode","quantity","neededOnJalali","reference"],
      optional: ["reference"],
      sample: [["MPS-2026-11","FG-001","250","1405/09/15","سفارش داخلی"]],
      help: "نسخه MPS باید از قبل ساخته شده و در وضعیت پیش‌نویس باشد. فقط اقلام طبقه محصول نهایی قابل ثبت‌اند؛ پس از ورود، کنترل و تصویب MPS را جداگانه انجام دهید.",
      map: (r,ctx) => ({itemId:ctx.items.find(x=>x.code.toLowerCase()===r.itemCode.toLowerCase())?.id,quantity:number(r.quantity),neededOnJalali:normalizeDate(r.neededOnJalali),reference:r.reference||""}),
      validate: (r,ctx) => {
        const version=ctx.mps.find(x=>String(x.code).toLowerCase()===r.mpsCode.toLowerCase());
        if(!version)return "کد نسخه MPS پیدا نشد.";
        if(version.state!==0)return "فقط به نسخه MPS پیش‌نویس می‌توان ردیف افزود.";
        const item=ctx.items.find(x=>x.code.toLowerCase()===r.itemCode.toLowerCase());
        if(!item)return "کد محصول در اقلام پیدا نشد.";
        if(item.type!==2)return "فقط محصول نهایی قابل ثبت در MPS است.";
        if(!Number.isFinite(number(r.quantity))||number(r.quantity)<=0||!validDate(r.neededOnJalali)||String(r.reference||"").length>120)return "مقدار، تاریخ یا مرجع نامعتبر است.";
        const needed=normalizeDate(r.neededOnJalali), key=version.id+"|"+item.id+"|"+needed;
        if(version.lines?.some(x=>x.itemId===item.id&&normalizeDate(x.neededOnJalali)===needed)||ctx.seenMps.has(key))return "برای این محصول و تاریخ، ردیف MPS تکراری است.";
        ctx.seenMps.add(key);
        return "";
      },
      url: (r,ctx) => apiRoot+"/mps/"+ctx.mps.find(x=>String(x.code).toLowerCase()===r.mpsCode.toLowerCase())?.id+"/lines",
      refresh: true
    },
    {
      form: "#balance-form", title: "ورود گروهی فرضیات موجودی و تأمین",
      file: "ippcs-planning-balances-template.csv",
      headers: ["itemCode","snapshotDateJalali","physicalQuantity","reservedQuantity","quarantinedQuantity","safetyQuantity","leadDays","minimumBatch","batchMultiple"],
      sample: [["RM-001","1405/09/01","1000","100","0","50","7","1","1"]],
      help: "این داده‌ها صرفاً فرضیات برنامه‌ریزی داخلی IPPCS هستند و موجودی واقعی انبار یا سند عملیاتی ایجاد نمی‌کنند. برای هر قلم فقط آخرین ردیف معتبر فایل را ثبت کنید.",
      map: (r,ctx) => ({itemId:ctx.items.find(x=>x.code.toLowerCase()===r.itemCode.toLowerCase())?.id,snapshotDateJalali:normalizeDate(r.snapshotDateJalali),physicalQuantity:number(r.physicalQuantity),reservedQuantity:number(r.reservedQuantity),quarantinedQuantity:number(r.quarantinedQuantity),safetyQuantity:number(r.safetyQuantity),leadDays:number(r.leadDays),minimumBatch:number(r.minimumBatch),batchMultiple:number(r.batchMultiple)}),
      validate: (r,ctx) => {
        if(!ctx.items.some(x=>x.code.toLowerCase()===r.itemCode.toLowerCase()))return "کد قلم پیدا نشد.";
        const nums=["physicalQuantity","reservedQuantity","quarantinedQuantity","safetyQuantity"].map(k=>number(r[k]));
        if(!validDate(r.snapshotDateJalali)||nums.some(x=>!Number.isFinite(x)||x<0)||number(r.leadDays)<0||!Number.isInteger(number(r.leadDays))||number(r.leadDays)>3650||!Number.isFinite(number(r.minimumBatch))||number(r.minimumBatch)<=0||!Number.isFinite(number(r.batchMultiple))||number(r.batchMultiple)<=0)return "تاریخ، موجودی‌ها، زمان تأمین یا سیاست دسته‌بندی نامعتبر است.";
        if(number(r.reservedQuantity)+number(r.quarantinedQuantity)>number(r.physicalQuantity))return "رزرو و قرنطینه نمی‌تواند از موجودی فیزیکی بیشتر باشد.";
        const item=ctx.items.find(x=>x.code.toLowerCase()===r.itemCode.toLowerCase());
        if(ctx.seenBalance.has(item.id))return "برای این قلم در همین فایل بیش از یک ردیف وجود دارد.";
        ctx.seenBalance.add(item.id);return "";
      },
      url: (r,ctx) => apiRoot+"/planning-balances/"+ctx.items.find(x=>x.code.toLowerCase()===r.itemCode.toLowerCase())?.id,
      method: "PUT",
      refresh: true
    },
    {
      form: "#receipt-form", title: "ورود گروهی دریافت‌های برنامه‌ای",
      file: "ippcs-planning-receipts-template.csv",
      headers: ["itemCode","availableOnJalali","quantity","reference"],
      optional: ["reference"],
      sample: [["RM-001","1405/09/10","500","تأمین برنامه‌ای"]],
      help: "این دریافت‌ها فقط ورودی فرضی MRP هستند و سفارش خرید، رسید انبار یا تراکنش واقعی ایجاد نمی‌کنند.",
      map: (r,ctx) => ({itemId:ctx.items.find(x=>x.code.toLowerCase()===r.itemCode.toLowerCase())?.id,availableOnJalali:normalizeDate(r.availableOnJalali),quantity:number(r.quantity),reference:r.reference||""}),
      validate: (r,ctx) => {
        if(!ctx.items.some(x=>x.code.toLowerCase()===r.itemCode.toLowerCase()))return "کد قلم پیدا نشد.";
        if(!validDate(r.availableOnJalali)||!Number.isFinite(number(r.quantity))||number(r.quantity)<=0||String(r.reference||"").length>120)return "تاریخ، مقدار یا مرجع دریافت معتبر نیست.";
        return "";
      },
      url: () => apiRoot+"/planning-receipts",
      refresh: true
    }
  ];

  function number(v) {
    const s=String(v??"").replace(/[۰-۹]/g,d=>"۰۱۲۳۴۵۶۷۸۹".indexOf(d)).replace(/[٠-٩]/g,d=>"٠١٢٣٤٥٦٧٨٩".indexOf(d)).replace(/٬/g,"").replace(/,/g,"").trim();
    return s===""?NaN:Number(s);
  }
  function normalizeDate(v) { return String(v??"").trim().replace(/-/g,"/").replace(/[۰-۹]/g,d=>"۰۱۲۳۴۵۶۷۸۹".indexOf(d)).replace(/[٠-٩]/g,d=>"٠١٢٣٤٥٦٧٨٩".indexOf(d)); }
  function parseCsv(text) {
    text=text.replace(/^\uFEFF/,"");
    const first=(text.split(/\r?\n/,1)[0]||"");
    const delimiter=(first.split(";").length>first.split(",").length)?";":",";
    const rows=[];let row=[],cell="",quoted=false;
    for(let i=0;i<text.length;i++){
      const ch=text[i];
      if(ch==='"'&&quoted&&text[i+1]==='"'){cell+='"';i++;}
      else if(ch==='"') quoted=!quoted;
      else if(ch===delimiter&&!quoted){row.push(cell.trim());cell="";}
      else if((ch==="\n"||ch==="\r")&&!quoted){if(ch==="\r"&&text[i+1]==="\n")i++;row.push(cell.trim());if(row.some(x=>x!==""))rows.push(row);row=[];cell="";}
      else cell+=ch;
    }
    if(cell!==""||row.length){row.push(cell.trim());if(row.some(x=>x!==""))rows.push(row);}
    if(quoted) throw new Error("فایل CSV دارای علامت نقل‌قول بسته‌نشده است.");
    if(rows.length<2) throw new Error("فایل باید شامل سطر عنوان و دست‌کم یک ردیف داده باشد.");
    const headers=rows.shift().map(x=>x.replace(/^\uFEFF/,"").trim());
    if(new Set(headers).size!==headers.length) throw new Error("عنوان ستون تکراری است.");
    return rows.map((values,i)=>({__line:i+2,...Object.fromEntries(headers.map((h,j)=>[h,values[j]??""]))}));
  }
  async function secureApi(url, payload, method="POST") {
    const response=await window.IPPCSSecurity.secureFetch(url,{method,headers:{"Content-Type":"application/json"},body:JSON.stringify(payload)});
    if(response.ok) return;
    let message="خطای سرور";
    try {const raw=await response.text();if(raw){try{const data=JSON.parse(raw);message=data.message||data.title||data.detail||raw;}catch{message=raw;}}} catch {}
    throw new Error("HTTP "+response.status+" — "+message);
  }
  async function context(spec, rows) {
    const needsRouting=spec.form==="#routing-form"||spec.form==="#operation-form";
    const needsBom=spec.form==="#bom-form";
    const needsMps=spec.form==="#mps-line-form"||spec.form==="#mps-form";
    const needsBomRevision=spec.form==="#bom-line-form"||spec.form==="#bom-version-form";
    const needsBalance=spec.form==="#balance-form";
    const [items,centers,bom,routings,bomRevisions,mps,balances]=await Promise.all([
      window.IPPCSSecurity.secureFetch(apiRoot+"/items").then(r=>{if(!r.ok)throw Error("دریافت اقلام ناموفق بود");return r.json();}),
      window.IPPCSSecurity.secureFetch(apiRoot+"/work-centers").then(r=>{if(!r.ok)throw Error("دریافت مراکز کاری ناموفق بود");return r.json();}),
      needsBom ? window.IPPCSSecurity.secureFetch(apiRoot+"/items").then(r=>{if(!r.ok)throw Error("دریافت اقلام ناموفق بود");return r.json();}).then(async catalog=>{
        const codes=new Set(rows.map(x=>String(x.parentCode||"").toLowerCase()));
        const parents=catalog.filter(item=>codes.has(String(item.code).toLowerCase()));
        const all=[];
        for(const item of parents){const response=await window.IPPCSSecurity.secureFetch(apiRoot+"/bom/"+item.id);if(!response.ok)throw Error("دریافت BOM کالای "+item.code+" ناموفق بود");all.push(...await response.json());}
        return all;
      }) : Promise.resolve([]),
      needsRouting ? window.IPPCSSecurity.secureFetch(apiRoot+"/routings").then(r=>{if(!r.ok)throw Error("دریافت مسیرهای ساخت ناموفق بود");return r.json();}) : Promise.resolve([]),
      needsBomRevision ? window.IPPCSSecurity.secureFetch(apiRoot+"/bom-revisions").then(r=>{if(!r.ok)throw Error("دریافت نسخه‌های BOM ناموفق بود");return r.json();}) : Promise.resolve([]),
      needsMps ? window.IPPCSSecurity.secureFetch(apiRoot+"/mps").then(r=>{if(!r.ok)throw Error("دریافت نسخه‌های MPS ناموفق بود");return r.json();}) : Promise.resolve([]),
      needsBalance ? window.IPPCSSecurity.secureFetch(apiRoot+"/planning-balances").then(r=>{if(!r.ok)throw Error("دریافت فرضیات موجودی ناموفق بود");return r.json();}) : Promise.resolve([])
    ]);
    return {items,centers,bom,routings,bomRevisions,mps,balances,seenBom:new Set(),seenBalance:new Set(),seenMps:new Set()};
  }
  function compareJalaliDates(a,b) {
    const parts=v=>normalizeDate(v).split("/").map(Number);
    const x=parts(a),y=parts(b);
    for(let i=0;i<3;i++){if(x[i]!==y[i])return x[i]-y[i];}
    return 0;
  }\n  function validDate(v) {
    const parts=normalizeDate(v).split("/");
    return parts.length===3 && parts[0].length===4 && parts.every(p=>p.length>0&&Array.from(p).every(ch=>ch>="0"&&ch<="9")) &&
      Number(parts[1])>=1&&Number(parts[1])<=12&&Number(parts[2])>=1&&Number(parts[2])<=31;
  }
  function downloadTemplate(spec) {
    const csv=[spec.headers,...spec.sample].map(row=>row.map(v=>{const s=String(v);return /[",\r\n;]/.test(s)?'"'+s.replace(/"/g,'""')+'"':s;}).join(",")).join("\r\n");
    const blob=new Blob(["\uFEFF",csv,"\r\n"],{type:"text/csv;charset=utf-8"});
    const url=URL.createObjectURL(blob),a=document.createElement("a");a.href=url;a.download=spec.file;a.click();URL.revokeObjectURL(url);
  }
  function attach(spec) {
    const form=document.querySelector(spec.form);if(!form)return;
    const panel=document.createElement("section");panel.className="panel excel-import";
    const h=document.createElement("h3");h.textContent=spec.title;
    const p=document.createElement("p");p.className="hint";p.textContent=spec.help+" فایل نمونه را دریافت کنید، در Excel تکمیل کنید و با نوع CSV UTF-8 ذخیره نمایید.";
    const row=document.createElement("div");row.className="form-actions";
    const template=document.createElement("button");template.type="button";template.className="btn-outline";template.textContent="دریافت قالب CSV";
    template.addEventListener("click",()=>downloadTemplate(spec));
    const input=document.createElement("input");input.type="file";input.accept=".csv,text/csv";input.setAttribute("aria-label","انتخاب فایل CSV");input.style.maxWidth="320px";
    const run=document.createElement("button");run.type="button";run.className="btn-main";run.textContent="بررسی و ورود گروهی";run.disabled=true;
    const status=document.createElement("div");status.className="notice";status.hidden=true;status.setAttribute("aria-live","polite");
    const results=document.createElement("div");
    row.append(template,input,run);panel.append(h,p,row,status,results);form.insertAdjacentElement("afterend",panel);
    let selectedFile=null;
    input.addEventListener("change",()=>{selectedFile=input.files?.[0]||null;run.disabled=!selectedFile;status.hidden=true;results.replaceChildren();});
    run.addEventListener("click",async()=>{
      if(!selectedFile)return;
      run.disabled=true;template.disabled=true;input.disabled=true;results.replaceChildren();status.hidden=false;status.className="notice";status.textContent="در حال بررسی فایل و داده‌های مرجع…";
      try{
        const text=await selectedFile.text();
        const rows=parseCsv(text);
        const actual=Object.keys(rows[0]).filter(k=>k!=="__line");
        const missing=spec.headers.filter(h=>!actual.includes(h));
        if(missing.length)throw new Error("ستون‌های الزامی پیدا نشد: "+missing.join(", "));
        const ctx=await context(spec,rows),seenCodes=new Set();
        let ok=0,failed=0;
        const table=document.createElement("div");table.className="table-wrap";
        const out=document.createElement("table");
        const head=document.createElement("tr");
        ["ردیف فایل","شناسه/کد","نتیجه"].forEach(s=>{const th=document.createElement("th");th.textContent=s;head.append(th);});
        const thead=document.createElement("thead");thead.append(head);out.append(thead);
        const body=document.createElement("tbody");out.append(body);table.append(out);results.append(table);
        for(const r of rows){
          const tr=document.createElement("tr"),line=document.createElement("td"),identity=document.createElement("td"),result=document.createElement("td");
          line.textContent=String(r.__line);
          identity.textContent=r.code||r.mpsCode||r.revision||(r.parentCode&&r.componentCode?r.parentCode+" → "+r.componentCode:null)||(r.itemCode&&r.revisionCode?r.itemCode+" / "+r.revisionCode:null)||r.itemCode||r.centerCode||"—";
          let error="";
          for(const key of spec.headers) if(!spec.optional?.includes(key)&&!String(r[key]??"").trim()){error="ستون "+key+" خالی است.";break;}
          if(!error&&spec.form==="#item-form"){
            const key=r.code.toLowerCase();if(seenCodes.has(key))error="کد در خود فایل تکراری است.";else seenCodes.add(key);
          }
          if(!error&&spec.form==="#center-form"){
            const key=r.code.toLowerCase();if(seenCodes.has(key))error="کد در خود فایل تکراری است.";else seenCodes.add(key);
          }
          if(!error)error=spec.validate(r,ctx);
          if(!error){
            try{await secureApi(spec.url(r,ctx),spec.map(r,ctx),spec.method);ok++;result.textContent="ثبت شد";}
            catch(e){failed++;result.textContent=e.message;}
          } else {failed++;result.textContent=error;}
          if(result.textContent!=="ثبت شد")tr.className="import-row-error";
          tr.append(line,identity,result);body.append(tr);
          status.textContent="پیشرفت: "+(ok+failed)+" از "+rows.length+" ردیف؛ موفق "+ok+"، ناموفق "+failed;
        }
        status.className="notice"+(failed?" error":"");
        status.textContent="پایان ورود گروهی — موفق: "+ok+"، ناموفق: "+failed+". ردیف‌های ناموفق در جدول مشخص شده‌اند.";
        if(ok&&spec.refresh)window.dispatchEvent(new CustomEvent("ippps:excel-import-complete"));
      }catch(e){status.className="notice error";status.textContent=e.message||"خواندن فایل ناموفق بود.";}
      finally{run.disabled=false;template.disabled=false;input.disabled=false;}
    });
  }
  function start(){specs.forEach(attach);}
  if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",start,{once:true});else start();
})();