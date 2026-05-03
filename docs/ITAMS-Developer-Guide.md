# ITAMS Developer Guide

This guide explains how to work on ITAMS locally and how the app is structured.
Production operations and deployment are covered in
[ITAMS Production Operations](./ITAMS-Production-Operations.md).

## Table Of Contents

- [Repository Layout](#repository-layout)
- [Local Prerequisites](#local-prerequisites)
- [Local Configuration](#local-configuration)
- [Build, Test, And Run](#build-test-and-run)
- [API Surface](#api-surface)
  - [Role Policy Summary](#role-policy-summary)
  - [Authorization Maintenance Checklist](#authorization-maintenance-checklist)
  - [Authorization Test Gaps](#authorization-test-gaps)
- [Frontend Notes](#frontend-notes)
- [Development Workflow](#development-workflow)
- [Troubleshooting](#troubleshooting)
  - [MongoDB Connection Failures](#mongodb-connection-failures)
  - [JWT Configuration Errors](#jwt-configuration-errors)
  - [CORS Failures](#cors-failures)
  - [Login Or Stale Session Problems](#login-or-stale-session-problems)
  - [Bootstrap Admin Was Not Created](#bootstrap-admin-was-not-created)
  - [Ports Already In Use](#ports-already-in-use)

## Repository Layout

| Path | Purpose |
| --- | --- |
| `ITAMS.Api` | ASP.NET Core minimal API backed by MongoDB. |
| `frontend\ITAMS.Client` | Blazor WebAssembly frontend. |
| `ITAMS.Api.Tests` | API and integration tests. |
| `scripts` | Deployment and utility scripts. |
| `docs` | Source-controlled documentation. |
| `ITAMS.sln` | Solution file for restore, build, and test. |

## Local Prerequisites

- Windows PowerShell.
- .NET 10 SDK.
- Git.
- Access to a MongoDB Atlas cluster or another reachable MongoDB instance.
- A modern browser.
- Optional: Visual Studio 2022 or Visual Studio Code.

Trust the local development HTTPS certificate if needed:

```powershell
dotnet dev-certs https --trust
```

## Local Configuration

The API reads normal `appsettings.json` values plus machine-specific secrets.
Do not put live secrets in source-controlled files.

Configure local secrets from the repository root:

```powershell
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "MongoDb:ConnectionString" "<MONGODB_CONNECTION_STRING>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "Jwt:SigningKey" "<JWT_SIGNING_KEY_AT_LEAST_32_CHARACTERS>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Username" "<BOOTSTRAP_ADMIN_USERNAME>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:DisplayName" "<BOOTSTRAP_ADMIN_DISPLAY_NAME>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Email" "<BOOTSTRAP_ADMIN_EMAIL>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Department" "<BOOTSTRAP_ADMIN_DEPARTMENT>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Password" "<BOOTSTRAP_ADMIN_PASSWORD>"
```

Important configuration notes:

- `Jwt:SigningKey` must be at least 32 characters.
- `MongoDb:ConnectionString` must point to a database the machine can reach.
- `BootstrapAdmin` creates the first login-capable admin only when no
  login-capable users already exist.
- The default local database name is `itams-dev`.
- The MongoDB collections are `assets`, `assignments`, `auditLogs`,
  `lifecycleEvents`, `users`, and `userSessions`.
- The Blazor client reads `ApiBaseUrl` from
  `frontend\ITAMS.Client\wwwroot\appsettings.json`.
- In local development, the client falls back to `https://localhost:7004/` if
  `ApiBaseUrl` is missing.

## Build, Test, And Run

Restore packages:

```powershell
dotnet restore .\ITAMS.sln
```

Build the solution:

```powershell
dotnet build .\ITAMS.sln -c Debug
```

Run API tests:

```powershell
dotnet test .\ITAMS.Api.Tests\ITAMS.Api.Tests.csproj -c Release
```

Start the API:

```powershell
dotnet watch --project .\ITAMS.Api\ITAMS.Api.csproj run --launch-profile https
```

Start the client in another terminal:

```powershell
dotnet watch --project .\frontend\ITAMS.Client\ITAMS.Client.csproj run --launch-profile https
```

Default development URLs:

- API: `https://localhost:7004`
- Swagger: `https://localhost:7004/swagger`
- Client: `https://localhost:5173`

Swagger and OpenAPI endpoints are enabled only in `Development`.

## API Surface

The production API is hosted under `https://itams.app/api/`. Locally, the API
routes are relative to the API host.

| Route group | Methods | Authorization |
| --- | --- | --- |
| `/auth/login` | `POST` | Anonymous |
| `/auth/refresh` | `POST` | Anonymous |
| `/auth/me` | `GET` | Authenticated |
| `/auth/logout` | `POST` | Authenticated |
| `/auth/change-password` | `POST` | Authenticated |
| `/assets` and `/assets/{id}` | `GET`, `POST`, `PUT`, `DELETE` | Asset read/write policies |
| `/assignments` and `/assignments/{id}` | `GET`, `POST`, `PUT`, `DELETE` | Assignment read/write policies |
| `/users` and `/users/{id}` | `GET`, `POST`, `PUT`, `DELETE` | User read/write policies |
| `/audit-logs` and `/audit-logs/{id}` | `GET` | History read policy |
| `/lifecycle-events` and `/lifecycle-events/{id}` | `GET` | History read policy |
| `/reports/overview` | `GET` | Reports read policy |

The API uses JWT bearer authentication. It validates issuer, audience, signing
key, token lifetime, role claims, and backing session activity.

### Role Policy Summary

| Role | Reports | Assets | Assignments | Users | History |
| --- | --- | --- | --- | --- | --- |
| `Admin` | Read | Read/write | Read/write | Read/write | Read |
| `Manager` | Read | Read/write | Read/write | Read | Read |
| `Technician` | None | Read/write | Read/write | None | None |
| `Auditor` | Read | Read | Read | Read | Read |
| `User` | None | None | None | None | None |

Role policies authorize from the JWT `role` claim. The per-request session
check confirms that the backing session and user are still active. Role and
active-status updates revoke existing sessions for the changed user.

### Authorization Maintenance Checklist

When changing roles or permissions, update these together:

- API authorization policies.
- Frontend role catalogs and role-aware navigation.
- Razor `[Authorize]` attributes.
- User-facing and maintainer documentation.
- API integration tests for allowed and forbidden access.

Before changing Admin user-management behavior, preserve a path for at least
one active login-capable `Admin` to remain available.

### Authorization Test Gaps

The current test suite covers representative role checks, authentication,
session refresh, logout, password changes, role/status session revocation, and read-only history routes. Add
targeted tests before relying on stricter guarantees for:

- Admin self-demotion or self-deactivation.
- Last active login-capable Admin removal.

## Frontend Notes

- The client is Blazor WebAssembly.
- Authorized requests use an auth message handler that attaches the current
  access token.
- Session state is stored in browser session storage with key
  `itams.auth.session`.
- Role-aware navigation is implemented client-side for usability, but API
  authorization remains the enforcement point.
- Deep links are handled by the published IIS rewrite rules in production.

## Development Workflow

1. Clone the repo:

   ```powershell
   git clone jalen@itams.app:C:/Git/ITAMS.git
   cd ITAMS
   ```

   If DNS or SSH to the domain is unavailable, use:

   ```powershell
   git clone jalen@20.120.240.89:C:/Git/ITAMS.git
   ```

2. Create a branch:

   ```powershell
   git switch -c feature/my-change
   ```

3. Restore, build, and test locally.
4. Review and commit only the intended files:

   ```powershell
   git status --short
   git diff -- <PATH_TO_REVIEW>
   git add -- <PATH_TO_COMMIT>
   git commit -m "Describe the change"
   ```

   Use path-specific `git add -- <PATH>` commands so unrelated local edits do
   not get included by accident.
5. Push the branch:

   ```powershell
   git push origin feature/my-change
   ```

   A local commit is only recorded in the current checkout. It is not available
   to other machines and does not deploy until it is pushed.
6. Merge or push to `main` only when ready to deploy.

Pushes to `main` run the production deployment hook. See
[ITAMS Git Deployment](./ITAMS-Git-Deployment.md) and
[ITAMS Production Operations](./ITAMS-Production-Operations.md).

## Troubleshooting

### MongoDB Connection Failures

Check the local `MongoDb:ConnectionString`, Atlas network access rules, database
user credentials, and whether the MongoDB cluster is available.

### JWT Configuration Errors

Confirm `Jwt:SigningKey` exists and is at least 32 characters. A missing or short
signing key prevents the API from starting.

### CORS Failures

If the browser blocks API calls, confirm the frontend origin is listed under
`Cors:AllowedOrigins` for the API environment. Local appsettings include common
localhost and `127.0.0.1` ports.

### Login Or Stale Session Problems

Clear site data or remove `itams.auth.session` from session storage, then sign in
again.

### Bootstrap Admin Was Not Created

Bootstrap admin seeding does not run when login-capable users already exist in
the configured database. Use an existing account, or point local development at
a clean database if a new bootstrap admin is needed.

### Ports Already In Use

Stop the conflicting process or change the launch profile ports. If the client
or API URL changes, keep `ApiBaseUrl` and CORS origins aligned.
