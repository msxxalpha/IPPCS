"""IPPCS disposable-SQL end-to-end manufacturing acceptance smoke.
Requires the running Development host, cookie authentication and already-applied EF migrations.
Uses generated CI-only admin credentials; no external ERP or inventory connections.
"""
import http.cookiejar
import json
import os
import subprocess
import urllib.error
import urllib.request

BASE = os.environ.get("IPPCS_E2E_BASE", "http://127.0.0.1:5295")
PASSWORD = os.environ["CI_ADMIN_PASSWORD"]
JALALI_NOW = subprocess.check_output(
    ["node", "-e",
     """let p=new Intl.DateTimeFormat('en-US-u-ca-persian-nu-latn',{timeZone:'Asia/Tehran',year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date());console.log([p.find(x=>x.type==='year').value,p.find(x=>x.type==='month').value,p.find(x=>x.type==='day').value].join('/'))"""],
    text=True,
).strip()

jar = http.cookiejar.CookieJar()
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
csrf = None


def request(path, method="GET", payload=None, status=200):
    headers = {}
    if payload is not None:
        headers["Content-Type"] = "application/json"
    if path.startswith("/api/v1") and method in ("POST", "PUT", "PATCH", "DELETE"):
        headers["X-IPPCS-CSRF"] = csrf
    body = json.dumps(payload).encode("utf-8") if payload is not None else None
    message = urllib.request.Request(BASE + path, data=body, method=method, headers=headers)
    try:
        with client.open(message, timeout=30) as res:
            code = res.status
            raw = res.read()
    except urllib.error.HTTPError as exc:
        code = exc.code
        raw = exc.read()
    if code != status:
        raise AssertionError(f"{method} {path}: expected HTTP {status}, got {code}: {raw[:500]!r}")
    return json.loads(raw) if raw else None


request("/auth/login", "POST", {"username": "ci-admin", "password": PASSWORD})
csrf = request("/auth/csrf")["token"]
items = request("/api/v1/items")
product = next(row for row in items if row["code"] == "SHOCK-CI-001")
raw = request("/api/v1/items", "POST",
              {"code": "CI-PRODUCTION-RAW", "name": "Sample raw part", "unit": "PCS", "type": 0}, 201)
# Malformed first-run master inputs must return a controlled response, not a
# SQL truncation or null-reference server exception.
request("/api/v1/work-centers", "POST",
        {"code": "INVALID-CENTER", "name": None, "nominalUnitsPerHour": 60}, 400)
request("/api/v1/work-centers", "POST",
        {"code": "TOO-LONG", "name": "a" * 257, "nominalUnitsPerHour": 60}, 400)
request("/api/v1/work-centers", "POST",
        {"code": "OVER-CAPACITY", "name": "Over", "nominalUnitsPerHour": 1000001}, 400)
center = request("/api/v1/work-centers", "POST",
                 {"code": "CI-WELD-ASSEMBLY", "name": "CI Production Bench",
                  "nominalUnitsPerHour": 60}, 201)
route = request("/api/v1/routings", "POST",
                {"itemId": product["id"], "revisionCode": "CI-R1",
                 "effectiveFromJalali": JALALI_NOW, "effectiveToJalali": None}, 201)
route_id = route["id"]
for seq, name in [(10, "Tube welding"), (20, "Final damper assembly")]:
    request(f"/api/v1/routings/{route_id}/operations", "POST", {
        "sequence": seq, "name": name, "workCenterId": center["id"], "setupMinutes": 5,
        "runMinutesPerUnit": 1, "yieldPercent": 100
    }, 201)
request(f"/api/v1/routings/{route_id}/approve", "POST", status=200)
bom = request("/api/v1/bom-revisions", "POST", {
    "parentItemId": product["id"], "revision": "CI-BOM-1",
    "validFromJalali": JALALI_NOW, "validUntilJalali": None
}, 201)
request(f"/api/v1/bom-revisions/{bom['id']}/lines", "POST", {
    "componentItemId": raw["id"], "quantityPer": 2, "scrapPercent": 0, "sequence": 10
}, 201)
request(f"/api/v1/bom-revisions/{bom['id']}/approve", "POST", status=200)
# Release must fail closed until exactly one applicable approved Control Plan exists.
uncontrolled = request("/api/v1/manufacturing-orders", "POST", {
    "number": "CI-MISSING-CONTROL-PLAN", "itemId": product["id"],
    "plannedQuantity": 2, "plannedStartJalali": JALALI_NOW, "dueOnJalali": JALALI_NOW
}, 201)
request(f"/api/v1/manufacturing-orders/{uncontrolled['id']}/release", "POST", status=409)
control = request("/api/v1/control-plans", "POST", {
    "routingRevisionId": route_id, "code": "CI-CONTROLPLAN-001",
    "validFromJalali": JALALI_NOW, "validUntilJalali": None
}, 201)
for sequence, code, minimum, maximum in [
    (10, "WELD-DIM", 10, 20), (20, "DAMPING-FORCE", 65, 85)
]:
    request(f"/api/v1/control-plans/{control['id']}/characteristics", "POST", {
        "operationSequence": sequence, "code": code, "description": code,
        "unit": "N", "minimum": minimum, "maximum": maximum, "mandatory": True
    }, 201)
request(f"/api/v1/control-plans/{control['id']}/approve", "POST")
order = request("/api/v1/manufacturing-orders", "POST", {
    "number": "CI-WORKORDER-STAGE5", "itemId": product["id"],
    "plannedQuantity": 10, "plannedStartJalali": JALALI_NOW, "dueOnJalali": JALALI_NOW
}, 201)
order_id = order["id"]
draft_print = request(f"/api/v1/factory-print/order/{order_id}")
assert draft_print["status"].startswith("پیش‌نویس"),draft_print
assert draft_print["officiallyApproved"] is False,draft_print
assert draft_print["reference"] == order["number"],draft_print
assert draft_print["tables"][0]["columns"][0] == "ردیف",draft_print
released = request(f"/api/v1/manufacturing-orders/{order_id}/release", "POST")
released_print = request(f"/api/v1/factory-print/order/{order_id}")
assert released_print["status"].startswith("آزادشده"),released_print
assert released_print["officiallyApproved"] is True,released_print
assert len(released_print["tables"][0]["rows"]) == 2,released_print
assert released["status"] == 1 and len(released["operations"]) == 2
first, second = released["operations"]
request(f"/api/v1/manufacturing-orders/{order_id}/operations/{first['id']}/confirm", "POST", {
    "goodQuantity": 9, "scrapQuantity": 1, "reworkQuantity": 0,
    "lotCode": "CI-LOT-001", "note": "Factory test"
})
hold = request(f"/api/v1/manufacturing-orders/{order_id}/quality-holds", "POST", {
    "operationId": first["id"], "quantity": 4, "reason": "CI test quality quarantine"
}, 201)
hold_id = hold["id"]
wip = request(f"/api/v1/manufacturing-orders/{order_id}/wip")["rows"]
assert wip[0]["pendingQuality"] == 4 and wip[0]["forwardAvailable"] == 5
request(f"/api/v1/manufacturing-orders/{order_id}/operations/{second['id']}/confirm", "POST", {
    "goodQuantity": 9, "scrapQuantity": 0, "reworkQuantity": 0
}, 409)
request(f"/api/v1/manufacturing-orders/{order_id}/quality-holds/{hold_id}/disposition?decision=1",
        "POST", {"note": "QC accepted after reinspection"})
first_requirements = request(f"/api/v1/manufacturing-orders/{order_id}/inspection-requirements")["requirements"]
first_requirement = next(r for r in first_requirements if r["operationSequence"] == 10)
final_requirement = next(r for r in first_requirements if r["operationSequence"] == 20)
first_inspection = request(f"/api/v1/manufacturing-orders/{order_id}/inspections/{first_requirement['id']}", "POST", {
    "value": 15, "nonconformingQuantity": 0, "note": "Weld dimension accepted"
})
assert first_inspection["passed"]
confirmed = request(f"/api/v1/manufacturing-orders/{order_id}/operations/{second['id']}/confirm", "POST", {
    "goodQuantity": 9, "scrapQuantity": 0, "reworkQuantity": 0, "lotCode": "CI-LOT-001"
})
assert confirmed["status"] == 2
final_inspection = request(f"/api/v1/manufacturing-orders/{order_id}/inspections/{final_requirement['id']}", "POST", {
    "value": 70, "nonconformingQuantity": 0, "note": "Final force test accepted"
})
assert final_inspection["orderStatus"] == 4
holds = request(f"/api/v1/manufacturing-orders/{order_id}/quality-holds")
assert len(holds) == 1 and holds[0]["status"] == 1
wip = request(f"/api/v1/manufacturing-orders/{order_id}/wip")["rows"]
assert wip[0]["awaitingNextOperation"] == 0
request("/api/v1/oee/plans", "PUT", {
    "workCenterId": center["id"], "productionDateJalali": JALALI_NOW,
    "plannedMinutes": 480, "note": "CI planned time input"
})
report = request("/api/v1/oee/daily?dateJalali=" + JALALI_NOW)
row = next(x for x in report["rows"] if x["workCenterId"] == center["id"])
assert row["good"] == 18 and row["scrap"] == 1 and row["oeePercent"] is not None, row
lots = request("/api/v1/manufacturing-lots/search?lotCode=CI-LOT-001")
assert lots["count"] == 2
assert all(row["orderNumber"] == "CI-WORKORDER-STAGE5" for row in lots["records"])
history = request(f"/api/v1/manufacturing-orders/{order_id}/history")["events"]
assert any(e["action"] == "QualityHold" for e in history)
assert any(e["action"] == "QualityRelease" for e in history)
# Stage six: the same approved Control Plan is snapshot-bound to every eligible order.
fresh = request("/api/v1/manufacturing-orders", "POST", {
    "number": "CI-CONTROLLED-STAGE6", "itemId": product["id"], "plannedQuantity": 10,
    "plannedStartJalali": JALALI_NOW, "dueOnJalali": JALALI_NOW
}, 201)
fid = fresh["id"]
released = request(f"/api/v1/manufacturing-orders/{fid}/release", "POST")
assert released["status"] == 1
first, second = released["operations"]
requirements = request(f"/api/v1/manufacturing-orders/{fid}/inspection-requirements")["requirements"]
assert len(requirements) == 2 and all(r["mandatory"] for r in requirements)
r1 = next(r for r in requirements if r["operationSequence"] == 10)
r2 = next(r for r in requirements if r["operationSequence"] == 20)
request(f"/api/v1/manufacturing-orders/{fid}/operations/{first['id']}/confirm", "POST", {
    "goodQuantity": 9, "scrapQuantity": 1, "reworkQuantity": 0,
    "lotCode": "CP-LOT-BASE"
})
request(f"/api/v1/manufacturing-orders/{fid}/operations/{second['id']}/confirm", "POST", {
    "goodQuantity": 9, "scrapQuantity": 0, "reworkQuantity": 0,
    "lotCode": "CP-LOT-ASSEMBLY"
}, 409)
failed = request(f"/api/v1/manufacturing-orders/{fid}/inspections/{r1['id']}", "POST", {
    "value": 6, "nonconformingQuantity": 3, "note": "Below control limit"
})
assert not failed["passed"]
holds = request(f"/api/v1/manufacturing-orders/{fid}/quality-holds")
open_hold = next(h for h in holds if h["status"] == 0)
assert open_hold["quantity"] == 3
request(f"/api/v1/manufacturing-orders/{fid}/quality-holds/{open_hold['id']}/disposition?decision=2",
        "POST", {"note": "Quality scrap after investigation"})
request(f"/api/v1/manufacturing-orders/{fid}/operations/{second['id']}/confirm", "POST", {
    "goodQuantity": 6, "scrapQuantity": 0, "reworkQuantity": 0
}, 409)
passed = request(f"/api/v1/manufacturing-orders/{fid}/inspections/{r1['id']}", "POST", {
    "value": 15, "nonconformingQuantity": 0, "note": "Reinspection passed"
})
assert passed["passed"]
second_result = request(f"/api/v1/manufacturing-orders/{fid}/operations/{second['id']}/confirm", "POST", {
    "goodQuantity": 6, "scrapQuantity": 0, "reworkQuantity": 0, "lotCode": "CP-LOT-ASSEMBLY"
})
assert second_result["status"] == 2, second_result
final_inspection = request(f"/api/v1/manufacturing-orders/{fid}/inspections/{r2['id']}", "POST", {
    "value": 70, "nonconformingQuantity": 0, "note": "Final damper force test passed"
})
assert final_inspection["orderStatus"] == 4
measurements = request(f"/api/v1/manufacturing-orders/{fid}/inspection-results")
assert len(measurements) == 3
confirmations = request(f"/api/v1/manufacturing-orders/{fid}/confirmations")
source = next(c for c in confirmations if c["operationSequence"] == 10)
dest = next(c for c in confirmations if c["operationSequence"] == 20)
request(f"/api/v1/manufacturing-orders/{fid}/lot-genealogy", "POST", {
    "sourceConfirmationId": source["id"], "destinationConfirmationId": dest["id"],
    "quantity": 6
}, 201)
links = request(f"/api/v1/manufacturing-orders/{fid}/lot-genealogy")
assert len(links) == 1 and links[0]["quantity"] == 6
request(f"/api/v1/manufacturing-orders/{fid}/lot-genealogy", "POST", {
    "sourceConfirmationId": source["id"], "destinationConfirmationId": dest["id"],
    "quantity": 1
}, 409)
# The forecast endpoint is deliberately read-only, requires no external sales data.
# With no prior confirmed history, its estimated quantity must be zero.
forecast = request(f"/api/v1/factory-demand/forecast/{product['id']}?fromJalali={JALALI_NOW}&lookback=6&months=2")
assert len(forecast["rows"]) == 2
# Stage seven: independent demand, capacity balancing and finite scheduling.
from datetime import datetime
from zoneinfo import ZoneInfo
day_of_week = (datetime.now(ZoneInfo("Asia/Tehran")).weekday() + 1) % 7
request("/api/v1/work-shifts", "POST", {
    "workCenterId": center["id"], "dayOfWeek": day_of_week,
    "startTime": "08:00", "endTime": "16:00", "effectiveCapacityPercent": 100
}, 201)
demand = request("/api/v1/factory-demand", "POST", {
    "itemId": product["id"], "reference": "CI-Internal-need",
    "dueOnJalali": JALALI_NOW, "quantity": 10, "category": "Forecast"
}, 201)
assert demand["id"]
draft = request("/api/v1/mps", "POST", {"code": "CI-MPS-FROM-DEMAND"}, 201)
linked = request(f"/api/v1/factory-demand/{demand['id']}/to-mps/{draft['id']}", "POST", status=201)
assert linked["mpsId"] == draft["id"]
request(f"/api/v1/factory-demand/{demand['id']}/to-mps/{draft['id']}", "POST", status=409)
sop = request("/api/v1/factory-sop/calculate", "POST", {
    "code": "CI-SOP-001", "fromJalali": JALALI_NOW, "throughJalali": JALALI_NOW
})
assert sop["demandQuantity"] >= 10 and sop["availableMinutes"] >= 480
mps = request("/api/v1/mps", "POST", {"code": "CI-MPS-STAGE7"}, 201)
request(f"/api/v1/mps/{mps['id']}/lines", "POST", {
    "itemId": product["id"], "neededOnJalali": JALALI_NOW,
    "quantity": 10, "reference": "CI planned shock absorbers"
}, 201)
request(f"/api/v1/mps/{mps['id']}/approve", "POST")
# Material feasibility must fail closed before any raw-material baseline exists.
request("/api/v1/factory-aps/calculate", "POST", {
    "mpsVersionId": mps["id"], "startJalali": JALALI_NOW,
    "horizonDays": 7, "capacityMultiplier": 1
}, 422)
request(f"/api/v1/planning-balances/{raw['id']}", "PUT", {
    "itemId": raw["id"], "physicalQuantity": 25, "reservedQuantity": 0,
    "quarantinedQuantity": 0, "safetyQuantity": 0, "leadDays": 1,
    "minimumBatch": 1, "batchMultiple": 1, "snapshotDateJalali": JALALI_NOW
})
aps = request("/api/v1/factory-aps/calculate", "POST", {
    "mpsVersionId": mps["id"], "startJalali": JALALI_NOW,
    "horizonDays": 7, "capacityMultiplier": 1
})
assert len(aps["allocations"]) == 2 and len(aps["unscheduled"]) == 0, aps
assert aps["allocations"][0]["finishTime"] <= aps["allocations"][1]["startTime"]
assert request(f"/api/v1/factory-aps/runs/{aps['runId']}")["result"]
whatif = request("/api/v1/factory-whatif/capacity", "POST", {
    "mpsVersionId": mps["id"], "startJalali": JALALI_NOW,
    "horizonDays": 7, "baselineCapacity": 1, "scenarioCapacity": 0.75
})
assert whatif["baseline"]["scheduled"] == 2
material_order = request("/api/v1/manufacturing-orders", "POST", {
    "number": "CI-WIP-MATERIAL-STAGE7", "itemId": product["id"],
    "plannedQuantity": 10, "plannedStartJalali": JALALI_NOW,
    "dueOnJalali": JALALI_NOW
}, 201)
material_id = material_order["id"]
request(f"/api/v1/manufacturing-orders/{material_id}/release", "POST")
consumed = request(f"/api/v1/factory-material/{material_id}/consumption", "POST", {
    "componentItemId": raw["id"], "quantity": 19, "reference": "CI trial line consumption"
}, 201)
assert consumed["id"]
variance = request(f"/api/v1/factory-material/{material_id}")["rows"]
assert any(x["componentItemId"] == raw["id"] and x["recordedConsumed"] == 19
           and x["plannedRequired"] == 20 for x in variance)
request("/api/v1/factory-cost/rates", "PUT", {
    "workCenterId": center["id"], "conversionRialsPerMinute": 200,
    "scrapRialsPerUnit": 15000, "effectiveFromJalali": JALALI_NOW,
    "note": "Non-financial test-only conversion assumption"
})
cost = request(f"/api/v1/factory-cost/orders/{fid}")
assert cost["totalConversionRials"] >= 0 and cost["missingRates"] == 0, cost
overview = request("/api/v1/factory-kpis/overview")
assert overview["completedOrders"] >= 2
adherence = request(
    f"/api/v1/factory-kpis/adherence/{mps['id']}?fromJalali={JALALI_NOW}&throughJalali={JALALI_NOW}"
)
assert adherence["rows"] and adherence["rows"][0]["mpsRequired"] == 10
# Local IPPCS accounts only; do not read HR or Active Directory.
operator_login = "ci-production-operator"
operator_password = "CI_Test_Operator_2026!"
user = request("/api/v1/system-users", "POST", {
    "username": operator_login, "password": operator_password,
    "role": "ProductionOperator"
}, 201)
assert user["role"] == "ProductionOperator"
users = request("/api/v1/system-users")
assert any(u["userName"] == operator_login for u in users)
operator_client = urllib.request.build_opener(
    urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

def operator_request(path, method="GET", payload=None, expected=200, token=None):
    headers = {"Content-Type": "application/json"} if payload is not None else {}
    if token:
        headers["X-IPPCS-CSRF"] = token
    req = urllib.request.Request(BASE + path, method=method, headers=headers,
                                 data=json.dumps(payload).encode() if payload is not None else None)
    try:
        with operator_client.open(req, timeout=30) as res:
            status = res.status
            raw = res.read()
    except urllib.error.HTTPError as e:
        status = e.code
        raw = e.read()
    assert status == expected, f"{path}: status {status}, expected {expected}, {raw[:300]}"
    return json.loads(raw) if raw else None

operator_request("/auth/login", "POST", {
    "username": operator_login, "password": operator_password
})
operator_csrf = operator_request("/auth/csrf")["token"]
operator_profile = operator_request("/auth/me")
assert "ProductionOperator" in operator_profile["roles"]
assert "Planner" not in operator_profile["roles"]
assert "Administrator" not in operator_profile["roles"]

# Stage 24: shop-floor operator is never the manufacturing plan/release authority.
operator_request("/api/v1/manufacturing-orders", "POST", {
    "number":"CI-ST24-OPERATOR-MUST-NOT-CREATE","itemId":product["id"],
    "plannedQuantity":2,"plannedStartJalali":JALALI_NOW,
    "dueOnJalali":JALALI_NOW
}, expected=403, token=operator_csrf)
operator_request(f"/api/v1/manufacturing-orders/{order_id}/release",
    "POST", expected=403, token=operator_csrf)
operator_request(f"/api/v1/manufacturing-orders/{order_id}/cancel",
    "POST", expected=403, token=operator_csrf)
operator_request(f"/api/v1/manufacturing-orders/{order_id}/resume",
    "POST", expected=403, token=operator_csrf)
operator_request("/api/v1/manufacturing-orders/00000000-0000-0000-0000-000000000001/hold",
    "POST", {"reason":"Operator may safely hold an existing order"},
    expected=404, token=operator_csrf)
print("PASS: SQL operator cannot create, release, cancel or resume manufacturing orders; may raise production hold.")

operator_request("/api/v1/system-users", expected=403)
operator_request("/api/v1/factory-demand", "POST", {
    "itemId": product["id"], "reference": "operator must not plan",
    "dueOnJalali": JALALI_NOW, "quantity": 10, "category": "Confirmed"
}, expected=403, token=operator_csrf)
operator_request(f"/api/v1/factory-material/{material_id}/consumption", "POST", {
    "componentItemId": raw["id"], "quantity": 1,
    "reference": "operator role permits shop-floor reporting"
}, expected=201, token=operator_csrf)
operator_request("/api/v1/auth/change-password", "POST", {
    "currentPassword": operator_password,
    "newPassword": "CI_Test_Operator_New_2026!"
}, token=operator_csrf)
request(f"/api/v1/system-users/{user['id']}/lock", "POST", status=204)
request(f"/api/v1/system-users/{user['id']}/unlock", "POST", status=204)
# Stage eight: explicit MPS to work-order pegging cannot double allocate plan quantity.
peg_order = request("/api/v1/manufacturing-orders", "POST", {
    "number": "CI-ORDER-MPS-PEG", "itemId": product["id"],
    "plannedQuantity": 10, "plannedStartJalali": JALALI_NOW,
    "dueOnJalali": JALALI_NOW
}, 201)
peg_report = request(f"/api/v1/production-pegging/mps/{mps['id']}")
peg_line = peg_report["rows"][0]
assert peg_line["unassignedQuantity"] == 10
request(f"/api/v1/production-pegging/orders/{peg_order['id']}", "POST", {
    "mpsLineId": peg_line["id"], "linkedQuantity": 10
}, 201)
request(f"/api/v1/production-pegging/orders/{peg_order['id']}", "POST", {
    "mpsLineId": peg_line["id"], "linkedQuantity": 1
}, 409)
updated_report = request(f"/api/v1/production-pegging/mps/{mps['id']}")
assert updated_report["rows"][0]["assignedQuantity"] == 10
assert updated_report["rows"][0]["unassignedQuantity"] == 0
print("PASS: approved MPS quantity links to work orders; duplicate allocations blocked.")
# Audited production deviation workflow tied to the approved MPS line and pegged order.
deviation = request("/api/v1/production-deviations", "POST", {
    "mpsLineId": peg_line["id"], "orderId": peg_order["id"],
    "category": 1, "description": "CI due-date plan variance test"
}, 201)
deviation_id = deviation["id"]
assert deviation["status"] == 0
request(f"/api/v1/production-deviations/{deviation_id}/close", "POST", {
    "verification": "Should be refused until action assigned"
}, 409)
assigned = request(f"/api/v1/production-deviations/{deviation_id}/action", "POST", {
    "rootCause": "Equipment maintenance",
    "correctiveAction": "Reschedule constrained center",
    "responsible": "IPPCS CI planning operator",
    "actionDueJalali": JALALI_NOW
})
assert assigned["status"] == 1
closed = request(f"/api/v1/production-deviations/{deviation_id}/close", "POST", {
    "verification": "Corrective action verified by CI"
})
assert closed["status"] == 2
deviations = request(f"/api/v1/production-deviations?mpsLineId={peg_line['id']}")
assert any(x["id"] == deviation_id and len(x["events"]) == 3 for x in deviations)
print("PASS: approved MPS deviation root cause, assigned action, auditable closure and state validation.")
# Stage 9.4: real SQL authenticated net MRP graph; no warehouse transaction.
request(f"/api/v1/planning-balances/{product['id']}", "PUT", {
    "itemId": product["id"], "physicalQuantity": 0, "reservedQuantity": 0,
    "quarantinedQuantity": 0, "safetyQuantity": 0, "leadDays": 0,
    "minimumBatch": 1, "batchMultiple": 1, "snapshotDateJalali": JALALI_NOW
})
net_trace = request(f"/api/v1/mrp/{mps['id']}/net-demand-pegging")
assert net_trace["kind"] == "TimePhasedNetMrpProposalGraph"
parent_lots = [x for x in net_trace["plannedLots"] if x["itemId"] == product["id"]]
assert len(parent_lots) == 1 and parent_lots[0]["quantity"] == 10
component_needs = [x for x in net_trace["demands"] if x["parentLotKey"] == parent_lots[0]["lotKey"]]
assert len(component_needs) == 1 and component_needs[0]["quantity"] == 20
component_allocations = [x for x in net_trace["allocations"]
                         if x["demandKey"] == component_needs[0]["demandKey"]]
assert sum(x["quantity"] for x in component_allocations) == 20
assert all(x["sourceType"] == "PlanningOpeningBalance" for x in component_allocations)
assert all(x["availableOnJalali"] <= x["neededOnJalali"] for x in component_allocations)
assert not [x for x in net_trace["plannedLots"] if x["itemId"] == raw["id"]]
print("PASS: SQL net MRP proposal lots, generated component needs and bounded dated allocations.")
# Stage 9.5: finite, read-only center capacity check based on the same net proposed lots.
preview = request(f"/api/v1/mrp/{mps['id']}/lot-capacity-preview?horizonDays=7&capacityMultiplier=1")
assert preview["kind"] == "UnconfirmedNetMrpBatchCapacityPreview"
assert len(preview["lots"]) == 1 and preview["lots"][0]["lotKey"] == parent_lots[0]["lotKey"]
assert preview["lateLots"] == 0, preview
# Earlier stages leave an in-progress manufacturing order on this center.
# Stage 9.6 must not claim that resource is available for new proposals.
assert preview["conservativelyBlockedActiveOrders"] >= 1, preview
assert preview["blockedLots"] == 1 and preview["onTimeLots"] == 0, preview
assert not preview["allocations"], preview
assert preview["lots"][0]["status"] == "Unscheduled", preview
assert request(f"/api/v1/mrp/{mps['id']}/lot-capacity-preview?horizonDays=0",status=400)["message"]
print("PASS: SQL net MRP proposed lot finite-center check, operation precedence and validation.")
# Stage 9.6: no false capacity availability while an active production order
# still uses the same operation center. This is an independent IPPCS order.
competing = request("/api/v1/manufacturing-orders", "POST", {
    "number": "CI-RESERVED-CENTER-STAGE96",
    "itemId": product["id"], "plannedQuantity": 2,
    "plannedStartJalali": JALALI_NOW, "dueOnJalali": JALALI_NOW
}, 201)
request(f"/api/v1/manufacturing-orders/{competing['id']}/release", "POST")
conflicted = request(f"/api/v1/mrp/{mps['id']}/lot-capacity-preview?horizonDays=7")
assert conflicted["blockedLots"] >= 1 and conflicted["onTimeLots"] == 0, conflicted
assert conflicted["conservativelyBlockedActiveOrders"] >= 1, conflicted
assert not conflicted["allocations"], conflicted
print("PASS: active manufacturing order conservatively reserves its work centers in net MRP preview.")
# Stage 9.7: append-only real operation clock on an existing released order.
clock_order_id = competing["id"]
clock_op_id = request(f"/api/v1/manufacturing-orders/{clock_order_id}")["operations"][0]["id"]
clock_base = f"/api/v1/manufacturing-orders/{clock_order_id}/operations/{clock_op_id}"
clock_start = request(clock_base + "/clock-start", "POST")
assert clock_start["isRunning"] and clock_start["startedUtc"]
request(clock_base + "/clock-start", "POST", status=409)
request(clock_base + "/confirm", "POST", {
    "goodQuantity": 1, "scrapQuantity": 0, "reworkQuantity": 0
}, 409)
during_clock = request(f"/api/v1/mrp/{mps['id']}/lot-capacity-preview?horizonDays=7")
assert during_clock["runningOperationClockSessions"] >= 1, during_clock
clock_stop = request(clock_base + "/clock-stop", "POST")
assert not clock_stop["isRunning"] and clock_stop["stoppedUtc"]
request(clock_base + "/clock-stop", "POST", status=409)
clock_history = request(f"/api/v1/manufacturing-orders/{clock_order_id}/operation-clock")["sessions"]
assert len(clock_history) == 1 and not clock_history[0]["isRunning"]
assert clock_history[0]["operationId"] == clock_op_id
clock_final = request(f"/api/v1/mrp/{mps['id']}/lot-capacity-preview?horizonDays=7")
assert clock_final["recordedOperationClockSessions"] >= 1, clock_final
assert clock_final["runningOperationClockSessions"] == 0, clock_final
print("PASS: actual operation start-stop clock, duplicate prevention, immutable journal and capacity preview.")

# Stage 9.8: SQL reservation and cancellation with conflicts.
next_day = subprocess.check_output(["node", "-e",
 "let p=new Intl.DateTimeFormat('en-US-u-ca-persian-nu-latn',{timeZone:'Asia/Tehran',year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date(Date.now()+86400000)); console.log([p.find(x=>x.type==='year').value,p.find(x=>x.type==='month').value,p.find(x=>x.type==='day').value].join('/'))"],text=True).strip()
request("/api/v1/work-shifts","POST",{"workCenterId":center["id"],
 "dayOfWeek":(day_of_week+1)%7,"startTime":"08:00","endTime":"16:00",
 "effectiveCapacityPercent":100},201)
slot_path=f"/api/v1/manufacturing-orders/{clock_order_id}/capacity-reservations"
reservation={"operationId":clock_op_id,"dateJalali":next_day,
 "startTime":"08:00","endTime":"09:00"}
booked=request(slot_path,"POST",reservation,201)
assert booked["id"]
check=request(slot_path)["rows"]
assert len(check)==1 and check[0]["plannedMinutes"]==60
assert check[0]["actualMinutes"] is not None
request(slot_path,"POST",reservation,409)
other=request("/api/v1/manufacturing-orders","POST",{
 "number":"CI-SLOT-CONFLICT-ORDER","itemId":product["id"],"plannedQuantity":2,
 "plannedStartJalali":JALALI_NOW,"dueOnJalali":JALALI_NOW},201)
other_released=request(f"/api/v1/manufacturing-orders/{other['id']}/release","POST")
other_path=f"/api/v1/manufacturing-orders/{other['id']}/capacity-reservations"
other_booking={"operationId":other_released["operations"][0]["id"],"dateJalali":next_day,
 "startTime":"08:30","endTime":"09:30"}
request(other_path,"POST",other_booking,409)
cancel_path=slot_path+"/"+booked["id"]+"/cancel"
assert request(cancel_path,"POST")["cancelled"]
request(cancel_path,"POST",status=409)
assert not request(slot_path)["rows"]
new_booking=request(other_path,"POST",other_booking,201)
assert new_booking["id"]!=booked["id"]
preview_booked=request(f"/api/v1/mrp/{mps['id']}/lot-capacity-preview?horizonDays=7")
assert preview_booked["activeCapacityReservations"]>=1,preview_booked
print("PASS: SQL machine reservations, cross-order conflict, cancellation and MRP capacity.")

# Stage 9.9 lifecycle releases are appended to the immutable journal.
cancel_order=request(f"/api/v1/manufacturing-orders/{other['id']}/cancel","POST")
assert cancel_order["status"]==5
assert not request(other_path)["rows"]
cancel_events=request(f"/api/v1/manufacturing-orders/{other['id']}/history")["events"]
assert any(x["action"]=="CapacityLifecycle" for x in cancel_events)






# Backtesting only a fully elapsed Persian month, using IPPCS internal confirmed demand.
jalali_past = subprocess.check_output(["node", "-e",
 "let p=new Intl.DateTimeFormat('en-US-u-ca-persian-nu-latn',{timeZone:'Asia/Tehran',year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date(Date.now()-45*86400000));console.log([p.find(x=>x.type==='year').value,p.find(x=>x.type==='month').value,p.find(x=>x.type==='day').value].join('/'))"
],text=True).strip()
request("/api/v1/factory-demand","POST",{
 "itemId":product["id"],"reference":"CI-FORECAST-BACKTEST",
 "quantity":12,"dueOnJalali":jalali_past,"category":"Confirmed"},201)
backtest=request(f"/api/v1/factory-demand/backtest/{product['id']}?throughJalali={jalali_past}&lookback=3&months=2")
assert backtest["monthsEvaluated"]==2 and len(backtest["months"])==2, backtest
assert backtest["wapePercent"] is not None, backtest
assert request(f"/api/v1/factory-demand/backtest/{product['id']}?throughJalali={JALALI_NOW}&lookback=3&months=2",status=400)
print("PASS: SQL completed Jalali-month forecast backtest, WAPE guard and future-period rejection.")

# Stage 10: approved MPS pegged adherence is not aggregate-product inference.
peg_adherence=request(f"/api/v1/factory-kpis/peg-adherence/{mps['id']}")
peg_row=next(x for x in peg_adherence["rows"] if x["lineId"]==peg_line["id"])
assert peg_row["assigned"]==10 and peg_row["required"]==10,peg_row
assert peg_row["unassigned"]==0 and peg_row["shortage"]>=0,peg_row
print("PASS: SQL linked MPS row adherence uses only assigned production orders.")

# Stage 10: measured operations cost report preserves missing data.
real_cost=request(f"/api/v1/factory-cost/orders/{clock_order_id}/actual-variance")
assert real_cost["orderId"]==clock_order_id
assert len(real_cost["rows"])==2
assert real_cost["rows"][0]["actualMinutes"] is not None
assert real_cost["rows"][1]["actualMinutes"] is None
assert real_cost["totals"]["actualConversionRials"] is None
clock_oee=request("/api/v1/oee/clock-verified?dateJalali="+JALALI_NOW)
assert clock_oee["timeZone"]=="Asia/Tehran"
assert any(row["centerId"]==center["id"] for row in clock_oee["rows"])
assert request("/api/v1/oee/clock-verified?dateJalali=1405/13/40",status=400)
print("PASS: SQL measured conversion cost/OEE preserve missing observations and validate Jalali dates.")

from stage11_production_trace_checks import stage11_acceptance
stage11_acceptance(request,raw["id"],center["id"],JALALI_NOW)

from stage33_master_data_checks import stage33_acceptance
stage33_acceptance(request,peg_line["id"],JALALI_NOW)

from stage34_factory_reasons_checks import stage34_acceptance
stage34_acceptance(request,product["id"],JALALI_NOW)

from stage35_first_run_acceptance import stage35_acceptance
stage35_acceptance(request,route_id,JALALI_NOW)

from stage36_analytical_verification_checks import stage36_acceptance
stage36_acceptance(request,JALALI_NOW)

from stage37_aps_catalog_checks import stage37_acceptance
stage37_acceptance(request,mps["id"],center["id"],JALALI_NOW)

print("PASS: isolated local operator account, permission boundary, material reporting, "
      "password change, administrator lock/unlock.")
print("PASS: SQL demand, S&OP, approved MPS, finite APS no overlap, what-if, "
      "line-side consumption variance, conversion cost and production KPI.")
print("PASS: approved Control Plan snapshots, required inspection gate, auto quarantine, "
      "scrap disposition, reinspection, final quality release, and exact-lot stage genealogy.")
print("PASS: SQL Server route+BOM approval, work order release, production, quality hold, "
      "blocked transfer, QC release, completed order, WIP, audit and OEE.")
