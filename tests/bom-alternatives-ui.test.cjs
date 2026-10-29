const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..', 'src', 'IPPCS.Web', 'wwwroot');
const planning = fs.readFileSync(path.join(root, 'planning.html'), 'utf8');
const planningJs = fs.readFileSync(path.join(root, 'js', 'planning-stage3.js'), 'utf8');
const execution = fs.readFileSync(path.join(root, 'execution.html'), 'utf8');
const executionJs = fs.readFileSync(path.join(root, 'js', 'execution.js'), 'utf8');
const service = fs.readFileSync(path.join(__dirname, '..', 'src', 'IPPCS.Web', 'BomAlternativeExecution.cs'), 'utf8');

test('BOM authoring exposes group, default, and priority fields', () => {
  assert.match(planning, /name="alternativeGroupCode"/);
  assert.match(planning, /name="isDefaultAlternative"/);
  assert.match(planning, /name="alternativePriority"/);
  assert.match(planningJs, /alternativeGroupCode:/);
  assert.match(planningJs, /isDefaultAlternative:/);
  assert.match(planningJs, /alternativePriority:/);
});

test('order UI can allocate alternatives and record actual material usage', () => {
  assert.match(execution, /id="materials"/);
  assert.match(execution, /id="material-options-save"/);
  assert.match(execution, /id="material-usage-form"/);
  assert.match(executionJs, /\/material-options\//);
  assert.match(executionJs, /\/material-usage/);
  assert.match(service, /SharePercent/);
  assert.match(service, /ComponentItemId = line.ComponentItemId/);
  assert.match(service, /RecordedBy = context\.User\.Identity/);
  assert.match(execution, /material-usage-history/);
  assert.match(executionJs, /x\.recordedBy/);
  assert.match(service, /مصرف تجمعی از مقدار برنامه‌ریزی‌شده بیشتر است/);
});

test('alternative allocation is exact-100 percent and never creates warehouse transactions', () => {
  assert.match(service, /input\.Shares\.Sum\(x => x\.SharePercent\) != 100m/);
  assert.match(service, /never posts warehouse stock|never posts warehouse stock/i);
  assert.doesNotMatch(service, /Warehouse|InventoryTransaction|StockLedger/);
});
