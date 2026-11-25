# Contributing

Thanks for helping make these samples better. Bug reports, fixes, and new samples are welcome.

## Before you open an issue

- **Security problem?** Don't open an issue; see [SECURITY.md](SECURITY.md).
- **Question about your own ExisOne account or a license?** Use the
  [ExisOne dashboard](https://www.exisone.com) or email support; issues here are public.
- **SDK bug?** Open it here and say which SDK and version. The SDK source lives elsewhere; we'll move it.

## Pull requests

1. One sample or one fix per pull request.
2. Keep samples **minimal and runnable**. A sample shows one idea; anything else is noise.
3. Every sample must:
   - read its settings from environment variables or a git-ignored local config file, never from
     values committed to the repository;
   - keep working without a network connection inside the grace period (see any sample's
     `LicenseCache` / `license_cache` helper);
   - show clear messages to the user, never a stack trace;
   - use obviously fake product names (`Acme ...`);
   - have a README with: what it shows, prerequisites, run steps, and the lines that matter.
4. Only demonstrate features the published SDKs support today. Ideas for SDK features belong in an
   issue.
5. CI builds every sample (`.github/workflows/build.yml`). Make sure your sample builds there.
6. Never commit a real access token, license key, or customer data, even a test one.

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
