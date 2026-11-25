// Acme Reports API: a pretend SaaS API whose endpoints need a licensed customer account.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import express from 'express';
import { LicenseGate } from './license-gate.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
try { process.loadEnvFile(path.join(here, '..', '.env')); } catch { /* use the environment */ }

const { EXISONE_BASE_URL = 'https://www.exisone.com', EXISONE_ACCESS_TOKEN, EXISONE_PRODUCT, PORT = 3000 } = process.env;
if (!EXISONE_ACCESS_TOKEN || !EXISONE_PRODUCT) {
  console.error('Set EXISONE_ACCESS_TOKEN and EXISONE_PRODUCT (copy .env.example to .env).');
  process.exit(2);
}

// In a real app: your database. Here: account id -> license key, in memory.
const licenseKeys = new Map();
const gate = new LicenseGate(licenseKeys, {
  baseUrl: EXISONE_BASE_URL, accessToken: EXISONE_ACCESS_TOKEN, product: EXISONE_PRODUCT, appVersion: '1.0.0',
});

const app = express();
app.use(express.json());

// Stand-in for your real authentication: the caller's account id comes from a header.
app.use((req, res, next) => {
  req.accountId = req.get('X-Account-Id');
  if (!req.accountId) return res.status(401).json({ error: 'unauthenticated', message: 'Send X-Account-Id.' });
  next();
});

app.post('/license/activate', async (req, res) => {
  const { key, email } = req.body ?? {};
  if (!key || !email) return res.status(400).json({ error: 'bad_request', message: 'key and email are required.' });
  const r = await gate.activate(req.accountId, key, email);
  res.status(r.ok ? 200 : 400).json(r);
});

app.post('/license/deactivate', async (req, res) => {
  try { res.json(await gate.deactivate(req.accountId)); }
  catch (err) { res.status(502).json({ ok: false, message: 'The license server refused or could not be reached.' }); }
});

app.get('/license', async (req, res) => res.json(await gate.check(req.accountId)));

// Licensed endpoints. The second one also needs a feature on the key.
app.get('/reports', gate.require(), (req, res) => res.json({ reports: ['sales-2026-q3', 'sales-2026-q4'] }));
app.get('/reports/export', gate.require('Export'), (req, res) => res.type('text/csv').send('month,sales\n2026-10,1200\n'));

app.listen(PORT, () => console.log(`Acme Reports API on http://localhost:${PORT}`));
