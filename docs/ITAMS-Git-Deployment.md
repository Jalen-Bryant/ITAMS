# ITAMS Git Deployment

The server hosts a bare Git repository at `C:\Git\ITAMS.git`. Pushes to `main`
trigger the server-side deployment hook. Other branches are stored for version
control but do not deploy.

## Remote Setup

From a remote development machine:

```powershell
git clone jalen@20.120.240.89:C:/Git/ITAMS.git
cd ITAMS
```

If the remote key has not been installed yet, create one and send the public key
line to the server administrator:

```powershell
ssh-keygen -t ed25519 -C "itams-deploy"
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
```

The key must be added to `C:\ProgramData\ssh\administrators_authorized_keys`
because the `jalen` account is in the local Administrators group.

## Deployment Flow

1. Push to a branch for normal version control:

```powershell
git push origin feature/my-change
```

2. Push or merge to `main` to deploy:

```powershell
git push origin main
```

The hook checks out the pushed commit into a clean work area, runs restore,
build, tests, publishes the client and API, reapplies IIS deployment settings,
then switches the IIS site to the new release only after validation succeeds.

## Rollback

Previous releases are kept under `C:\inetpub\ITAMS\releases`. To roll back,
point the `ITAMS` site and `/api` application back to a previous release folder,
then recycle the `ITAMS.Site` and `ITAMS.Api` app pools.
