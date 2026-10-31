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
const printDocs = fs.readFileSync(path.join(__dirname, '..', 'src', 'IPPCS.Web', 'FactoryPrintDocuments.cs'), 'utf8');
const planningApi = fs.readFileSync(path.join(__dirname, '..', 'src', 'IPPCS.Web', 'PlanningStageThree.cs'), 'utf8');
const sopApsApi = fs.readFileSync(path.join(__dirname, '..', 'src', 'IPPCS.Web', 'FactoryDemandSopAps.cs'), 'utf8');
const capacityRules = fs.readFileSync(path.join(__dirname, '..', 'src', 'IPPCS.Web', 'WorkCenterProductCapacity.cs'), 'utf8');

test('BOM authoring exposes group, default, and priority fields', () => {
  assert.match(planning, /name="alternativeGroupCode"/);
  assert.match(planning, /name="isDefaultAlternative"/);
  assert.match(planning, /name="alternativePriority"/);
  assert.match(planningJs, /alternativeGroupCode:/);
  assert.match(planningJs, /isDefaultAlternative:/);
  assert.match(planningJs, /alternativePriority:/);
});

test('order UI saves alternative groups atomically and records real usage history', () => {
  assert.match(execution, /id="materials"/);
  assert.match(execution, /id="material-options-save"/);
  assert.match(execution, /id="material-usage-form"/);
  assert.match(execution, /id="material-usage-history"/);
  assert.match(executionJs, /\/material-options["]/);
  assert.match(executionJs, /groups\.push\(\{groupCode,shares:active\}\)/);
  assert.match(executionJs, /\/material-usage/);
  assert.match(service, /RecordedBy = context\.User\.Identity/);
  assert.match(service, /CalculatePlannedComponentQuantity/);
  assert.match(service, /پس از ثبت مصرف واقعی، ترکیب گروه/);
  assert.match(executionJs, /x\.recordedBy/);
});

test('alternative allocations validate 100 percent, preserve used groups, and never post stock', () => {
  assert.match(service, /g\.Shares\.Sum\(x => x\.SharePercent\) != 100m/);
  assert.match(service, /var hasUsage = await usage\.AnyAsync/);
  assert.match(service, /effectivePrior/);
  assert.match(service, /never posts warehouse stock/i);
  assert.doesNotMatch(service, /Warehouse|InventoryTransaction|StockLedger/);
});

test('BOM and production-order printouts disclose approved alternatives and recorded consumption', () => {
  assert.match(printDocs, /گروه آلترناتیو/);
  assert.match(printDocs, /سهم تولید/);
  assert.match(printDocs, /سوابق شارژ و مصرف واقعی مواد/);
  assert.match(printDocs, /RecordedBy/);
  assert.match(printDocs, /این گزارش تراکنش انبار نیست/);
});

test('saved MRP, S&OP and APS inputs retain the assumptions that produced their results', () => {
  assert.match(planningApi, /AlternativeGroupCode/);
  assert.match(planningApi, /IsDefaultAlternative/);
  assert.match(planningApi, /AlternativePriority/);
  assert.match(planningApi, /Lines.Select/);

  assert.match(capacityRules, /WorkCenterProductCapacityEvidence/);
  assert.match(capacityRules, /RoutingRunMinutesPerUnit/);
  assert.match(capacityRules, /EffectiveRunMinutesPerUnit/);
  assert.match(capacityRules, /HasProductCapacityOverride/);
  assert.match(capacityRules, /BuildEvidence/);

  assert.match(sopApsApi, /productCapacityEvidence=usedProductCapacityEvidence/);
  assert.match(sopApsApi, /usedRoutingRevisionIds/);
  assert.match(sopApsApi, /InputJson=JsonSerializer/);
});
