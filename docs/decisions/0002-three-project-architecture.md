# ADR-0002 — Three Source Projects (Entities in Application)

* **Status:** ACCEPTED
* **Date:** 2026-11-22

## Context
Standard Clean Architecture often adds a fourth `Domain` project, increasing project count, assembly boilerplate, and navigation overhead for a small-to-medium service with straightforward data models.

## Decision
Use **3 source projects** (`ExpenseTracker.Api` → `ExpenseTracker.Application` ← `ExpenseTracker.Infrastructure`) plus 2 test projects (`UnitTests`, `IntegrationTests`). Place plain entities (`User`, `RefreshToken`, `Category`, `Transaction`) and enums (`TransactionType`) inside `ExpenseTracker.Application/{Entities,Enums}`. `Application` has zero dependencies on `Infrastructure`, `Api`, ASP.NET Core, or the SQL Server provider.

## Consequences
* Simpler dependency graph and faster onboarding.
* Domain logic stays cleanly isolated from HTTP and database provider concerns; extracting a separate `Domain` project later remains straightforward if complexity grows.
