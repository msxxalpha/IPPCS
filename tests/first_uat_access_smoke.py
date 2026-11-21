"""Read-only first-UAT entry-point gate against the actual Development host.

Run after the disposable SQL integration scenario. No IPPCS business writes.
CI secrets arrive via environment variables; never log them.
"""
import http.cookiejar
import json
import os
import urllib.error
import urllib.request

BASE = os.environ.get("IPPCS_E2E_BASE", "http://127.0.0.1:5295")
USERNAME = os.environ.get("IPPCS_SMOKE_USERNAME", "ci-admin")
PASSWORD = os.environ["CI_ADMIN_PASSWORD"]

PAGES = [
    "/login.html", "/app.html", "/planning.html", "/planning-advanced.html",
    "/execution.html", "/control-plan.html", "/factory-reports.html",
    "/factory-print.html", "/factory-master-data.html",
    "/factory-release-readiness.html", "/factory-recall.html",
    "/production-deviations.html", "/mps-pegging.html",
    "/engineering-rebind.html", "/system-users.html", "/help.html",
]


jar = http.cookiejar.CookieJar()
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))


def get(path):
    with client.open(BASE + path, timeout=25) as response:
        assert response.status == 200, (path, response.status)
        return response.read()


def main():
    health = json.loads(get("/health"))
    assert health["status"] == "ready", health
    assert health["version"] == "0.8.0-first-uat1", health
    for page in PAGES:
        content = get(page)
        assert b"<html" in content.lower(), page
        assert len(content) > 500, (page, len(content))
    payload = json.dumps({"username": USERNAME, "password": PASSWORD}).encode()
    request = urllib.request.Request(
        BASE + "/auth/login", data=payload, method="POST",
        headers={"Content-Type": "application/json"})
    with client.open(request, timeout=25) as response:
        assert response.status == 200
    who = json.loads(get("/auth/me"))
    assert who["username"] == USERNAME and "Administrator" in who["roles"], who
    readiness = json.loads(get("/api/v1/factory-release-readiness"))
    assert readiness["declaredModules"] == 18
    assert len(readiness["modules"]) == 18
    assert readiness["productionReleaseAuthorized"] is False
    assert readiness["factoryAcceptedModules"] == 0
    for path in [
        "/api/v1/items", "/api/v1/work-centers", "/api/v1/routings",
        "/api/v1/bom-revisions", "/api/v1/mps",
        "/api/v1/manufacturing-orders", "/api/v1/control-plans",
        "/api/v1/factory-reports/dimensions",
    ]:
        assert get(path).strip().startswith((b"[", b"{")), path
    units = json.loads(get("/api/v1/factory-master-options?kind=UnitOfMeasure"))
    assert len(units) >= 20, "Missing initial unit-of-measure seed data"
    orders = json.loads(get("/api/v1/manufacturing-orders"))
    fixture = next(x for x in orders if x["number"] == "CI-WORKORDER-STAGE5")
    document = json.loads(get("/api/v1/factory-print/order/" + fixture["id"]))
    assert document["kind"] == "order"
    assert document["reference"] == fixture["number"]
    assert document["tables"][0]["rows"]
    print(f"PASS: first-UAT real-host gate: {len(PAGES)} HTML screens, "
          f"18 modules, admin login, 9 API groups, master catalog "
          f"and printed SQL manufacturing order.")


if __name__ == "__main__":
    main()
