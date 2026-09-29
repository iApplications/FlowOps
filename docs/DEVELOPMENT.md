# Local Development

Phase 0 runs Studio, Portal, and the API on localhost. PostgreSQL runs in Docker Compose. Redis, RabbitMQ, email delivery, authentication, and workflow execution belong to later phases.

## Prerequisites

- Node.js 20.9 or newer and npm
- .NET 10 SDK
- Docker Desktop with Compose

## Install dependencies

From the repository root:

```powershell
npm.cmd install
```

## Start PostgreSQL

Copy `.env.example` to `.env`, then set a local `FLOWOPS_DB_PASSWORD` value in `.env`. This file is ignored by Git.

```powershell
Copy-Item .env.example .env
docker compose up -d postgres
docker compose ps
```

PostgreSQL listens on `127.0.0.1:5432` by default. Change `FLOWOPS_DB_PORT` in `.env` if that port is already in use. The Phase 0 API does not yet read or write business data; database integration starts with later persistence work.

## Start the API

In Visual Studio 2026, open `FlowOps.slnx`, select `FlowOps.Api` as the startup project, and press F5. If Visual Studio was already open when the .NET SDK was installed, restart Visual Studio so it picks up the user `PATH` and `DOTNET_ROOT` settings.

Keep only one API process running. Stop debugging in Visual Studio or press Ctrl+C in the terminal that started the API before rebuilding if Windows reports that `FlowOps.Api.exe` is locked.

Alternatively, from a terminal:

```powershell
dotnet run --project backend/FlowOps.Api --urls http://localhost:5050
```

If the SDK was installed to the user directory with Microsoft's install script and `dotnet` is not on `PATH`, run `& "$env:USERPROFILE\.dotnet\dotnet.exe"` in place of `dotnet` in the commands above and below.

Open `http://localhost:5050/swagger` for the interactive API interface. The OpenAPI document is at `http://localhost:5050/openapi/v1.json`. Both are available only in Development. Check `http://localhost:5050/api/health` for a JSON response and `http://localhost:5050/health/live` for HTTP 200. The API uses structured JSON console logging.

## Start the web apps

Use separate terminals from the repository root:

```powershell
npm.cmd run dev:studio
npm.cmd run dev:portal
```

Studio runs at `http://localhost:3000`; Portal runs at `http://localhost:3001`. Both use shared packages from `packages/`.

## Checks

```powershell
npm.cmd run lint
npm.cmd run typecheck
npm.cmd run build
dotnet build backend/FlowOps.Api
docker compose config
```

The current checks cover the Phase 0 scaffold. End-to-end workflow, authorization, and database checks will be added with the features they protect.
