# Desktop app (Python / Tkinter): trial → buy → activate → validate on start

**Acme Notes** in Python: a pretend note editor that only works with a license or an active trial.
Same flow as the [.NET desktop sample](../desktop-dotnet/):
- trial on first run, Buy / Renew, activate with key + email;
- validate on every start;
- offline grace period (7 days) with a clear message;
- version check;
- deactivate to move to another computer.

Only the standard library plus the `exisone-client` package. Network calls run on a worker
thread, so the window never freezes.

## Prerequisites

- Python 3.9 or newer with Tkinter (included in the python.org installers; on Linux, install
  `python3-tk`).
- An ExisOne product, a `verify` access token and a test key: see
  [the 5-minute setup](../../README.md#5-minute-setup).

## Run

```bash
cd samples/python-app
python -m venv .venv
.venv/bin/pip install -r requirements.txt      # Windows: .venv\Scripts\pip ...
cp .env.example .env                           # then fill in EXISONE_ACCESS_TOKEN and EXISONE_PRODUCT
.venv/bin/python app.py                        # Windows: .venv\Scripts\python app.py
```

## The lines that matter

[`licensing.py`](licensing.py) (`LicenseManager.check`):

```python
try:
    # An empty key is a trial check: the server starts (or continues) this machine's trial.
    r = self.client.validate(key or "", self.hardware_id, self.settings.product, self.app_version)
except requests.RequestException as e:
    status = e.response.status_code if getattr(e, "response", None) is not None else None
    return self._from_cache(cached, status)   # offline: last good answer inside the grace period
self._save({...})                             # status, expiry, features, time of this answer
return self._from_server(r, key)
```

`licensing.py` has no Tkinter code; use it as-is in a Qt, wx, or command-line app (the
[hardware-locked](../hardware-locked/) sample does).

## Notes

- The access token ships with your app, so treat it as public and give it only `verify`.
- The cache is a convenience for offline use, not copy protection: the server is asked on every
  start and the cache is ignored on another machine.

More: [Python SDK docs](https://www.exisone.com/docs-sdk-python.html) · [ExisOne](https://www.exisone.com)
