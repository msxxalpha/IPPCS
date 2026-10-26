/* IPPCS reusable Jalali date picker.
   Adapted from the working in-house EmployeePerformance and Supply-Chain Payment picker.
   Browser Persian calendar + server-side System.Globalization.PersianCalendar validation.
   No external JS dependency. All date-only calculations happen at UTC noon. */
(() => {
  "use strict";
  const fmt = new Intl.DateTimeFormat("en-US-u-ca-persian-nu-latn", {
    timeZone: "UTC", year: "numeric", month: "numeric", day: "numeric"
  });
  const months = ["فروردین","اردیبهشت","خرداد","تیر","مرداد","شهریور","مهر","آبان","آذر","دی","بهمن","اسفند"];
  const weeks = ["ش","ی","د","س","چ","پ","ج"];
  const toLatin = text => String(text ?? "").replace(/[۰-۹٠-٩]/g, c => {
    const fa="۰۱۲۳۴۵۶۷۸۹", ar="٠١٢٣٤٥٦٧٨٩";
    return String(Math.max(fa.indexOf(c), ar.indexOf(c)));
  });
  const toFa = text => String(text).replace(/[0-9]/g, d => "۰۱۲۳۴۵۶۷۸۹"[Number(d)]);
  const utcNoon = (year, monthIndex, day) => new Date(Date.UTC(year, monthIndex, day, 12));
  function fromGregorian(date) {
    const bits = fmt.formatToParts(date);
    const number = type => Number(toLatin(bits.find(x => x.type === type)?.value));
    return { y: number("year"), m: number("month"), d: number("day") };
  }
  const startCache = new Map();
  function yearStart(year) {
    if (!Number.isInteger(year) || year < 1200 || year > 1600) throw new RangeError("سال شمسی خارج از محدوده است");
    if (!startCache.has(year)) {
      const gregorianYear = year + 621;
      let found;
      for (let day = 17; day <= 24; day++) {
        const d = utcNoon(gregorianYear, 2, day);
        const j = fromGregorian(d);
        if (j.y === year && j.m === 1 && j.d === 1) { found = d; break; }
      }
      if (!found) throw new RangeError("شروع سال شمسی یافت نشد");
      startCache.set(year, found);
    }
    return startCache.get(year);
  }
  function daysInMonth(y, m) {
    if (m < 1 || m > 12) throw new RangeError("ماه نامعتبر است");
    if (m <= 6) return 31;
    if (m <= 11) return 30;
    const length = Math.round((yearStart(y + 1) - yearStart(y)) / 86400000);
    return length - 336;
  }
  function toGregorian(y,m,d) {
    if (!Number.isInteger(y) || !Number.isInteger(m) || !Number.isInteger(d) ||
        d < 1 || d > daysInMonth(y,m)) throw new RangeError("تاریخ شمسی نامعتبر است");
    const offset = (m <= 6 ? (m-1)*31 : 186 + (m-7)*30) + d-1;
    const value = new Date(yearStart(y).getTime() + offset*86400000);
    const roundtrip = fromGregorian(value);
    if (roundtrip.y !== y || roundtrip.m !== m || roundtrip.d !== d)
      throw new RangeError("تاریخ شمسی معتبر نیست");
    return value;
  }
  function parse(text) {
    const match = /^([0-9]{4})\/([0-9]{1,2})\/([0-9]{1,2})$/.exec(toLatin(text).trim().replace(/-/g,"/"));
    if (!match) return null;
    const y=Number(match[1]),m=Number(match[2]),d=Number(match[3]);
    try { toGregorian(y,m,d); return {y,m,d}; } catch { return null; }
  }
  const format = ({y,m,d}) => `${y}/${String(m).padStart(2,"0")}/${String(d).padStart(2,"0")}`;
  function create(input) {
    if (input.dataset.jpInitialized) return;
    input.dataset.jpInitialized = "1";
    input.autocomplete = "off";
    input.inputMode = "numeric";
    input.placeholder = input.placeholder || "۱۴۰۵/۰۸/۰۴";
    const wrap = input.closest(".jalali-field") || input.parentElement;
    wrap.classList.add("jalali-field");
    const button = wrap.querySelector("[data-jalali-trigger]") || document.createElement("button");
    if (!button.parentElement) { button.type="button"; button.className="date-trigger"; button.textContent="▣"; wrap.append(button); }
    button.setAttribute("aria-label","باز کردن تقویم شمسی");
    button.type = "button";
    const picker = document.createElement("div");
    picker.className = "jalali-picker";
    picker.hidden = true;
    picker.setAttribute("role","dialog");
    picker.setAttribute("aria-label","انتخاب تاریخ شمسی");
    wrap.append(picker);
    let view = parse(input.value) || fromGregorian(new Date());
    function validInput() {
      const ok = !input.value.trim() || !!parse(input.value);
      input.setCustomValidity(ok ? "" : "تاریخ شمسی معتبر وارد کنید.");
      return ok;
    }
    function render() {
      const today = fromGregorian(new Date());
      picker.replaceChildren();
      const header = document.createElement("div");
      header.className = "jp-head";
      const prev = document.createElement("button"), next = document.createElement("button");
      prev.type=next.type="button";
      prev.textContent="‹"; next.textContent="›";
      prev.setAttribute("aria-label","ماه قبل"); next.setAttribute("aria-label","ماه بعد");
      const month = document.createElement("select");
      month.setAttribute("aria-label","ماه");
      months.forEach((m,i)=> {
        const opt=document.createElement("option"); opt.value=String(i+1);
        opt.textContent=m; month.append(opt);
      });
      month.value = String(view.m);
      const year = document.createElement("select");
      year.setAttribute("aria-label","سال");
      for(let y=Math.max(1200,view.y-25);y<=Math.min(1599,view.y+25);y++) {
        const opt=document.createElement("option"); opt.value=String(y);
        opt.textContent=toFa(y); year.append(opt);
      }
      year.value=String(view.y);
      header.append(prev,year,month,next);
      picker.append(header);
      function navigate(y,m) {
        if(m<1){y--;m=12;} if(m>12){y++;m=1;}
        if(y<1200||y>1599) return;
        view={y,m,d:Math.min(view.d,daysInMonth(y,m))};render();
      }
      prev.onclick=()=>navigate(view.y,view.m-1);
      next.onclick=()=>navigate(view.y,view.m+1);
      month.onchange=()=>navigate(view.y,Number(month.value));
      year.onchange=()=>navigate(Number(year.value),view.m);
      const weekRow=document.createElement("div");weekRow.className="jp-week";
      weeks.forEach(w=>{const cell=document.createElement("span");cell.textContent=w;weekRow.append(cell);});
      picker.append(weekRow);
      const grid=document.createElement("div");grid.className="jp-grid";
      const firstDay=toGregorian(view.y,view.m,1);
      const offset=(firstDay.getUTCDay()+1)%7;
      for(let i=0;i<offset;i++){const blank=document.createElement("span");blank.className="jp-blank";grid.append(blank);}
      for(let d=1;d<=daysInMonth(view.y,view.m);d++){
        const day=document.createElement("button");day.type="button";day.textContent=toFa(d);day.className="jp-cell";
        if(today.y===view.y&&today.m===view.m&&today.d===d)day.classList.add("today");
        const selected=parse(input.value);
        if(selected?.y===view.y&&selected?.m===view.m&&selected?.d===d)day.classList.add("selected");
        day.setAttribute("aria-label",`${d} ${months[view.m-1]} ${view.y}`);
        day.onclick=()=>{input.value=format({y:view.y,m:view.m,d});validInput();input.dispatchEvent(new Event("input",{bubbles:true}));input.dispatchEvent(new Event("change",{bubbles:true}));close();input.focus({preventScroll:true});};
        grid.append(day);
      }
      picker.append(grid);
      const footer=document.createElement("div");footer.className="jp-footer";
      const todayButton=document.createElement("button");todayButton.type="button";todayButton.textContent="امروز";
      todayButton.onclick=()=>{view=today;input.value=format(today);validInput();input.dispatchEvent(new Event("change",{bubbles:true}));close();};
      const clearButton=document.createElement("button");clearButton.type="button";clearButton.textContent="پاک‌کردن";
      clearButton.onclick=()=>{input.value="";validInput();input.dispatchEvent(new Event("change",{bubbles:true}));close();};
      footer.append(todayButton,clearButton);picker.append(footer);
    }
    function open(){document.querySelectorAll(".jalali-picker").forEach(p=>p.hidden=true);view=parse(input.value)||fromGregorian(new Date());render();picker.hidden=false;}
    function close(){picker.hidden=true;}
    button.addEventListener("click",e=>{e.preventDefault();picker.hidden?open():close();});
    input.addEventListener("focus",()=>{if(picker.hidden)open();});
    input.addEventListener("input",validInput);
    input.addEventListener("blur",validInput);
    document.addEventListener("pointerdown",e=>{if(!wrap.contains(e.target))close();});
    wrap.addEventListener("keydown",e=>{if(e.key==="Escape")close();});
    validInput();
  }
  function init(root=document){root.querySelectorAll("[data-jalali-date]").forEach(create);}
  window.IPPCSJalali = {init, parse, format, toGregorian, fromGregorian, daysInMonth, toLatin};
  if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",()=>init());else init();
})();
