const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");
const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const html=fs.readFileSync(path.join(root,"app.html"),"utf8");
const css=fs.readFileSync(path.join(root,"css/production.css"),"utf8");
const nav=html.slice(html.indexOf('<nav id="primary-nav"'),html.indexOf("</nav>",html.indexOf('<nav id="primary-nav"'))+6);

test("primary navigation follows master data, engineering, planning, execution, test and reporting order",()=>{
 const titles=[...nav.matchAll(/class="nav-group-title">([^<]+)<\/div>/g)].map(x=>x[1]);
 assert.deepEqual(titles,[
  "۱. داده‌های پایه کارخانه",
  "۲. مهندسی محصول و ساخت",
  "۳. برنامه‌ریزی تولید",
  "۴. اجرای تولید",
  "۵. آزمایش‌ها و تست‌های دوره‌ای/سالانه",
  "۶. ردیابی و گزارش‌ها",
  "۷. مدیریت سامانه"
 ]);
 const anchors=[
  'href="/factory-master-data.html"','data-page="items"','data-page="centers"',
  'data-page="shifts"','data-page="bom"','data-page="routings"',
  'href="/engineering-rebind.html"','href="/planning-advanced.html"',
  'data-page="plans"','href="/planning.html"','href="/mps-pegging.html"',
  'href="/execution.html"','href="/production-deviations.html"',
  'href="/control-plan.html"','href="/factory-release-readiness.html"',
  'href="/factory-analytical-verification.html"','href="/factory-recall.html"',
  'href="/factory-reports.html"','href="/system-users.html"'
 ];
 let last=-1;
 for(const marker of anchors){
  const position=nav.indexOf(marker);
  assert.ok(position>=0,"missing primary navigation entry "+marker);
  assert.ok(position>last,"navigation order changed before "+marker);
  last=position;
 }
});

test("quality and periodic/annual test forms are in one navigation group",()=>{
 const begin=nav.indexOf('aria-label="آزمایش‌ها و تست‌های دوره‌ای و سالانه"');
 const end=nav.indexOf('aria-label="ردیابی و گزارش‌ها"',begin);
 assert.ok(begin>=0&&end>begin,"test group is absent or out of order");
 const group=nav.slice(begin,end);
 for(const href of ["/control-plan.html","/factory-release-readiness.html","/factory-analytical-verification.html"])
  assert.ok(group.includes('href="'+href+'"'),"test-related page is outside its group: "+href);
});

test("navigation group styling preserves responsive and keyboard-accessible sidebar behavior",()=>{
 assert.match(css,/#primary-nav \.nav-group\{/);
 assert.match(css,/#primary-nav \.nav-group-title\{/);
 assert.match(css,/focus-visible/);
 assert.match(css,/@media\(max-width:900px\)/);
});
