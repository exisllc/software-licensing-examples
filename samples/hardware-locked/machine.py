"""Acme Render: device binding and seat limits from the command line.

  python machine.py id                         this machine's hardware ID
  python machine.py activate <key> <email>     bind the key to this machine (uses one seat)
  python machine.py status                     is this machine licensed?
  python machine.py seats [<key>]              seats used / allowed on a key
  python machine.py deactivate                 free this machine's seat (to move to a new machine)
"""
import sys
from pathlib import Path

import requests

from licensing import LicenseManager, load_settings, user_data_dir

APP_VERSION = "1.0.0"


def main(argv: list[str]) -> int:
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__)
        return 0
    try:
        settings = load_settings(Path(__file__).parent / ".env")
    except ValueError as e:
        print(f"Configuration problem: {e}", file=sys.stderr)
        return 2
    lic = LicenseManager(settings, APP_VERSION, user_data_dir("AcmeRender") / "license-cache.json")
    command, args = argv[0], argv[1:]

    if command == "id":
        # The ID the key is bound to. Stable across restarts; changes if the hardware changes.
        # Users read it out when they need an offline code (see samples/offline-activation).
        print(lic.hardware_id)
        return 0

    if command == "activate":
        if len(args) != 2:
            print("Usage: python machine.py activate <key> <email>", file=sys.stderr)
            return 2
        ok, message = lic.activate(args[0], args[1])
        print(message)
        if not ok:
            # A full key reports the server's seat-limit message. Show the user how to free a seat.
            print("If this key is in use on another machine, run 'python machine.py deactivate' there first,\n"
                  "or deactivate the old machine from the customer portal.", file=sys.stderr)
        return 0 if ok else 1

    if command == "status":
        s = lic.check()
        print(s.message)
        if s.can_use and lic.stored_key:
            print(f"Bound to this machine: {lic.hardware_id[:12]}…  key {mask(lic.stored_key)}")
        return 0 if s.can_use else 1

    if command == "seats":
        key = args[0] if args else lic.stored_key
        if not key:
            print("Usage: python machine.py seats <key>", file=sys.stderr)
            return 2
        try:
            u = lic.client.get_seat_usage(key)
        except requests.RequestException as e:
            gone = getattr(e, "response", None) is None
            print("Can't reach the license server." if gone else "The license server refused the request.", file=sys.stderr)
            return 1
        if u.is_unlimited and u.current_seats == 0:
            # Seat counts exist for multi-seat (corporate) keys; a single-device key reports nothing here.
            print("No seat information: this is a single-device key, or a multi-seat key with no machines yet.")
        elif u.is_unlimited:
            print(f"{u.current_seats} machine(s) active, no limit.")
        else:
            print(f"{u.current_seats} of {u.max_seats} machine(s) active, {u.remaining} free.")
            if not u.has_capacity:
                print("The key is full: deactivate a machine before activating a new one.")
        return 0

    if command == "deactivate":
        ok, message = lic.deactivate()
        print(message)
        return 0 if ok else 1

    print(__doc__)
    return 2


def mask(key: str) -> str:
    return key[:4] + "…" + key[-4:] if len(key) > 8 else "…"


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
