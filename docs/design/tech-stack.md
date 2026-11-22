# Tech Stack and Version Policy

## 1. Runtime & Ecosystem Baseline (2026-11)

| Fact | Source |
|---|---|
| .NET 11 (STS, C# 15) was released on 2026-11-10; supported until 2028-11-14. | Microsoft .NET support policy page + .NET blog "Announcing .NET 11" |
| .NET 10 is **LTS**, released 2025-11-11, supported until 2028-11-14 (same end date as .NET 11). | .NET support policy page |
| .NET 8 and .NET 9 reached end of support on 2026-11-10. | .NET support policy page |
| EF Core 10 matches .NET 10 (LTS); EF Core 11 shipped with .NET 11. | learn.microsoft.com/ef/core/what-is-new |
| SQL Server 2025 GA 2025-11-18; Docker tags `2025-latest` and `2022-latest` both exist. | learn.microsoft.com SQL Server Linux release notes, Docker Hub page |
| xUnit.net v3 is the current template (`dotnet new xunit3`); on .NET 10 SDK it uses Microsoft.Testing.Platform and generates a `global.json`. | xunit.net v3 getting-started page |
| Swashbuckle 10.x moved to Microsoft.OpenApi 2.x namespaces. | Swashbuckle migration guide |

## 2. Decisions

| Area | Choice | Why | Not chosen |
|---|---|---|---|
| Runtime | **.NET 10 LTS**, `net10.0`, C# 14 (default for .NET 10) | LTS, same support end date as .NET 11, mature NuGet ecosystem, fewer breaking changes. | .NET 11 (STS). |
| Web | ASP.NET Core Web API with **Controllers** | Explicit routing, model binding, `[ProducesResponseType]`, and attributes. | Minimal APIs. |
| ORM | **EF Core 10** Code First + `Microsoft.EntityFrameworkCore.SqlServer` | Fluent configuration in separate `IEntityTypeConfiguration<T>` classes. | Dapper. |
| DB | **SQL Server** — dev via LocalDB or Docker `mcr.microsoft.com/mssql/server:2022-latest` | Widely documented and fully supported (`2025-latest` also supported). | PostgreSQL/MySQL/MongoDB. |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer` + `JsonWebTokenHandler` (`Microsoft.IdentityModel.JsonWebTokens`) | First-party, modern handler. HS256 with a ≥ 32-byte secret from configuration. | ASP.NET Core Identity full stack. |
| Password hashing | `PasswordHasher<User>` from `Microsoft.Extensions.Identity.Core` (PBKDF2, salted, versioned) | First-party, supports rehash on login. | Custom hashing; MD5/SHA-only. |
| Validation | **DataAnnotations** on request DTOs + business checks in services | Zero extra dependency; automatic 400 via `[ApiController]`. | FluentValidation. |
| Mapping | **Manual** mapping (`ToDto()` extension methods and SQL `.Select(...)` projections) | Readable, debuggable, compile-time safe, avoids over-fetching. AutoMapper/MediatR moved to commercial licensing in 2025. | AutoMapper, Mapster, MediatR. |
| Errors | `ProblemDetails` (RFC 9457) + `IExceptionHandler` | Built into ASP.NET Core; standard shape. | Custom `{success,data,message}` envelope. |
| OpenAPI | `Microsoft.AspNetCore.OpenApi` (`AddOpenApi` / `MapOpenApi`) + **Swashbuckle.AspNetCore.SwaggerUI** pointing to `/openapi/v1.json`; JWT scheme added by a *document transformer* | Built-in generator without mixing `AddSwaggerGen`; Swagger UI keeps familiar Authorize flow. | Swashbuckle `AddSwaggerGen`. |
| Logging | Built-in `ILogger<T>` (console) | Simple and structured. | Serilog. |
| Time | Inject `TimeProvider` | Deterministic tests via `FakeTimeProvider`. | `DateTime.Now/UtcNow` calls inside services. |
| Unit tests | **xUnit v3** (`dotnet new xunit3`), **Moq**, EF Core **InMemory** provider for service-logic tests | Fast feedback; known InMemory limits documented in [testing.md](testing.md). | FluentAssertions ≥ 8 (licence change). |
| Integration tests | `Microsoft.AspNetCore.Mvc.Testing` + **Testcontainers.MsSql** (Docker) or an external SQL Server via env var `TEST_SQL_CONNECTION` | Tests real SQL translation, constraints, and auth pipeline. | SQLite in-memory. |
| Packages | **Central Package Management** (`Directory.Packages.props`) | Single source of truth for pinned versions. | Per-csproj versions. |
| Container | Multi-stage Dockerfile (`mcr.microsoft.com/dotnet/sdk:10.0` → `aspnet:10.0`), `docker-compose.yml` (api + sqlserver) | Lightweight non-root container deployment. | Kubernetes, cloud IaC. |
| CI | GitHub Actions (build + test + vulnerable-package check) | Automated quality & security gate. | — |

## 3. Packages (pinned in `Directory.Packages.props`)

| Project | Packages |
|---|---|
| Application | `Microsoft.EntityFrameworkCore` (no provider), `Microsoft.Extensions.Identity.Core`, `Microsoft.Extensions.Logging.Abstractions` |
| Infrastructure | `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets=all`), `Microsoft.IdentityModel.JsonWebTokens` |
| Api | `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore.SwaggerUI`, `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets=all`, for EF tooling) |
| UnitTests | `xunit.v3` (`xunit.runner.visualstudio`), `Moq`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`), `Microsoft.NET.Test.Sdk` |
| IntegrationTests | `xunit.v3`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.NET.Test.Sdk` |
| Tools (`.config/dotnet-tools.json`) | `dotnet-ef` (10.x) |

## 4. Dependency Maintenance Policy

1. Prefer **latest stable (non-preview)** packages supporting `net10.0`.
2. Keep EF Core, ASP.NET Core, and `Microsoft.Extensions.*` packages aligned on the **10.0.x** line.
3. Record architectural package changes in an ADR under `docs/decisions/`.
4. Run `dotnet list package --vulnerable --include-transitive` in CI and whenever updating dependencies.

## 5. Local Tooling

* .NET 10 SDK (`global.json` + Visual Studio / VS Code C# Dev Kit / Rider).
* Git, Docker Desktop (for SQL Server container and Testcontainers), Postman (`postman/` + `/openapi/v1.json`).
