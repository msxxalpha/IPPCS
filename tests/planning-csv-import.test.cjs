const test=require("node:test");
const assert=require("node:assert/strict");
const fs=require("node:fs");
const path=require("node:path");

const root=path.resolve(__dirname,"../src/IPPCS.Web/wwwroot");
const importer=fs.readFileSync(path.join(root,"js/excel-csv-import.js"),"utf8");
const planning=fs.readFileSync(path.join(root,"planning.html"),"utf8");

test("planning BOM and MPS draft headers have matching import forms and API routes",()=>{
  assert.match(planning,/<form id="bom-version-form"/);
  assert.match(planning,/<form id="mps-form"/);
  assert.match(importer,/form: "#bom-version-form"/);
  assert.match(importer,/file: "ippcs-bom-revisions-template\.csv"/);
  assert.match(importer,/url: \(\) => apiRoot\+"\/bom-revisions"/);
  assert.match(importer,/form: "#mps-form"/);
  assert.match(importer,/file: "ippcs-mps-versions-template\.csv"/);
  assert.match(importer,/url: \(\) => apiRoot\+"\/mps"/);
});

test("versioned BOM and MPS line imports remain draft-only and validate duplicate keys",()=>{
  assert.match(importer,/form: "#bom-line-form"/);
  assert.match(importer,/if\(rev\.state!==0\)return "فقط به نسخه BOM پیش‌نویس می‌توان جزء افزود\."/);
  assert.match(importer,/form: "#mps-line-form"/);
  assert.match(importer,/if\(version\.state!==0\)return "فقط به نسخه MPS پیش‌نویس می‌توان ردیف افزود\."/);
  assert.match(importer,/ctx\.seenBom\.has\(componentKey\)\|\|ctx\.seenBom\.has\(sequenceKey\)/);
  assert.match(importer,/ctx\.seenMps\.has\(key\)/);
});

test("planning assumptions imports are explicitly non-transactional and support PUT for balances",()=>{
  assert.match(importer,/form: "#balance-form"/);
  assert.match(importer,/method: "PUT"/);
  assert.match(importer,/form: "#receipt-form"/);
  assert.match(importer,/فرضیات برنامه‌ریزی داخلی IPPCS هستند و موجودی واقعی انبار/);
  assert.match(importer,/سفارش خرید، رسید انبار یا تراکنش واقعی ایجاد نمی‌کنند/);
});

test("the importer labels itself CSV-only rather than claiming binary XLSX parsing",()=>{
  assert.match(importer,/input\.accept="\.csv,text\/csv"/);
  assert.match(importer,/با نوع CSV UTF-8 ذخیره نمایید/);
});
