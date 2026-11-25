// Lemon Squeezy License API calls. Works in Node 18+, Deno, Bun, Electron and browsers.
const LICENSE_API = 'https://licenses.exisone.com/v1/licenses';

async function call(action: 'activate' | 'validate' | 'deactivate', params: Record<string, string>) {
  const res = await fetch(`${LICENSE_API}/${action}`, {
    method: 'POST',
    headers: { Accept: 'application/json', 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams(params),
  });
  return res.json(); // failures carry an `error` message
}

export const activate = (licenseKey: string, instanceName: string) =>
  call('activate', { license_key: licenseKey, instance_name: instanceName });

export const validate = (licenseKey: string, instanceId?: string) =>
  call('validate', instanceId ? { license_key: licenseKey, instance_id: instanceId } : { license_key: licenseKey });

export const deactivate = (licenseKey: string, instanceId: string) =>
  call('deactivate', { license_key: licenseKey, instance_id: instanceId });
