# ITAMS

<p align="center">
  <img src="frontend/ITAMS.Client/wwwroot/images/logo.png" alt="ITAMS logo" width="160">
</p>

<p align="center">
  A full-stack IT asset management system for tracking inventory, assignments,
  users, reports, and asset history.
</p>

ITAMS helps IT teams manage the lifecycle of organizational hardware from
inventory and assignment through repair, retirement, and audit review. It
combines a Blazor WebAssembly client with an ASP.NET Core API and MongoDB
storage.

Production application: [https://itams.app](https://itams.app)

## Features

- Track laptops, desktops, monitors, mobile devices, servers, peripherals, and
  other IT assets.
- Record asset tags, serial numbers, manufacturers, models, locations,
  departments, warranties, lifecycle dates, and status.
- Assign assets to users and retain assignment history.
- Manage application users, roles, departments, and account status.
- Review operational dashboards, trends, and warranty information.
- Maintain audit logs and asset lifecycle events.
- Authenticate with short-lived JWT access tokens and server-backed refresh
  sessions.
- Enforce role-based authorization in both the API and user interface.

## Roles

| Role | Typical access |
| --- | --- |
| `Admin` | Full access, including user management |
| `Manager` | Reports, assets, assignments, users, and history |
| `Technician` | Asset and assignment management |
| `Auditor` | Read-only reports, assets, assignments, users, and history |
| `User` | Account access only |

The API is the final authorization boundary. Client-side navigation and controls
mirror the user's permissions for usability.

## Technology

| Layer | Technology |
| --- | --- |
| Application platform | .NET 10 and C# |
| Frontend | Blazor WebAssembly |
| Backend | ASP.NET Core minimal API |
| Database | MongoDB |
| Authentication | JWT bearer tokens and refresh sessions |
| API documentation | Swagger/OpenAPI in development |
| Testing | xUnit and ASP.NET Core integration testing |
| Production hosting | IIS |

## Repository Structure

```text
ITAMS.Api/                 ASP.NET Core API
ITAMS.Api.Tests/           API and integration tests
frontend/ITAMS.Client/     Blazor WebAssembly client
docs/                      Application, development, and operations guides
scripts/                   Deployment and utility scripts
ITAMS.sln                  .NET solution
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- Access to MongoDB Atlas or another reachable MongoDB instance
- A modern web browser
- Windows PowerShell for the documented scripts and commands

Trust the local HTTPS development certificate if necessary:

```powershell
dotnet dev-certs https --trust
```

## Local Setup

Clone the repository and restore its dependencies:

```powershell
git clone https://github.com/Jalen-Bryant/ITAMS.git
cd ITAMS
dotnet restore .\ITAMS.sln
```

Configure development secrets from the repository root:

```powershell
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "MongoDb:ConnectionString" "<MONGODB_CONNECTION_STRING>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "Jwt:SigningKey" "<JWT_SIGNING_KEY_AT_LEAST_32_CHARACTERS>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Username" "<ADMIN_USERNAME>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:DisplayName" "<ADMIN_DISPLAY_NAME>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Email" "<ADMIN_EMAIL>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Department" "<ADMIN_DEPARTMENT>"
dotnet user-secrets --project .\ITAMS.Api\ITAMS.Api.csproj set "BootstrapAdmin:Password" "<ADMIN_PASSWORD>"
```

The bootstrap account is created only when the configured database has no
login-capable users.

### Run the API

```powershell
dotnet watch --project .\ITAMS.Api\ITAMS.Api.csproj run --launch-profile https
```

The local API runs at `https://localhost:7004`. Swagger is available at
`https://localhost:7004/swagger` in the `Development` environment.

### Run the client

In a second terminal:

```powershell
dotnet watch --project .\frontend\ITAMS.Client\ITAMS.Client.csproj run --launch-profile https
```

The client runs at `https://localhost:5173` and reads its API address from
`frontend/ITAMS.Client/wwwroot/appsettings.json`.

## Build and Test

```powershell
dotnet restore .\ITAMS.sln
dotnet build .\ITAMS.sln -c Release
dotnet test .\ITAMS.Api.Tests\ITAMS.Api.Tests.csproj -c Release
```

The test suite covers authentication, authorization, session behavior, CORS,
reports, audit history, bootstrap administration, and core API services.

## API Overview

The API provides route groups for:

- `/auth`
- `/assets`
- `/assignments`
- `/users`
- `/reports`
- `/audit-logs`
- `/lifecycle-events`

All protected routes require a valid access token and an active server-side
session. Role or account-status changes revoke affected sessions.

## Configuration and Security

Source-controlled settings contain only non-secret defaults. Never commit:

- MongoDB connection strings
- JWT signing keys
- Bootstrap administrator passwords
- SSH or certificate private keys
- Exported certificate files

Use .NET user secrets for local development and an external secret store or
protected environment configuration in production.

## Documentation

- [Documentation index](docs/README.md)
- [Application guide](docs/ITAMS-Application-Guide.md)
- [Developer guide](docs/ITAMS-Developer-Guide.md)
- [Technology stack](docs/ITAMS-Tech-Stack.md)
- [Git deployment guide](docs/ITAMS-Git-Deployment.md)
- [Production operations](docs/ITAMS-Production-Operations.md)

## Deployment

The included deployment workflow targets IIS. A push to the configured
production `main` branch can trigger the server-side deployment hook, which
restores, builds, tests, publishes, switches the active release, and performs
smoke checks.

Review the [production operations guide](docs/ITAMS-Production-Operations.md)
before configuring or running a deployment.
