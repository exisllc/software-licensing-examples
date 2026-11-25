# Security

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

- Preferred: use **Report a vulnerability** on this repository's **Security** tab (GitHub private
  vulnerability reporting).
- Or email **exisllc@gmail.com** with "SECURITY" in the subject.

Include what you found, how to reproduce it, and what an attacker could do with it. We aim to
acknowledge reports within 3 business days and will keep you posted until it is fixed. We are happy
to credit you once a fix ships, if you want.

In scope: the samples and helpers in this repository, the ExisOne client SDKs
([NuGet](https://www.nuget.org/packages/ExisOne.Client), [npm](https://www.npmjs.com/package/exisone-client),
[PyPI](https://pypi.org/project/exisone-client/), [Swift](https://github.com/exisllc/ExisOne.Swift)), and
the ExisOne licensing API at exisone.com.

## How licensing secrets work in these samples

- **Signing happens on the server only.** ExisOne signs offline codes with a private key that never
  leaves the server. Apps only *verify* with the public key, which is safe to ship.
- **License keys cannot be generated in the app.** Only the server knows which keys are valid.
- **The access token in your app is public.** Anyone can extract it from an app you distribute. Give
  that token only the `verify` permission (plus `email` if the app sends support tickets). Never put a
  token with `generate` in an app you ship. ExisOne refuses account-management calls (other tokens,
  signing keys, payment and email settings, products) from API tokens, so a `verify` token can only
  validate licenses.
- **Never commit your configuration.** `.env` and `appsettings.Local.json` are git-ignored; the files
  in this repository contain placeholders only.

More: [ExisOne documentation](https://www.exisone.com/docs.html).
