# ADR-0003 — Direct `IAppDbContext` Without Repository Pattern

* **Status:** ACCEPTED
* **Date:** 2026-11-22

## Context
Wrapping EF Core's `DbSet<T>` and `SaveChangesAsync` in custom `IRepository<T>` / `IUnitOfWork` interfaces duplicates EF Core's built-in Repository + Unit of Work implementation and complicates projection, `AsNoTracking()`, and SQL-translatable `GroupBy` queries.

## Decision
Inject **`IAppDbContext`** (exposing `DbSet<User>`, `DbSet<RefreshToken>`, `DbSet<Category>`, `DbSet<Transaction>`, `SaveChangesAsync(CancellationToken)`, `CanConnectAsync(CancellationToken)`, `ClearChangeTracker()`, and `RevokeAllActiveTokensForUserAsync(...)`) directly into Application services, implemented by `AppDbContext` in `Infrastructure`.

## Consequences
* Services write clean, SQL-translatable LINQ queries with `.AsNoTracking()` and `.Select(...)` projections directly, while atomic bulk token revocation uses `ExecuteUpdateAsync` on relational SQL Server with an InMemory fallback for unit tests.
* Unit tests run against EF Core InMemory `AppDbContext` (or mocks where appropriate), while integration tests verify real SQL Server translation and constraints.
