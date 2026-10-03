# Local Development

Phase 1 runs Studio, Portal, and the API on localhost. PostgreSQL can run locally on Windows or in Docker Compose. Mailpit is available through Compose for email testing. Workflow execution belongs to later phases.

Studio and Portal have independent accounts. The same email can register once on each site with separate passwords, confirmation/reset tokens, MFA keys, memberships and browser sessions. Authentication requests specify `surface`; browser API requests and server page checks send `X-FlowOps-Surface` to select the site's session cookie. The selector never grants account permissions.

Studio signup can confirm email, sign in to setup, create a workspace as Owner and enable MFA. Full Studio management requires Owner/Admin membership and the workspace MFA policy. Portal signup cannot create organizations or access Studio. Studio adds confirmed Portal accounts with the Member role and confirmed Studio accounts with the Admin role. Converting between those roles requires adding the separate account; roles cannot change an account's site.

The SeparateStudioPortalAccounts migration preserves legacy Studio signup markers and existing Owner/Admin accounts as Studio accounts; remaining accounts become Portal accounts. It revokes existing sessions, so sign in again after migration. For an account previously used on both sites, register a separate account on the other site and assign its memberships in Studio.

## Prerequisites

- Node.js 20.9 or newer and npm
- .NET 10 SDK
- PostgreSQL 17 locally, or Docker Desktop with Compose

## Install dependencies

From the repository root:

```powershell
npm.cmd install
```

## Start PostgreSQL with Docker Compose

Copy `.env.example` to `.env`, then set a local `FLOWOPS_DB_PASSWORD` value in `.env`. This file is ignored by Git.

```powershell
Copy-Item .env.example .env
docker compose up -d postgres mailpit
docker compose ps
```

PostgreSQL listens on `127.0.0.1:5432` by default. Change `FLOWOPS_DB_PORT` in `.env` if that port is already in use. Mailpit's inbox is at `http://localhost:8025`; its local SMTP listener is on port 1025.

## Start PostgreSQL locally on Windows

Install PostgreSQL 17 with the server and command-line tools, then start its Windows service. From `psql` as the `postgres` administrator, create the application login and database:

```sql
CREATE ROLE flowops LOGIN;
\password flowops
CREATE DATABASE flowops OWNER flowops;
```

The `\password` command prompts for a password without putting it in shell history. The API reads the nonsecret host, port, database, and username from `backend/FlowOps.Api/appsettings.Development.json`.

Save the login password in a Windows DPAPI encrypted file from the repository root:

```powershell
& .\scripts\set-local-db-secret.ps1
```

The file is stored at `%LOCALAPPDATA%\FlowOps\Secrets\Development.dbpassword.dpapi`, outside the repository. Only the same Windows account can decrypt it. The API reads this file in Development when neither `ConnectionStrings:FlowOps` nor `FLOWOPS_DB_PASSWORD` is configured. If PowerShell script execution is disabled, invoke the script with `powershell.exe -ExecutionPolicy Bypass -File .\scripts\set-local-db-secret.ps1`.

For confirmation and password reset emails without Docker, install and start Mailpit in a separate terminal:

```powershell
winget install --id axllent.mailpit --exact
mailpit --listen 127.0.0.1:8025 --smtp 127.0.0.1:1025
```

Open `http://localhost:8025` to read captured emails. Mailpit must be running before requesting a confirmation or password reset email. Its inbox is local; it does not deliver these test messages to external recipients.

## Configure the API database for Docker or another PostgreSQL host

When using Docker Compose, the API must use the same PostgreSQL credentials as `.env`. In Visual Studio, right-click `FlowOps.Api` and choose **Manage User Secrets**. Add a connection string like this, replacing the password and port with the values in your local `.env`:

```json
{
  "ConnectionStrings": {
    "FlowOps": "Host=127.0.0.1;Port=5432;Database=flowops;Username=flowops;Password=YOUR_LOCAL_PASSWORD"
  }
}
```

User secrets stay outside the repository but are not encrypted. On Windows, use the DPAPI file above when encryption at rest is needed. An alternative is to set `ConnectionStrings__FlowOps` or `FLOWOPS_DB_PASSWORD` in the API process environment. Production must supply `ConnectionStrings__FlowOps` through its secret manager and its own SMTP configuration; do not copy development secrets into source control. Nonsecret defaults belong in `appsettings.json`, with environment-specific overrides in `appsettings.Development.json` or `appsettings.{Environment}.json`.

Restore the repository-local EF Core tool and apply the migration once PostgreSQL is running:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update --project backend/FlowOps.Api
```

In Visual Studio, use **Tools → NuGet Package Manager → Package Manager Console** or a terminal for the migration command before starting the API.

## Start the API

In Visual Studio 2026, open `FlowOps.slnx`, select `FlowOps.Api` as the startup project, and press F5. If Visual Studio was already open when the .NET SDK was installed, restart Visual Studio so it picks up the user `PATH` and `DOTNET_ROOT` settings.

Keep only one API process running. Stop debugging in Visual Studio or press Ctrl+C in the terminal that started the API before rebuilding if Windows reports that `FlowOps.Api.exe` is locked.

Alternatively, from a terminal:

```powershell
dotnet run --project backend/FlowOps.Api --urls http://localhost:5050
```

If the SDK was installed to the user directory with Microsoft's install script and `dotnet` is not on `PATH`, run `& "$env:USERPROFILE\.dotnet\dotnet.exe"` in place of `dotnet` in the commands above and below.

Open `http://localhost:5050/swagger` for the interactive API interface. The OpenAPI document is at `http://localhost:5050/openapi/v1.json`. Both are available only in Development. Swagger UI obtains CSRF tokens automatically for API mutations. Check `http://localhost:5050/api/health` for a JSON response and `http://localhost:5050/health/live` for HTTP 200. The API uses structured JSON console logging.

## Start the web apps

Use separate terminals from the repository root:

```powershell
npm.cmd run dev:studio
npm.cmd run dev:portal
```

Studio runs at `http://localhost:3000`; Portal runs at `http://localhost:3001`. Both proxy `/api` to `http://localhost:5050` and use shared packages from `packages/`. Set `FLOWOPS_API_URL` for both Next.js processes if the API uses another address. Use the same hostname for all three services during local development; mixing `localhost` and `127.0.0.1` can prevent browser cookies from being sent.

Create an account at `/register`, open Mailpit and follow the confirmation link, then sign in at `/login` and create an organization on the home page. Owners and Admins must set up an authenticator before they can manage members or enter protected Studio features. Save the recovery codes when they are shown. Password reset emails also appear in Mailpit. Members can be added by email after they have registered and confirmed their address.

## Checks

```powershell
npm.cmd run lint
npm.cmd run typecheck
npm.cmd run build
dotnet build backend/FlowOps.Api
dotnet test FlowOps.slnx -c Release
docker compose config
```

The automated API tests use an in-memory SQLite database for authentication, CSRF, separate Studio/Portal accounts and sessions, MFA, password-reset, and cross-organization checks. The latest local run passed 11 tests. PostgreSQL migrations have been applied locally, and the Studio/Portal browser flows have undergone internal testing. Docker checks remain separate from the local Windows PostgreSQL setup. Complete internal testing before committing Phase 1.
