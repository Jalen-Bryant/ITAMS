# ITAMS Git Deployment

This is the quick reference for Git access and deployment. For full production
operations, rollback, DNS, certificate, and IIS details, see
[ITAMS Production Operations](./ITAMS-Production-Operations.md).

## Table Of Contents

- [Remote Setup](#remote-setup)
- [Commit Changes](#commit-changes)
- [Deployment Flow](#deployment-flow)
- [Post-Deploy Checks](#post-deploy-checks)
- [Rollback](#rollback)

## Remote Setup

Preferred clone URL:

```powershell
git clone jalen@itams.app:C:/Git/ITAMS.git
cd ITAMS
```

Fallback clone URL if DNS or SSH to the domain is unavailable:

```powershell
git clone jalen@20.120.240.89:C:/Git/ITAMS.git
cd ITAMS
```

If a remote key has not been installed yet, create one and send the public key
line to the server administrator:

```powershell
ssh-keygen -t ed25519 -C "itams-deploy"
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
```

The key must be added to `C:\ProgramData\ssh\administrators_authorized_keys`
because the `jalen` account is in the local Administrators group.

## Commit Changes

Before deploying, commit the intended source changes from the repository root:

```powershell
git status --short
git diff -- <PATH_TO_REVIEW>
git add -- <PATH_TO_COMMIT>
git commit -m "Describe the change"
```

For documentation-only updates, add the specific files under `docs`, for
example:

```powershell
git add -- docs/ITAMS-Git-Deployment.md docs/ITAMS-Production-Operations.md
git commit -m "Update deployment documentation"
```

Use path-specific `git add -- <PATH>` commands instead of staging everything
when the worktree may contain unrelated edits. Confirm the commit before
pushing:

```powershell
git status --short
git log --oneline -1
```

A commit by itself does not deploy the site. It must be pushed or merged to
`main` so the server-side deployment hook can run.

## Deployment Flow

Push to a branch for normal version control:

```powershell
git push origin feature/my-change
```

Push or merge to `main` to deploy:

```powershell
git push origin main
```

The server-side hook deploys only `refs/heads/main`. It runs restore, Release
build, API tests, client/API publish, IIS setting reapplication, release switch,
app-pool recycle, and smoke tests. Other branches are stored for version control
but do not deploy.

When working directly in the production source checkout at `C:\ITAMS\ITAMS`,
the checkout is still not the live site. After committing there, push to the
local bare repository to trigger the same `post-receive` deployment hook:

```powershell
git push C:/Git/ITAMS.git main
```

Use the local bare-repo push only on the production server. From normal
developer machines, use `git push origin main`.

## Post-Deploy Checks

```powershell
curl.exe --silent --show-error --max-time 30 --output NUL --write-out "site=%{http_code}`n" https://itams.app/
curl.exe --silent --show-error --max-time 30 https://itams.app/appsettings.json
curl.exe --silent --show-error --max-time 30 --output NUL --write-out "api=%{http_code}`n" https://itams.app/api/auth/me
curl.exe --silent --show-error --max-time 30 --head https://www.itams.app/
```

Expected results:

- Site root returns `200`.
- `appsettings.json` points to `https://itams.app/api/`.
- `/api/auth/me` returns `401 Unauthorized` without a token.
- `www.itams.app` redirects to `https://itams.app/`.

## Rollback

Previous releases are kept under `C:\inetpub\ITAMS\releases`. To roll back,
point the `ITAMS` site and `/api` application back to a previous release folder,
then recycle the `ITAMS.Site` and `ITAMS.Api` app pools. Detailed rollback steps
are in [ITAMS Production Operations](./ITAMS-Production-Operations.md).
