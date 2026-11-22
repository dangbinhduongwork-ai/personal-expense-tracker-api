# Deployment & Local Environment Guide

This guide covers running **Personal Expense Tracker API** locally on Windows 10/11 and via Docker Compose.

## 1. Prerequisites
* **.NET 10 SDK** (`10.0.x`): verify with `dotnet --list-sdks`.
* **SQL Server**: Docker Desktop (recommended) or SQL Server Express / LocalDB.
* **Git** and **PowerShell** (`pwsh` or Windows PowerShell 5.1).

## 2. Running Locally Without Docker for the API

1. Restore local EF Core tools and NuGet dependencies:
   ```powershell
   dotnet tool restore
   dotnet restore
   ```
2. Configure `user-secrets` on `src/ExpenseTracker.Api` (never store secrets in `appsettings*.json`):
   ```powershell
   $bytes = New-Object byte[] 48; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
   $jwtKey = [Convert]::ToBase64String($bytes)
   dotnet user-secrets set "Jwt:Key" "$jwtKey" --project src/ExpenseTracker.Api
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=ExpenseTrackerDb;Trusted_Connection=True;TrustServerCertificate=True" --project src/ExpenseTracker.Api
   ```
3. Apply migrations and launch:
   ```powershell
   dotnet tool run dotnet-ef database update --project src/ExpenseTracker.Infrastructure --startup-project src/ExpenseTracker.Api
   dotnet run --project src/ExpenseTracker.Api
   ```
   Optional Development-only sample dataset (1 demo user + 200 deterministic transactions):
   ```powershell
   dotnet run --project src/ExpenseTracker.Api -- --Seed:DemoData=true
   ```
   *(Ignored automatically outside `Development`.)*

## 3. Running with Docker Compose

1. Copy `.env.example` to `.env` (git-ignored) and replace placeholder values:
   * `MSSQL_SA_PASSWORD`: SQL Server password (>= 8 chars, at least 3 of: uppercase, lowercase, digit, symbol).
   * `JWT_KEY`: Base64 random key with at least **32 decoded bytes** (48 recommended).
   * `.env.example` enables `OPENAPI_ENABLE_UI=true` for local Swagger UI exploration (`docker-compose.yml` defaults it to `false` when unset).
2. Build and start containers:
   ```powershell
   docker compose up --build -d
   docker compose ps
   ```
   * SQL Server listens only on the internal Docker bridge network; `api` binds `127.0.0.1:8080:8080`, applies EF migrations on startup (`Database__MigrateOnStartup=true`), and runs as non-root `$APP_UID`.
3. Verify end-to-end with the smoke script:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -BaseUrl http://localhost:8080
   ```
4. Tear down containers (and remove volumes when finished):
   ```powershell
   docker compose down -v
   ```

## 4. Regenerating the Static OpenAPI Snapshot

`docs/api/openapi.v1.json` is exported directly from a running instance with `OpenApi:EnableUi=true`:

```powershell
New-Item -ItemType Directory -Force docs\api | Out-Null
Invoke-RestMethod -Method Get -Uri "http://localhost:8080/openapi/v1.json" | ConvertTo-Json -Depth 100 | Set-Content -Path docs\api\openapi.v1.json -Encoding UTF8
```

## 5. Production Hardening Notes (Beyond Local Compose Demo)
* Set `ASPNETCORE_ENVIRONMENT=Production`, `OpenApi__EnableUi=false`, `Seed__DemoData=false`, and explicit `AllowedHosts`.
* Set `HttpsRedirection__Enabled=true` or terminate TLS at your reverse proxy / ingress.
* Disable `Database__MigrateOnStartup` in multi-replica deployments and run `dotnet ef migrations bundle` / idempotent SQL scripts as a release step.
* Store secrets in a managed secret store (Azure Key Vault / KMS / platform secrets) and use a least-privilege SQL login instead of `sa`.

## 6. Troubleshooting

| Symptom | Cause & Fix |
|---|---|
| `sqlserver` unhealthy / exits | `MSSQL_SA_PASSWORD` failed SQL Server complexity policy or `.env` is missing. |
| API fails fast at startup (`Jwt:Key`) | `JWT_KEY` is missing, invalid Base64, or shorter than 32 decoded bytes. |
| `400 Bad Request` invalid host header | Verify HTTP `Host` header matches `AllowedHosts` (default `localhost;127.0.0.1` in compose). |
| Integration tests fail to find Docker | Start Docker Desktop, or set `TEST_SQL_CONNECTION` to an existing SQL Server instance. |
| `dotnet ef` not found | Run `dotnet tool restore` at the repository root. |
| `Bind for 127.0.0.1:8080 failed` | Port `8080` is already in use on the host; set `API_PORT=18080` (or another free port) in `.env` and target `http://localhost:18080`. |
