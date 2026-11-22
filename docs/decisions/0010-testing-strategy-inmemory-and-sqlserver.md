# ADR-0010 — Two-Layer Test Strategy (InMemory Unit + Real SQL Server Integration)

* **Status:** ACCEPTED
* **Date:** 2026-11-22

## Context
EF Core InMemory does not enforce foreign keys, unique indexes, CHECK constraints, SQL Server collations, or LINQ-to-SQL `GroupBy`/`decimal(18,2)` translation, while SQLite differs from SQL Server on `decimal` aggregates and ordering.

## Decision
Use a two-layer **xUnit v3** (MTP via `global.json`) suite:
1. **`ExpenseTracker.UnitTests` (`Moq` + `FakeTimeProvider` + EF Core `InMemory`):** fast, Docker-free verification of service business rules, validation branching, rounding, zero-filling, and error mapping.
2. **`ExpenseTracker.IntegrationTests` (`WebApplicationFactory<Program>` + `Testcontainers.MsSql`, with optional `TEST_SQL_CONNECTION` override):** full HTTP pipeline, migrations, seed identity continuation, DB constraints (`CHECK`, `Restrict`, unique indexes), `SEC-01..25`, and SQL Server aggregation against deterministic dataset **DS-1**.

## Consequences
* Fast feedback during unit development plus high confidence that SQL Server queries, constraints, and JWT enforcement actually work end-to-end.
