# ITAMS Tech Stack

This document summarizes the technologies used by ITAMS and where each layer
lives in the repository. For setup commands, see
[ITAMS Developer Guide](./ITAMS-Developer-Guide.md). For production hosting and
deployment details, see
[ITAMS Production Operations](./ITAMS-Production-Operations.md).

## Table Of Contents

- [Stack Snapshot](#stack-snapshot)
- [Solution Projects](#solution-projects)
- [Backend API](#backend-api)
- [Data Storage](#data-storage)
- [Authentication And Authorization](#authentication-and-authorization)
- [Frontend Client](#frontend-client)
- [Configuration And Secrets](#configuration-and-secrets)
- [Testing](#testing)
- [Build And Deployment](#build-and-deployment)
- [Runtime Infrastructure](#runtime-infrastructure)
- [Not Currently In The Stack](#not-currently-in-the-stack)

## Stack Snapshot

| Layer | Technology | Repository Location | Notes |
| --- | --- | --- | --- |
| Application platform | .NET 10, C# | `ITAMS.sln` | All application projects target `net10.0` with nullable reference types and implicit usings enabled. |
| Backend | ASP.NET Core minimal API | `ITAMS.Api` | JSON HTTP API for assets, assignments, users, history, reports, and authentication. |
| Frontend | Blazor WebAssembly | `frontend\ITAMS.Client` | Browser client published as static files and configured by `wwwroot\appsettings.json`. |
| Database | MongoDB | `ITAMS.Api\Services`, `ITAMS.Api\Models` | Accessed through `MongoDB.Driver`; local default database name is `itams-dev`. |
| Authentication | JWT bearer tokens and refresh sessions | `ITAMS.Api\Services`, `frontend\ITAMS.Client\Services` | Access tokens are stateless, while each request validates that the backing session remains active. |
| API documentation | Swagger / OpenAPI | `ITAMS.Api\Program.cs`, `ITAMS.Api\Swagger` | Enabled only in the `Development` environment. |
| Tests | xUnit and ASP.NET Core test host | `ITAMS.Api.Tests` | Covers API behavior, authorization, CORS, authentication, reports, and core services. |
| Deployment | PowerShell, Git hook, IIS | `scripts`, `docs\ITAMS-Production-Operations.md` | Pushes to `main` run restore, build, tests, publish, IIS switch, and smoke tests. |

## Solution Projects

| Project | SDK | Target | Purpose |
| --- | --- | --- | --- |
| `ITAMS.Api` | `Microsoft.NET.Sdk.Web` | `net10.0` | ASP.NET Core API, MongoDB access, JWT auth, authorization policies, startup index checks, and bootstrap admin seeding. |
| `frontend\ITAMS.Client` | `Microsoft.NET.Sdk.BlazorWebAssembly` | `net10.0` | Blazor WebAssembly application, routeable pages, shared components, API clients, auth state, and browser storage integration. |
| `ITAMS.Api.Tests` | `Microsoft.NET.Sdk` | `net10.0` | xUnit test project that references the API and uses `Microsoft.AspNetCore.Mvc.Testing`. |

## Backend API

The API is an ASP.NET Core minimal API. Endpoint groups are split by domain
under `ITAMS.Api\Endpoints`, while domain logic and MongoDB operations live
under `ITAMS.Api\Services`.

Key backend packages:

| Package | Version | Use |
| --- | --- | --- |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | `10.0.5` | JWT bearer authentication. |
| `Microsoft.AspNetCore.OpenApi` | `10.0.5` | Development OpenAPI endpoint support. |
| `MongoDB.Driver` | `3.7.1` | MongoDB client, collections, queries, indexes, and transactions. |
| `Swashbuckle.AspNetCore` | `10.1.5` | Swagger generation and Swagger UI in development. |

The API uses:

- Minimal API endpoint mapping from `Program.cs`.
- `ProblemDetails` plus a global exception handler for consistent error
  responses.
- ASP.NET Core options binding and validation for MongoDB, JWT, CORS, and
  bootstrap admin settings.
- CORS configured from `Cors:AllowedOrigins`.
- HTTPS redirection.
- Singleton application services and a singleton `IMongoClient`.
- Startup tasks that ensure MongoDB indexes and the first login-capable admin
  path are ready before serving traffic.

## Data Storage

ITAMS stores application data in MongoDB. The API can use MongoDB Atlas or
another reachable MongoDB instance through `MongoDb:ConnectionString`.

Configured collections:

| Collection | Purpose |
| --- | --- |
| `assets` | Asset records and lifecycle state. |
| `assignments` | Asset assignment records. |
| `auditLogs` | User-visible history and mutation audit records. |
| `lifecycleEvents` | Asset lifecycle event history. |
| `users` | User profiles, roles, password hashes, and account state. |
| `userSessions` | Refresh-session backing records used to validate active logins. |

The default local database name is `itams-dev`. Production database and
connection details are supplied outside the repository.

## Authentication And Authorization

The backend uses ASP.NET Core JWT bearer authentication. Token validation checks
issuer, audience, signing key, token lifetime, unique-name claims, and role
claims. After token validation, the API also checks that the corresponding
server-side session is still active.

Role authorization is evaluated from the JWT `role` claim through ASP.NET Core
authorization policies. The request-time session check confirms that the
backing session is active and that the user record still exists and is active.
User role changes and active-status changes revoke that user's sessions so old
access and refresh tokens cannot continue with stale authorization.

Password hashing uses `PasswordHasher<UserDocument>` from ASP.NET Core Identity.
The app has a bootstrap-admin configuration path for the first login-capable
administrator when no login-capable users already exist.

Authorization is policy-based. Policies cover:

- Authenticated access.
- User read and write access.
- Asset read and write access.
- Assignment read and write access.
- History read access.
- Reports read access.

The Blazor client keeps session state in browser session storage under
`itams.auth.session` and attaches access tokens to authorized API requests.
Client-side role-aware navigation mirrors the API policy roles for usability,
but the API remains the enforcement point.

## Frontend Client

The frontend is a Blazor WebAssembly app served from
`frontend\ITAMS.Client`. It uses routeable Razor pages, reusable Razor
components, typed model classes, and scoped API services.

Key frontend packages:

| Package | Version | Use |
| --- | --- | --- |
| `Microsoft.AspNetCore.Components.Authorization` | `10.0.5` | Client auth state and authorization support. |
| `Microsoft.AspNetCore.Components.WebAssembly` | `10.0.5` | Blazor WebAssembly runtime. |
| `Microsoft.AspNetCore.Components.WebAssembly.DevServer` | `10.0.5` | Local development server. |

The client also includes small JavaScript helpers under `wwwroot\js` for theme,
storage, and layout behavior. Styling is plain CSS in `wwwroot\css\app.css`;
there is no JavaScript package manager or frontend build chain outside the .NET
Blazor tooling.

The client reads `ApiBaseUrl` from
`frontend\ITAMS.Client\wwwroot\appsettings.json`. If missing, local development
falls back to `https://localhost:7004/`.

## Configuration And Secrets

Source-controlled configuration contains non-secret defaults and names. Secret
values must stay outside Git.

| Area | Local Development | Production |
| --- | --- | --- |
| API app settings | `ITAMS.Api\appsettings.json` and user secrets | Published API `web.config` environment variables. |
| MongoDB connection string | `.NET user-secrets` for `ITAMS.Api` | `C:\ITAMS\Deploy\itams-production-env.json`. |
| JWT signing key | `.NET user-secrets` for `ITAMS.Api` | `C:\ITAMS\Deploy\itams-production-env.json`. |
| Bootstrap admin password | `.NET user-secrets` for `ITAMS.Api` | `C:\ITAMS\Deploy\itams-production-env.json`. |
| Client API URL | `frontend\ITAMS.Client\wwwroot\appsettings.json` | Rewritten during deployment to `https://itams.app/api/`. |

Do not commit MongoDB connection strings, JWT signing keys, bootstrap admin
passwords, private keys, certificate private keys, or exported `.pfx` files.

## Testing

The test project is `ITAMS.Api.Tests`. It uses:

| Package | Version | Use |
| --- | --- | --- |
| `Microsoft.AspNetCore.Mvc.Testing` | `10.0.5` | In-process API test host. |
| `Microsoft.NET.Test.Sdk` | `17.11.1` | .NET test platform integration. |
| `xunit` | `2.9.2` | Test framework. |
| `xunit.runner.visualstudio` | `2.8.2` | Visual Studio and `dotnet test` runner support. |

Run the API test suite with:

```powershell
dotnet test .\ITAMS.Api.Tests\ITAMS.Api.Tests.csproj -c Release
```

## Build And Deployment

The repo builds through standard .NET CLI commands:

```powershell
dotnet restore .\ITAMS.sln
dotnet build .\ITAMS.sln -c Release
dotnet publish .\frontend\ITAMS.Client\ITAMS.Client.csproj -c Release
dotnet publish .\ITAMS.Api\ITAMS.Api.csproj -c Release
```

Production deployment is automated by a Git `post-receive` hook and
`scripts\Invoke-ITAMSDeployment.ps1`. Pushes to `main` deploy through the
validated path:

1. Checkout the pushed commit into a clean work area.
2. Restore, build, and test the solution.
3. Publish the Blazor client and ASP.NET Core API.
4. Write the production client `appsettings.json`.
5. Apply IIS rewrite rules and API environment variables.
6. Switch the IIS site and `/api` application to the new release.
7. Recycle app pools and smoke-test the live site.

## Runtime Infrastructure

Production runs at `https://itams.app/` on IIS:

| Runtime Item | Value |
| --- | --- |
| Root site | Blazor WebAssembly static client. |
| API child app | `/api`, hosted by ASP.NET Core Module. |
| IIS site | `ITAMS`. |
| IIS app pools | `ITAMS.Site`, `ITAMS.Api`. |
| Canonical API URL | `https://itams.app/api/`. |
| Release layout | Versioned folders under `C:\inetpub\ITAMS\releases`. |

The production server also hosts the bare Git repository used by the deployment
hook. IIS owns runtime process lifetime, so the app does not depend on
`dotnet watch`, an interactive shell, or a desktop session.

## Not Currently In The Stack

The current repository does not use:

- Entity Framework Core or a relational database.
- Node.js, npm, Vite, React, Angular, or a separate JavaScript bundler.
- Docker or Kubernetes deployment artifacts.
- A separate reverse proxy layer in front of IIS.
- A dedicated frontend unit-test framework.
