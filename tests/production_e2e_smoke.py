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
history = request(f"/api/v1/manufacturing-orders/{order_id}/history")["events"]
assert any(e["action"] == "QualityHold" for e in history)
assert any(e["action"] == "QualityRelease" for e in history)
print("PASS: SQL Server route+BOM approval, work order release, production, quality hold, "
      "blocked transfer, QC release, completed order, WIP, audit and OEE.")
