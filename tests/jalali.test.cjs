const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");

const script = fs.readFileSync(path.join(__dirname,"../src/IPPCS.Web/wwwroot/js/jalali-datepicker.js"),"utf8");
const context = {
  window: {},
  document: {readyState:"loading",addEventListener(){}},
  Intl, Date, Number, Math, String, RangeError
};
vm.runInNewContext(script,context);
const j = context.window.IPPCSJalali;

test("Jalali date conversion, canonical October 2026 example",()=>{
  const date = j.toGregorian(1405,8,4);
  assert.equal(date.toISOString().slice(0,10),"2026-10-26");
  const parts = j.fromGregorian(new Date("2026-10-26T12:00:00Z"));
  assert.equal(j.format(parts),"1405/08/04");
});
test("Persian and Arabic digits normalize correctly",()=>{
  assert.equal(j.toLatin("۱۴۰۵/۰۸/۰۴"),"1405/08/04");
  assert.equal(j.toLatin("١٤٠٥/٠٨/٠٤"),"1405/08/04");
  assert.equal(j.format(j.parse("۱۴۰۵-۸-۴")),"1405/08/04");
});
test("invalid dates are rejected",()=>{
  for(const text of ["1405/13/01","1405/08/32","1405/12/30","1405/0/1","1405/1/0","text","1405/08/04 extra"])
    assert.equal(j.parse(text),null,text);
});
test("Jalali month lengths are correct for leap/non-leap years",()=>{
  assert.equal(j.daysInMonth(1405,1),31);
  assert.equal(j.daysInMonth(1405,7),30);
  assert.equal(j.daysInMonth(1405,12),29);
  assert.equal(j.daysInMonth(1399,12),30);
  assert.equal(j.format(j.parse("1399/12/30")),"1399/12/30");
});
test("every day of 1405 round-trips correctly",()=>{
  for(let m=1;m<=12;m++)for(let d=1;d<=j.daysInMonth(1405,m);d++){
    const result = j.fromGregorian(j.toGregorian(1405,m,d));
    assert.equal(j.format(result),j.format({y:1405,m,d}));
  }
});
