This folder contains JSON Schema files describing Jaya's configuration models and the overall `settings.json` single-file configuration.

Files:
- `settings.json` - top-level schema for the single `settings.json` file used by the new configuration service.
- `application.json`, `pane.json`, `toolbar.json`, `update.json` - UI-related config models.
- `provider.json` - base provider config model (currently only `isEnabled`).
- `providers/*.json` - provider-specific schemas (filesystem, dropbox, googledrive, s3, ftp).
- `account.json`, `release.json`, `theme.json`, `directorySortSetting.json`, `filesystemObject.json` - shared model schemas.

Usage:
- Use `settings.json` as the main schema to validate the single-file configuration.
- Provider-specific sections are expected under `providers` as properties keyed by provider id or name.

Example snippet of `settings.json`:
```json
{
  "schemaVersion": 1,
  "application": {
    "isItemCheckBoxVisible": true,
    "themeName": "Dark"
  },
  "providers": {
    "FileSystemService": {
      "isEnabled": true,
      "isProtectedFileVisible": false
    }
  }
}
```

Notes:
- The schemas intentionally allow `additionalProperties` for some shared models where runtime objects (styles, assets) are complex; feel free to tighten these if stricter validation is desired.
- If you want me to generate example `settings.json` files for multiple scenarios (minimal, full, providers populate), tell me which ones to produce.
