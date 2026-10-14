#!/usr/bin/env python3
"""
Appium WebDriver navigation helper for App Store screenshots.
Targets one sim at a time; caller orchestrates per-device sessions.

Invoked from the App Store screenshot capture flow documented in
`docs/plans/app-store-screenshots.md`. Pure-stdlib (urllib) — no pip deps.
Per-screen entry points; does not own screenshot capture itself
(use `xcrun simctl io <UDID> screenshot <path>` for the actual file write).
"""
import json
import sys
import time
import urllib.request
import urllib.error
import subprocess

APPIUM_URL = "http://127.0.0.1:4723"
BUNDLE_ID = "com.multithreadedllc.prayercards"


def http(method, path, body=None, base=None):
    base = base or APPIUM_URL
    url = base + path
    data = None
    headers = {"Content-Type": "application/json"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            txt = r.read().decode("utf-8")
            return json.loads(txt) if txt else {}
    except urllib.error.HTTPError as e:
        body_txt = e.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"HTTP {e.code} on {method} {path}: {body_txt}") from e


def create_session(udid, device_name):
    caps = {
        "capabilities": {
            "alwaysMatch": {
                "platformName": "iOS",
                "appium:automationName": "XCUITest",
                "appium:platformVersion": "27.0",
                "appium:deviceName": device_name,
                "appium:udid": udid,
                "appium:bundleId": BUNDLE_ID,
                "appium:noReset": True,
                "appium:autoDismissAlerts": False,
                "appium:newCommandTimeout": 300,
                "appium:wdaLaunchTimeout": 180000,
                "appium:wdaConnectionTimeout": 120000,
            }
        }
    }
    print(f"  Creating session: {device_name} ({udid})...", flush=True)
    resp = http("POST", "/session", caps)
    sid = resp.get("value", {}).get("sessionId") or resp.get("sessionId")
    if not sid:
        raise RuntimeError(f"No sessionId in response: {resp}")
    print(f"  Session: {sid}", flush=True)
    return sid


def quit_session(sid):
    if not sid:
        return
    try:
        http("DELETE", f"/session/{sid}")
    except Exception as e:
        print(f"  (quit warning: {e})", flush=True)


def find(sid, using, value, timeout=10):
    deadline = time.time() + timeout
    last = "no result"
    while time.time() < deadline:
        try:
            resp = http("POST", f"/session/{sid}/element", {"using": using, "value": value})
            v = resp.get("value")
            if isinstance(v, dict):
                eid = v.get("element-6066-11e4-a52e-4f735466cecf") or v.get("ELEMENT")
                if eid:
                    return eid
        except Exception as e:
            last = str(e)[:100]
        time.sleep(0.4)
    raise RuntimeError(f"Element not found: {using}={value!r}")


def find_all(sid, using, value):
    resp = http("POST", f"/session/{sid}/elements", {"using": using, "value": value})
    out = []
    for v in resp.get("value", []) or []:
        eid = v.get("element-6066-11e4-a52e-4f735466cecf") or v.get("ELEMENT")
        if eid:
            out.append(eid)
    return out


def click(sid, eid):
    http("POST", f"/session/{sid}/element/{eid}/click", {})


def type_text(sid, eid, text):
    http("POST", f"/session/{sid}/element/{eid}/value", {"text": text})


def screenshot(udid, path):
    """Use simctl, NOT Appium screenshot, per gotchas."""
    subprocess.run(["xcrun", "simctl", "io", udid, "screenshot", path], check=True)
    print(f"  Captured: {path}", flush=True)


def goto_tab(sid, tab_name):
    """Tap a Shell tab by accessibility id."""
    eid = find(sid, "accessibility id", tab_name, timeout=10)
    click(sid, eid)
    time.sleep(1.2)


def wait(seconds):
    time.sleep(seconds)


def page_source(sid):
    return http("GET", f"/session/{sid}/source").get("value", "")


def execute(sid, script, args=None):
    return http("POST", f"/session/{sid}/execute/sync", {"script": script, "args": args or []})


# ---- Navigation per screen ----

def nav_home(sid):
    goto_tab(sid, "Home")


def nav_cards_with_expand(sid):
    """02-prayer-cards: Prayer Cards tab, sections expanded, one card expanded."""
    goto_tab(sid, "Prayer Cards")
    wait(2.0)
    # Clear any stuck tag-filter selection (chip in 'selected' state)
    try:
        sel = find(sid, "-ios predicate string",
                   "label ENDSWITH 'selected' AND NOT label CONTAINS 'not selected' AND type == 'XCUIElementTypeButton'",
                   timeout=2)
        click(sid, sel)
        wait(0.6)
        print("  cleared stuck tag-chip selection", flush=True)
    except Exception:
        pass
    # Step 1: ensure sections are expanded. Probe for any CARD (not tag-chip) visibility.
    # Card buttons contain "prayer" in their label (e.g. "Family & Health, 2 prayers, ...");
    # tag chips do not.
    def cards_visible():
        try:
            find(sid, "-ios predicate string",
                 "label CONTAINS 'prayer' AND type == 'XCUIElementTypeButton'", timeout=2)
            return True
        except Exception:
            return False
    if not cards_visible():
        # Tap Personal and Ministry section headers
        headers = find_all(sid, "accessibility id", "Cards_Section_Header")
        print(f"  found {len(headers)} section headers, expanding...", flush=True)
        for hid in headers:
            try:
                r = http("GET", f"/session/{sid}/element/{hid}/attribute/label")
                label = r.get("value", "")
                if label in ("Personal", "Ministry"):
                    click(sid, hid)
                    wait(0.8)
            except Exception:
                pass
        wait(1.5)
    else:
        print("  cards already visible (sections pre-expanded)", flush=True)
    # Step 2: tap a CARD (must contain "prayer" to disambiguate from tag chips)
    for card_label in ("Family", "Career"):
        for filt in (f"label CONTAINS '{card_label}' AND label CONTAINS 'Collapsed' AND label CONTAINS 'prayer' AND type == 'XCUIElementTypeButton'",
                     f"label CONTAINS '{card_label}' AND label CONTAINS 'prayer' AND type == 'XCUIElementTypeButton'"):
            try:
                eid = find(sid, "-ios predicate string", filt, timeout=4)
                click(sid, eid)
                wait(1.5)
                return
            except Exception as e:
                print(f"  card-tap try {card_label}: {str(e)[:100]}", flush=True)
                continue
    print("  warn: no expandable card found by name", flush=True)


def nav_prayer_detail(sid):
    """03-prayer-detail: tap 'Wisdom for the job transition'."""
    goto_tab(sid, "Prayers")
    wait(1.0)
    # Active filter assumed default; tap the prayer
    eid = find(sid, "-ios predicate string",
               "label CONTAINS 'Wisdom for the job'", timeout=10)
    click(sid, eid)
    wait(1.2)


def nav_prayer_list_active(sid):
    goto_tab(sid, "Prayers")
    wait(0.8)
    try:
        f = find(sid, "accessibility id", "List_Filter_Active", timeout=5)
        click(sid, f)
        wait(0.6)
    except Exception:
        pass


def nav_prayer_list_answered(sid):
    goto_tab(sid, "Prayers")
    wait(0.8)
    f = find(sid, "accessibility id", "List_Filter_Answered", timeout=5)
    click(sid, f)
    wait(0.8)


def nav_tags(sid):
    goto_tab(sid, "Tags")
    wait(1.0)


def nav_tag_detail(sid):
    """07-tag-detail: tap 'Family' tag → Edit → color picker page."""
    goto_tab(sid, "Tags")
    wait(1.0)
    # Tap Family tag row to expand inline actions
    eid = find(sid, "-ios predicate string", "label == 'Family'", timeout=8)
    click(sid, eid)
    wait(1.0)
    # Tap Edit button (now visible inline) — AutomationId Tags_Btn_Edit
    try:
        edit = find(sid, "accessibility id", "Tags_Btn_Edit", timeout=5)
    except Exception:
        edit = find(sid, "-ios predicate string",
                    "label CONTAINS 'Edit' AND label CONTAINS 'tag'", timeout=4)
    click(sid, edit)
    wait(1.5)
    # Dismiss keyboard if up. Try mobile: hideKeyboard, then fallback tap below input.
    try:
        execute(sid, "mobile: hideKeyboard", [{}])
        wait(0.5)
    except Exception:
        # Fallback: tap "View Prayers with this Tag" label area to defocus
        try:
            tgt = find(sid, "-ios predicate string",
                       "label == 'View Prayers with this Tag'", timeout=2)
            click(sid, tgt)
            wait(0.6)
        except Exception:
            pass


def nav_prayer_time(sid):
    """08-prayer-time: Home → Prayer Time → All Requests scope."""
    goto_tab(sid, "Home")
    wait(0.6)
    eid = find(sid, "accessibility id", "Home_Btn_PrayerTime", timeout=8)
    click(sid, eid)
    wait(1.5)
    # Scope picker may appear: tap Start (or 'All Requests')
    try:
        start = find(sid, "accessibility id", "Scope_Btn_Start", timeout=4)
        click(sid, start)
    except Exception:
        try:
            ar = find(sid, "-ios predicate string",
                      "label == 'All Requests'", timeout=4)
            click(sid, ar)
        except Exception:
            pass
    wait(2.0)


def nav_quick_add(sid):
    goto_tab(sid, "Home")
    wait(0.6)
    eid = find(sid, "accessibility id", "Home_Btn_QuickAdd", timeout=8)
    click(sid, eid)
    wait(1.5)


def nav_collections(sid):
    """10-manage-collections: Cards tab → More (...) overflow → Manage Collections."""
    goto_tab(sid, "Prayer Cards")
    wait(1.2)
    # Tap the More button (top-right ellipsis)
    try:
        more = find(sid, "-ios predicate string",
                    "label == 'More actions' OR name == 'More'", timeout=4)
        click(sid, more)
        wait(1.0)
    except Exception as e:
        print(f"  warn: More button not found: {e}", flush=True)
        return
    # Tap Manage Collections in popup (AutomationId='Collections')
    try:
        coll = find(sid, "accessibility id", "Collections", timeout=4)
        click(sid, coll)
        wait(1.5)
    except Exception as e:
        # Fallback: try by label
        try:
            coll = find(sid, "-ios predicate string",
                        "label == 'Manage Collections'", timeout=3)
            click(sid, coll)
            wait(1.5)
        except Exception:
            print(f"  warn: Manage Collections not found: {e}", flush=True)


def dismiss_system_alert(sid):
    """Best-effort: dismiss any iOS system alert (notifications, location, etc.)."""
    for _ in range(3):
        try:
            execute(sid, "mobile: alert", [{"action": "accept"}])
            wait(0.5)
        except Exception:
            break


def nav_confirm_import(sid):
    """11-confirm-import: Settings → Stage sample payload → ConfirmImport modal."""
    dismiss_system_alert(sid)
    goto_tab(sid, "Settings")
    wait(1.5)
    dismiss_system_alert(sid)
    # The button is in App Settings; need to navigate to it. Check what's on Settings root.
    # If button visible directly, tap. Else navigate to App Settings sub-page.
    try:
        btn = find(sid, "accessibility id", "AppSettings_Btn_StageSamplePayload", timeout=3)
        click(sid, btn)
        wait(2.0)
        return
    except Exception:
        pass
    # Navigate to App Settings sub-page
    try:
        eid = find(sid, "accessibility id", "Settings_Row_AppSettings", timeout=4)
        click(sid, eid)
        wait(1.5)
    except Exception as e:
        print(f"  warn: couldn't reach App Settings: {e}", flush=True)
        return
    # Dismiss any system permission alert (e.g., notifications)
    for _ in range(3):
        try:
            allow = find(sid, "-ios predicate string",
                         "(label == 'Allow' OR label == \"Don't Allow\") AND type == 'XCUIElementTypeButton'",
                         timeout=2)
            click(sid, allow)
            wait(0.8)
        except Exception:
            break
    # Scroll to find the button
    try:
        btn = find(sid, "accessibility id", "AppSettings_Btn_StageSamplePayload", timeout=4)
        click(sid, btn)
        wait(2.5)
    except Exception as e:
        execute(sid, "mobile: scroll", [{"direction": "down"}])
        wait(0.5)
        btn = find(sid, "accessibility id", "AppSettings_Btn_StageSamplePayload", timeout=4)
        click(sid, btn)
        wait(2.5)


def reset_to_home(sid):
    """Restart app via terminate + activate to clear modal state."""
    try:
        execute(sid, "mobile: terminateApp", [{"bundleId": BUNDLE_ID}])
        wait(0.6)
        execute(sid, "mobile: activateApp", [{"bundleId": BUNDLE_ID}])
        wait(2.0)
    except Exception as e:
        print(f"  reset_to_home warn: {e}", flush=True)


def main():
    if len(sys.argv) < 4:
        print("Usage: screenshot_nav.py <udid> <device_name> <out_dir> [theme=light|dark] [screens=2,3,4,5,6,7,8,9,10]")
        sys.exit(2)
    udid = sys.argv[1]
    device_name = sys.argv[2]
    out_dir = sys.argv[3]
    theme = sys.argv[4] if len(sys.argv) > 4 else "light"
    screens = sys.argv[5] if len(sys.argv) > 5 else "2,3,4,5,6,7,8,9,10"
    wanted = set(screens.split(","))

    # Set theme via simctl
    subprocess.run(["xcrun", "simctl", "ui", udid, "appearance", theme], check=True)
    wait(0.6)

    sid = create_session(udid, device_name)
    try:
        # ---- Captures ----
        if "2" in wanted:
            print("[02] Prayer Cards expanded", flush=True)
            reset_to_home(sid)
            nav_cards_with_expand(sid)
            screenshot(udid, f"{out_dir}/02-prayer-cards.png")

        if "3" in wanted:
            print("[03] Prayer Detail", flush=True)
            reset_to_home(sid)
            nav_prayer_detail(sid)
            screenshot(udid, f"{out_dir}/03-prayer-detail.png")

        if "4" in wanted:
            print("[04] Prayer List Active", flush=True)
            reset_to_home(sid)
            nav_prayer_list_active(sid)
            screenshot(udid, f"{out_dir}/04-prayer-list.png")

        if "5" in wanted:
            print("[05] Answered Prayers", flush=True)
            reset_to_home(sid)
            nav_prayer_list_answered(sid)
            screenshot(udid, f"{out_dir}/05-answered-prayers.png")

        if "6" in wanted:
            print("[06] Tags List", flush=True)
            reset_to_home(sid)
            nav_tags(sid)
            screenshot(udid, f"{out_dir}/06-tags-list.png")

        if "7" in wanted:
            print("[07] Tag Detail", flush=True)
            reset_to_home(sid)
            nav_tag_detail(sid)
            screenshot(udid, f"{out_dir}/07-tag-detail.png")

        if "8" in wanted:
            print("[08] Prayer Time", flush=True)
            reset_to_home(sid)
            nav_prayer_time(sid)
            screenshot(udid, f"{out_dir}/08-prayer-time.png")

        if "9" in wanted:
            print("[09] Quick Add", flush=True)
            reset_to_home(sid)
            nav_quick_add(sid)
            screenshot(udid, f"{out_dir}/09-quick-add.png")

        if "10" in wanted:
            print("[10] Collections", flush=True)
            reset_to_home(sid)
            nav_collections(sid)
            screenshot(udid, f"{out_dir}/10-manage-collections.png")

        if "11" in wanted:
            print("[11] Confirm Import", flush=True)
            reset_to_home(sid)
            nav_confirm_import(sid)
            screenshot(udid, f"{out_dir}/11-confirm-import.png")

    finally:
        quit_session(sid)


if __name__ == "__main__":
    main()
