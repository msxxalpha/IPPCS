"""Stage 9.9 authenticated SQL smoke extension. No external ERP connections."""

def verify_completion_release(request, slot_path, reservation, confirm_path, order_id):
    planned = request(slot_path, "POST", reservation, 201)
    assert planned["id"]
    before = request(slot_path)["rows"]
    assert len(before) == 1 and before[0]["id"] == planned["id"]
    completed = request(confirm_path, "POST", {
        "goodQuantity": 2, "scrapQuantity": 0, "reworkQuantity": 0
    })
    assert completed["operations"][0]["isComplete"], completed
    assert not request(slot_path)["rows"], "Completed routing operation retained a future slot"
    audit = request(f"/api/v1/manufacturing-orders/{order_id}/history")["events"]
    assert any(x["action"] == "CapacityLifecycle" and
               "OperationCompleted" in (x["details"] or "") for x in audit), audit
    assert any(x["action"] == "CenterSlotCancelled" and
               x["details"] == planned["id"].replace("-", "") for x in audit), audit
    print("PASS: completed routing operation atomically releases its planned machine slot.")
