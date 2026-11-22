# ADR-0001 — Technology Stack (.NET 10 LTS)

* **Status:** ACCEPTED
* **Date:** 2026-11-22
* **Verified Against:** Official .NET Support Policy, EF Core 10 docs, SQL Server Linux release notes, xUnit v3 docs, NuGet v3 API (all accessed 2026-11-22).

## Context
The project requires a stable, maintainable REST API stack running cleanly on Windows 10/11 and Docker. Although .NET 11 (STS) shipped on 2026-11-10, .NET 10 (LTS, released 2025-11-11) is supported through the exact same end date (**2028-11-14**) and has mature `10.0.15` runtime and NuGet alignment. Commercial license shifts in 2025 affect AutoMapper (`>=15`), MediatR (`>=13`), and FluentAssertions (`>=8`).

## Decision
Lock the technology stack to **.NET 10 LTS (`net10.0`, C# 14)**, **ASP.NET Core Web API with Controllers**, **EF Core 10 Code First + SQL Server** (`2022-latest` Docker / LocalDB dev), **`PasswordHasher<User>`** + **`JsonWebTokenHandler`** (HS256 JWT + SHA-256 hashed rotating refresh tokens), **DataAnnotations**, manual DTO mapping, **`ProblemDetails` (`IExceptionHandler`)**, injected **`TimeProvider`**, **Central Package Management** (`Directory.Packages.props`), **xUnit v3** (MTP + `Moq` + EF InMemory) for unit tests, and **`WebApplicationFactory<Program>` + `Testcontainers.MsSql`** for integration tests. Exact pinned package versions are recorded in `Directory.Packages.props`.

## Consequences
* Zero commercial licensing risk (all packages MIT, Apache-2.0, or BSD-3-Clause).
* Simple, debuggable code without MediatR/AutoMapper/Repository indirection.
* Upgrading to a future LTS requires an explicit new ADR.
