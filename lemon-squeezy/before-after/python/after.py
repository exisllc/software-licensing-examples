"""Lemon Squeezy License API calls."""
import requests

LICENSE_API = "https://licenses.exisone.com/v1/licenses"


def _call(action: str, params: dict) -> dict:
    r = requests.post(f"{LICENSE_API}/{action}", data=params, headers={"Accept": "application/json"}, timeout=10)
    return r.json()  # failures carry an "error" message


def activate(license_key: str, instance_name: str) -> dict:
    return _call("activate", {"license_key": license_key, "instance_name": instance_name})


def validate(license_key: str, instance_id: str | None = None) -> dict:
    params = {"license_key": license_key}
    if instance_id:
        params["instance_id"] = instance_id
    return _call("validate", params)


def deactivate(license_key: str, instance_id: str) -> dict:
    return _call("deactivate", {"license_key": license_key, "instance_id": instance_id})
