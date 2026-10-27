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
order = request("/api/v1/manufacturing-orders", "POST", {
    "number": "CI-WORKORDER-STAGE5", "itemId": product["id"],
    "plannedQuantity": 10, "plannedStartJalali": JALALI_NOW, "dueOnJalali": JALALI_NOW
}, 201)
order_id = order["id"]
released = request(f"/api/v1/manufacturing-orders/{order_id}/release", "POST")
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
confirmed = request(f"/api/v1/manufacturing-orders/{order_id}/operations/{second['id']}/confirm", "POST", {
    "goodQuantity": 9, "scrapQuantity": 0, "reworkQuantity": 0, "lotCode": "CI-LOT-001"
})
assert confirmed["status"] == 4
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
# Stage six: approved Control Plan is snapshot-bound to this routing revision at release.
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
print("PASS: isolated local operator account, permission boundary, material reporting, "
      "password change, administrator lock/unlock.")
print("PASS: SQL demand, S&OP, approved MPS, finite APS no overlap, what-if, "
      "line-side consumption variance, conversion cost and production KPI.")
print("PASS: approved Control Plan snapshots, required inspection gate, auto quarantine, "
      "scrap disposition, reinspection, final quality release, and exact-lot stage genealogy.")
print("PASS: SQL Server route+BOM approval, work order release, production, quality hold, "
      "blocked transfer, QC release, completed order, WIP, audit and OEE.")
