"""Licensing for a Python app: start-up check, activation, deactivation, offline grace period.

This file is identical in samples/python-app and samples/hardware-locked. Rules:
  - the server decides; ask it on every start;
  - a network problem never locks out a user who was licensed within the last grace_days days;
  - every outcome comes with a message you can show as-is.
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Optional

import requests
from exisone import ExisOneClient, ExisOneClientOptions


@dataclass
class Settings:
    base_url: str
    access_token: str          # verify-only token: it ships in the app, so it is public
    product: str
    public_key: Optional[str]  # PEM, only for offline codes
    buy_url: str
    grace_days: int


def load_settings(env_file: Path) -> Settings:
    """EXISONE_* environment variables, or a git-ignored .env file (environment wins)."""
    values = {}
    if env_file.exists():
        for line in env_file.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                k, v = line.split("=", 1)
                values[k.strip()] = v.strip()
    get = lambda k, default="": os.environ.get(k) or values.get(k) or default
    s = Settings(
        base_url=get("EXISONE_BASE_URL", "https://www.exisone.com"),
        access_token=get("EXISONE_ACCESS_TOKEN"),
        product=get("EXISONE_PRODUCT"),
        public_key=get("EXISONE_PUBLIC_KEY") or None,
        buy_url=get("EXISONE_BUY_URL"),
        grace_days=int(get("EXISONE_GRACE_DAYS", "7")),
    )
    if not s.access_token:
        raise ValueError("EXISONE_ACCESS_TOKEN is not set. Copy .env.example to .env and fill it in.")
    if not s.product:
        raise ValueError("EXISONE_PRODUCT is not set.")
    return s


@dataclass
class LicenseState:
    kind: str  # licensed | trial | trial-ended | expired | not-licensed | update-required | needs-connection
    message: str
    expires_at: Optional[datetime] = None  # UTC
    features: list[str] = field(default_factory=list)
    update_available: Optional[str] = None
    offline: bool = False

    @property
    def can_use(self) -> bool:
        return self.kind in ("licensed", "trial")


def _utcnow() -> datetime:
    return datetime.now(timezone.utc).replace(tzinfo=None)  # the SDK returns naive UTC datetimes


class LicenseManager:
    def __init__(self, settings: Settings, app_version: str, cache_file: Path):
        self.settings = settings
        self.app_version = app_version
        self.cache_file = cache_file
        self.client = ExisOneClient(ExisOneClientOptions(
            base_url=settings.base_url,
            access_token=settings.access_token,
            offline_public_key=settings.public_key,
            timeout=10,
        ))
        self.hardware_id = self.client.generate_hardware_id()  # stable per machine

    @property
    def stored_key(self) -> Optional[str]:
        return (self._load() or {}).get("activation_key")

    # ---- start-up check ------------------------------------------------------------------------

    def check(self) -> LicenseState:
        cached = self._load()
        if cached and cached.get("hardware_id") != self.hardware_id:
            cached = None  # copied from another machine
        key = (cached or {}).get("activation_key")
        try:
            # An empty key is a trial check: the server starts (or continues) this machine's trial.
            r = self.client.validate(key or "", self.hardware_id, self.settings.product, self.app_version)
        except requests.RequestException as e:
            status = e.response.status_code if getattr(e, "response", None) is not None else None
            return self._from_cache(cached, status)  # network down, or the server refused the token
        self._save({
            "activation_key": key, "email": (cached or {}).get("email"), "status": r.status,
            "expires_at": r.expiration_date.isoformat() if r.expiration_date else None,
            "features": r.features, "last_validated_utc": _utcnow().isoformat(), "hardware_id": self.hardware_id,
        })
        return self._from_server(r, key)

    def _from_server(self, r, key) -> LicenseState:
        exp = r.expiration_date
        update = r.server_version if _is_newer(r.server_version, self.app_version) else None
        common = dict(expires_at=exp, features=list(r.features or []), update_available=update)
        if r.status == "licensed":
            msg = f"Licensed until {exp:%Y-%m-%d}." if exp and exp.year < 9999 else "Licensed."
            return LicenseState("licensed", msg, **common)
        if r.status == "trial":
            days = max(0, (exp - _utcnow()).days + 1) if exp else 0
            return LicenseState("trial", f"Trial: {days} day(s) left.", **common)
        if r.status == "expired":
            if key:
                return LicenseState("expired", f"Your license expired on {exp:%Y-%m-%d}. Renew it to keep going.", **common)
            return LicenseState("trial-ended", "Your trial has ended. Buy a license to keep going.", **common)
        if r.status == "version_outdated":
            return LicenseState("update-required",
                                f"This version is no longer supported. Please update to {r.minimum_required_version} or later.",
                                update_available=r.server_version)
        if key:
            return LicenseState("not-licensed", "This license is not active on this computer. Activate it again, or contact support.")
        return LicenseState("not-licensed", "Enter a license key to continue.")

    def _from_cache(self, c, http_status) -> LicenseState:
        now = _utcnow()
        last = datetime.fromisoformat(c["last_validated_utc"]) if c and c.get("last_validated_utc") else None
        exp = datetime.fromisoformat(c["expires_at"]) if c and c.get("expires_at") else None
        usable = (c and c.get("status") in ("licensed", "trial") and last
                  and now - last < timedelta(days=self.settings.grace_days)
                  and (exp is None or exp > now))
        if not usable:
            if http_status in (401, 403):
                return LicenseState("needs-connection", "The license server refused this app's access token. Please contact support.")
            return LicenseState("needs-connection", "Can't reach the license server. Connect to the internet and try again.")
        until = last + timedelta(days=self.settings.grace_days)
        kind = "trial" if c["status"] == "trial" else "licensed"
        return LicenseState(kind, f"Working offline. Connect to the internet before {until:%Y-%m-%d} to keep going.",
                            expires_at=exp, features=c.get("features") or [], offline=True)

    # ---- activation ----------------------------------------------------------------------------

    def activate(self, key: str, email: str) -> tuple[bool, str]:
        key, email = key.strip(), email.strip()
        r = self.client.activate(key, email, self.hardware_id, self.settings.product, self.app_version)
        if not r.success:  # the SDK returns network errors as success=False instead of raising
            if r.error_code == "version_outdated":
                return False, f"This version is too old to activate. Please update to {r.minimum_required_version} or later."
            if not r.error_code and (r.error_message or "").startswith("Network error"):
                return False, "Can't reach the license server. Activation needs an internet connection."
            return False, r.error_code or r.error_message or "Activation failed. Check the key and try again."
        self._save({"activation_key": key, "email": email, "status": "licensed", "hardware_id": self.hardware_id})
        return True, "Activated. Thank you!"

    def deactivate(self) -> tuple[bool, str]:
        """Frees this machine's seat so the key can be activated elsewhere."""
        key = self.stored_key
        if not key:
            return False, "No license is activated on this computer."
        try:
            self.client.deactivate(key, self.hardware_id, self.settings.product)
        except requests.RequestException as e:
            if getattr(e, "response", None) is None:
                return False, "Can't reach the license server. Deactivation needs an internet connection."
            return False, "The license server refused the deactivation. Please contact support."
        self.cache_file.unlink(missing_ok=True)
        return True, "This computer is deactivated. You can now activate the key on another computer."

    # The cache is a convenience for offline use, not copy protection: the server is asked again
    # on every start, so editing this file gains a user at most the grace period.
    def _load(self) -> Optional[dict]:
        try:
            return json.loads(self.cache_file.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return None

    def _save(self, value: dict) -> None:
        try:
            self.cache_file.parent.mkdir(parents=True, exist_ok=True)
            self.cache_file.write_text(json.dumps(value, indent=2), encoding="utf-8")
        except OSError:
            pass  # never fail the app because the cache can't be written


def _is_newer(candidate: Optional[str], current: str) -> bool:
    try:
        a = [int(x) for x in (candidate or "").split(".")]
        b = [int(x) for x in current.split(".")]
    except ValueError:
        return False
    n = max(len(a), len(b))
    return (a + [0] * (n - len(a))) > (b + [0] * (n - len(b)))


def user_data_dir(app_name: str) -> Path:
    """Per-user app-data folder (APPDATA on Windows, ~/Library/Application Support on macOS, ~/.config on Linux)."""
    if os.name == "nt":
        base = Path(os.environ.get("APPDATA", Path.home()))
    elif os.uname().sysname == "Darwin":
        base = Path.home() / "Library" / "Application Support"
    else:
        base = Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config"))
    return base / app_name
