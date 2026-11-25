# Hardware-locked licenses: device binding, seat limits, moving machines

**Acme Render** (a pretend command-line app) shows what binding a license to hardware means for
you and your customers.

What it shows:
- **The hardware ID.** A stable fingerprint of this machine (`machine.py id`). Activation binds
  the key to it, and the same key on another machine is refused until a seat is free.
- **Seat limits.** A single-device key allows one machine. A corporate (multi-seat) key allows N,
  and `machine.py seats` shows how many are in use.
- **A full key.** Activation returns the server's seat-limit message, and the app tells the user
  how to free a seat.
- **Moving to a new machine.** `machine.py deactivate` on the old one frees its seat, or the
  customer does it from the customer portal if the old machine is gone.

## Prerequisites

- Python 3.9 or newer.
- An ExisOne product, a `verify` access token, and two keys: a normal key and a corporate key with
  2 seats. See [the 5-minute setup](../../README.md#5-minute-setup).

## Run

```bash
cd samples/hardware-locked
python -m venv .venv && .venv/bin/pip install -r requirements.txt   # Windows: .venv\Scripts\pip
cp .env.example .env            # then fill in EXISONE_ACCESS_TOKEN and EXISONE_PRODUCT
python machine.py id
python machine.py activate XXXX-XXXX-XXXX-XXXX you@example.com
python machine.py status
python machine.py seats
python machine.py deactivate
```

To see a seat limit on one computer, activate a 1-seat key here, then try it on a second
computer (or a VM): it is refused until you deactivate the first.

## The lines that matter

```python
lic.hardware_id                                   # licensing.py: client.generate_hardware_id(), stable per machine
ok, message = lic.activate(key, email)            # binds the key to this hardware ID (uses a seat)
u = lic.client.get_seat_usage(key)                # corporate keys: current_seats, max_seats, remaining
ok, message = lic.deactivate()                    # frees this machine's seat
```

## Notes

- The hardware ID is a hash of hardware properties (processor, board, disk and network adapter
  identifiers; virtual and VPN adapters are skipped). Replacing hardware can change it, and then
  the customer deactivates the old ID (portal) and activates again.
- On a server or in Docker, where many users share one machine, use a per-user ID instead (see
  [server-side](../server-side/)).
- `licensing.py` is the same file as in [python-app](../python-app/).

More: [Python SDK docs](https://www.exisone.com/docs-sdk-python.html) · [ExisOne](https://www.exisone.com)
