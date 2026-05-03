# ITAMS Production Operations

This guide documents the current production ITAMS server, deployment flow, and
recurring admin tasks. It intentionally includes hostnames, paths, and
thumbprints, but it must not include secret values.

## Table Of Contents

- [Production Topology](#production-topology)
- [Domain, DNS, And Certificate](#domain-dns-and-certificate)
- [IIS Runtime Settings](#iis-runtime-settings)
- [Production Configuration](#production-configuration)
- [Git Remote And Auto-Deploy](#git-remote-and-auto-deploy)
- [Deployment Smoke Tests](#deployment-smoke-tests)
- [Rollback](#rollback)
- [Reboot Validation](#reboot-validation)
- [Add SSH Access For A Developer](#add-ssh-access-for-a-developer)
- [Rotate Production Secrets](#rotate-production-secrets)
- [Renew Or Replace The SSL Certificate](#renew-or-replace-the-ssl-certificate)
- [Troubleshooting](#troubleshooting)
  - [Site Loads But Stays On The Launch Screen](#site-loads-but-stays-on-the-launch-screen)
  - [API Route Returns Blazor HTML](#api-route-returns-blazor-html)
  - [API Fails After Deployment](#api-fails-after-deployment)
  - [Push To Main Failed](#push-to-main-failed)
  - [DNS Or Certificate Problems](#dns-or-certificate-problems)

## Production Topology

| Item | Value |
| --- | --- |
| Canonical site | `https://itams.app/` |
| API base URL | `https://itams.app/api/` |
| WWW host | `https://www.itams.app/` redirects to `https://itams.app/` |
| Public IP | `20.120.240.89` |
| Source repo | `C:\ITAMS\ITAMS` |
| Bare Git repo | `C:\Git\ITAMS.git` |
| Deployment script | `C:\ITAMS\Deploy\Invoke-ITAMSDeployment.ps1` |
| Protected production env file | `C:\ITAMS\Deploy\itams-production-env.json` |
| IIS site | `ITAMS` |
| Static client app pool | `ITAMS.Site` |
| API child app | `/api` |
| API app pool | `ITAMS.Api` |
| Releases root | `C:\inetpub\ITAMS\releases` |
| Current release marker | `C:\inetpub\ITAMS\current-release.txt` |

The Blazor WebAssembly client is served by the root IIS site. The ASP.NET Core
API is hosted as the `/api` child application. IIS owns process lifetime, so the
site and API can recover after reboot without `dotnet watch`, a desktop session,
or a foreground terminal.

## Domain, DNS, And Certificate

Production DNS should be:

| DNS record | Value |
| --- | --- |
| `itams.app` A record | `20.120.240.89` |
| `www.itams.app` CNAME | `itams.app` |

The IIS site has host-specific bindings for `itams.app` and `www.itams.app`.
The public certificate is installed in the Local Machine Personal certificate
store.

| Certificate detail | Value |
| --- | --- |
| Thumbprint | `6CA76FE166B9CFC69864E15AEA99913FBBEDBEC6` |
| SANs | `itams.app`, `www.itams.app` |
| Store | `Cert:\LocalMachine\My` |

The old public IP HTTPS binding remains for compatibility and redirects to the
canonical domain. Browsers can still show a certificate warning when browsing
directly to `https://20.120.240.89/`, because certificate validation happens
before IIS can issue the redirect.

Check bindings:

```powershell
Import-Module WebAdministration
Get-WebBinding -Name "ITAMS" |
    Select-Object protocol,bindingInformation,certificateHash,sslFlags |
    Format-Table -AutoSize
```

Expected bindings include:

- `http *:80:itams.app`
- `http *:80:www.itams.app`
- `https *:443:itams.app` with SNI and cert
  `6CA76FE166B9CFC69864E15AEA99913FBBEDBEC6`
- `https *:443:www.itams.app` with SNI and cert
  `6CA76FE166B9CFC69864E15AEA99913FBBEDBEC6`

## IIS Runtime Settings

The API app pool should be configured for ASP.NET Core Module hosting:

```powershell
Import-Module WebAdministration
Get-Item "IIS:\AppPools\ITAMS.Api" |
    Select-Object name,state,managedRuntimeVersion,startMode,
        @{Name="IdleTimeout";Expression={$_.processModel.idleTimeout}}
```

Expected API app pool values:

- `.NET CLR Version`: No Managed Code
- `startMode`: `AlwaysRunning`
- `idleTimeout`: `00:00:00`

The `/api` IIS application should have `preloadEnabled=true`. The Windows
services `W3SVC` and `WAS` should start automatically.

Check site and API paths:

```powershell
Import-Module WebAdministration
Get-Website -Name "ITAMS" | Select-Object Name,State,PhysicalPath,applicationPool
Get-WebApplication -Site "ITAMS" | Select-Object Path,PhysicalPath,applicationPool
```

## Production Configuration

Production API settings are injected into the published API `web.config` during
deployment from:

```text
C:\ITAMS\Deploy\itams-production-env.json
```

The file must be ACL-protected and must stay out of Git. Required values:

```json
{
  "ASPNETCORE_ENVIRONMENT": "Production",
  "MongoDb__ConnectionString": "<MONGODB_CONNECTION_STRING>",
  "Jwt__SigningKey": "<JWT_SIGNING_KEY_AT_LEAST_32_CHARACTERS>",
  "Jwt__AccessTokenMinutes": "15",
  "Jwt__RefreshTokenHours": "4",
  "BootstrapAdmin__Username": "<BOOTSTRAP_ADMIN_USERNAME>",
  "BootstrapAdmin__DisplayName": "<BOOTSTRAP_ADMIN_DISPLAY_NAME>",
  "BootstrapAdmin__Email": "<BOOTSTRAP_ADMIN_EMAIL>",
  "BootstrapAdmin__Department": "<BOOTSTRAP_ADMIN_DEPARTMENT>",
  "BootstrapAdmin__Password": "<BOOTSTRAP_ADMIN_PASSWORD>",
  "Cors__AllowedOrigins__0": "https://itams.app",
  "Cors__AllowedOrigins__1": "https://www.itams.app",
  "Security__ContentSecurityPolicyReportOnly": "false"
}
```

Production CORS and CSP are intentionally restricted to `https://itams.app` and
`https://www.itams.app`. Localhost origins belong only in development settings.

Never paste real values from this file into documentation, Git commits, tickets,
or chat logs.

## Git Remote And Auto-Deploy

The IIS server hosts a bare Git repo:

```text
C:\Git\ITAMS.git
```

Preferred remote URL:

```powershell
git clone jalen@itams.app:C:/Git/ITAMS.git
```

Fallback remote URL:

```powershell
git clone jalen@20.120.240.89:C:/Git/ITAMS.git
```

The server-side `post-receive` hook deploys only `refs/heads/main`. Other
branches are accepted for version control and do not deploy.

The source checkout at `C:\ITAMS\ITAMS` is not the live site. A local commit in
that checkout only updates Git history; it does not publish files under IIS.
After committing on the production server, push the commit into the bare repo so
the normal deployment hook runs:

```powershell
git -C C:\ITAMS\ITAMS status --short
git -C C:\ITAMS\ITAMS log --oneline -1
git -C C:\ITAMS\ITAMS push C:/Git/ITAMS.git main
```

From a normal developer workstation, push to `origin main` instead. Do not copy
files directly into `C:\inetpub\ITAMS\releases`; that bypasses validation,
release tracking, app-pool recycling, and smoke tests.

On a successful push to `main`, the hook:

1. Checks out the pushed commit into a clean work area.
2. Runs `dotnet restore`.
3. Runs `dotnet build -c Release`.
4. Runs API tests.
5. Publishes the Blazor client and API into a versioned release folder.
6. Writes production `appsettings.json` with
   `https://itams.app/api/`.
7. Reapplies IIS rewrite rules, API environment variables, and file ACLs.
8. Switches the IIS site and `/api` application to the new release.
9. Recycles `ITAMS.Api` and `ITAMS.Site`.
10. Smoke-tests the live site.
11. Leaves IIS on the previous release if validation fails.

## Deployment Smoke Tests

Run these after deployment:

```powershell
curl.exe --silent --show-error --max-time 30 --output NUL --write-out "site=%{http_code}`n" https://itams.app/
curl.exe --silent --show-error --max-time 30 https://itams.app/appsettings.json
curl.exe --silent --show-error --max-time 30 --output NUL --write-out "api=%{http_code}`n" https://itams.app/api/auth/me
curl.exe --silent --show-error --max-time 30 --head https://www.itams.app/
curl.exe -k --silent --show-error --max-time 30 --head https://20.120.240.89/
```

Expected results:

- `https://itams.app/` returns `200`.
- `https://itams.app/appsettings.json` returns
  `{"ApiBaseUrl":"https://itams.app/api/"}`.
- `https://itams.app/api/auth/me` returns `401 Unauthorized`.
- `https://www.itams.app/` returns a `301` redirect to `https://itams.app/`.
- `https://20.120.240.89/` returns a redirect to `https://itams.app/` when
  certificate warnings are ignored by `curl -k`.

## Rollback

Previous releases are kept under:

```text
C:\inetpub\ITAMS\releases
```

To roll back:

1. Pick the previous release folder.
2. Point the `ITAMS` site to that release's `site` folder.
3. Point the `/api` application to that release's `api` folder.
4. Recycle both app pools.
5. Run the smoke tests.
6. Update `C:\inetpub\ITAMS\current-release.txt` only after confirming the
   rollback works.

Example:

```powershell
$release = "C:\inetpub\ITAMS\releases\<COMMIT_SHORT_SHA>"
Import-Module WebAdministration
Set-ItemProperty "IIS:\Sites\ITAMS" -Name physicalPath -Value "$release\site"
Set-ItemProperty "IIS:\Sites\ITAMS\api" -Name physicalPath -Value "$release\api"
Restart-WebAppPool -Name "ITAMS.Api"
Restart-WebAppPool -Name "ITAMS.Site"
```

## Reboot Validation

After a server reboot:

```powershell
Get-Service W3SVC,WAS | Select-Object Name,Status,StartType
Import-Module WebAdministration
Get-Website -Name "ITAMS" | Select-Object Name,State,PhysicalPath
Get-WebAppPoolState -Name "ITAMS.Api"
Get-WebAppPoolState -Name "ITAMS.Site"
```

Then run the deployment smoke tests. The API should respond at
`https://itams.app/api/auth/me` with `401 Unauthorized`, not Blazor HTML.

## Add SSH Access For A Developer

Use SSH key authentication. Do not enable password-based Git deployment unless a
separate security review approves it.

1. Ask the developer for their Ed25519 public key line:

   ```text
   ssh-ed25519 <PUBLIC_KEY> <COMMENT>
   ```

2. Add the public key to:

   ```text
   C:\ProgramData\ssh\administrators_authorized_keys
   ```

3. Preserve strict ACLs on the file. It should be readable by SYSTEM and
   Administrators only.
4. Ask the developer to test:

   ```powershell
   ssh jalen@itams.app "git --version"
   git clone jalen@itams.app:C:/Git/ITAMS.git
   ```

Because `jalen` is an administrator account, Windows OpenSSH uses
`administrators_authorized_keys` instead of the user's profile
`authorized_keys` file.

## Rotate Production Secrets

1. Update the source secret system first, such as MongoDB Atlas or the password
   vault.
2. Edit `C:\ITAMS\Deploy\itams-production-env.json` on the server.
3. Keep the same JSON key names.
4. Do not print the file contents to shared logs or screenshots.
5. Re-run deployment or recycle the API after the published `web.config`
   receives the new values.
6. Run the smoke tests and sign in with a known account.

If rotating `Jwt__SigningKey`, expect existing sessions to become invalid.

## Renew Or Replace The SSL Certificate

1. Install the renewed certificate, including private key, into
   `Cert:\LocalMachine\My`.
2. Confirm SANs include `itams.app` and `www.itams.app`.
3. Rebind the `itams.app` and `www.itams.app` HTTPS bindings to the new
   certificate thumbprint.
4. Keep SNI enabled on both host-specific HTTPS bindings.
5. Run the domain smoke tests.
6. Update this document if the certificate thumbprint changes.

Certificate validation command:

```powershell
Get-ChildItem Cert:\LocalMachine\My |
    Where-Object { $_.Subject -like "*itams.app*" -or $_.DnsNameList -match "itams.app" } |
    Select-Object Subject,Thumbprint,NotAfter,HasPrivateKey
```

## Troubleshooting

### Site Loads But Stays On The Launch Screen

Confirm the client was published normally, not with a stale Blazor fingerprint
placeholder. The deployment script should run client publish without
`--no-build`.

### API Route Returns Blazor HTML

Check the root `web.config` rewrite rules. `/api` must be excluded from Blazor
fallback rewrite rules.

### API Fails After Deployment

Check:

- `ITAMS.Api` app pool state.
- Windows Event Viewer.
- API `web.config` environment variables.
- MongoDB connectivity.
- `C:\inetpub\ITAMS\releases\<COMMIT>\api\logs` if runtime logs are present.

### Push To Main Failed

Read the remote hook output from `git push`. If validation failed, the Git ref
may have updated, but IIS should remain on the previous working release.
Fix the issue and push a new commit to `main`.

### DNS Or Certificate Problems

Confirm DNS resolves to `20.120.240.89`:

```powershell
Resolve-DnsName itams.app -Type A
Resolve-DnsName www.itams.app
```

Confirm HTTPS cert selection:

```powershell
curl.exe --silent --show-error --max-time 30 --head https://itams.app/
curl.exe --silent --show-error --max-time 30 --head https://www.itams.app/
```
