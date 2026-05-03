# ITAMS Documentation

This folder contains source-controlled documentation for ITAMS, the IT Asset
Management System hosted at `https://itams.app/`.

## Table Of Contents

- [Documentation Map](#documentation-map)
- [Production Quick Facts](#production-quick-facts)
- [Security Notes](#security-notes)
- [Common Validation Checks](#common-validation-checks)

## Documentation Map

| Document | Audience | Purpose |
| --- | --- | --- |
| [ITAMS Application Guide](./ITAMS-Application-Guide.md) | Application users, managers, support staff | Explains what the app does, the main workflows, roles, and login/session behavior. |
| [ITAMS Developer Guide](./ITAMS-Developer-Guide.md) | Developers | Explains the repo layout, local setup, configuration, API surface, tests, and troubleshooting. |
| [ITAMS Production Operations](./ITAMS-Production-Operations.md) | IT admins, server operators, release owners | Explains the IIS server, domain, certificate, Git auto-deploy flow, rollback, validation, and admin tasks. |
| [ITAMS Git Deployment](./ITAMS-Git-Deployment.md) | Developers, release owners | Short Git remote and deployment quick reference. |
| [ITAMS Setup Runbook](./ITAMS-Setup-Runbook.txt) | Developers | Original cross-machine local setup runbook. |

## Production Quick Facts

| Item | Value |
| --- | --- |
| Canonical URL | `https://itams.app/` |
| API URL | `https://itams.app/api/` |
| WWW behavior | `https://www.itams.app/` redirects to `https://itams.app/` |
| Public IP | `20.120.240.89` |
| IIS site | `ITAMS` |
| IIS app pools | `ITAMS.Site`, `ITAMS.Api` |
| Git remote | `jalen@itams.app:C:/Git/ITAMS.git` |
| Auto-deploy branch | `main` |

## Security Notes

- Do not commit MongoDB connection strings, JWT signing keys, bootstrap admin
  passwords, SSH private keys, certificate private keys, or exported `.pfx`
  files.
- Production secrets live outside the repo in
  `C:\ITAMS\Deploy\itams-production-env.json`.
- Development secrets should be configured with .NET user secrets for
  `ITAMS.Api`.
- Public documentation can include infrastructure names, domains, paths, and
  certificate thumbprints, but not credential values.

## Common Validation Checks

Run these after DNS, IIS, certificate, or deployment changes:

```powershell
curl.exe --silent --show-error --max-time 30 --output NUL --write-out "site=%{http_code}`n" https://itams.app/
curl.exe --silent --show-error --max-time 30 https://itams.app/appsettings.json
curl.exe --silent --show-error --max-time 30 --output NUL --write-out "api=%{http_code}`n" https://itams.app/api/auth/me
curl.exe --silent --show-error --max-time 30 --head https://www.itams.app/
```

Expected results:

- `https://itams.app/` returns `200`.
- `https://itams.app/appsettings.json` contains
  `{"ApiBaseUrl":"https://itams.app/api/"}`.
- `https://itams.app/api/auth/me` returns `401 Unauthorized` when no token is
  supplied.
- `https://www.itams.app/` redirects to `https://itams.app/`.
