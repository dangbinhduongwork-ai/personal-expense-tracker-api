# ADR-0006 — `DateOnly` for Transaction Dates and UTC for Audit Timestamps

* **Status:** ACCEPTED
* **Date:** 2026-11-22

## Context
Storing a user's expense date as `datetime2` introduces midnight time-zone shifts across UTC and `Asia/Ho_Chi_Minh` (+07:00), causing late-night expenses to appear on the wrong day or month in statistics.

## Decision
Store `Transaction.TransactionDate` as C# **`DateOnly`** mapped natively to SQL Server **`date`** (`yyyy-MM-dd`). Store all audit and token lifecycle timestamps (`CreatedAt`, `UpdatedAt`, `ExpiresAt`, `RevokedAt`) as UTC **`DateTime`** (`timeProvider.GetUtcNow().UtcDateTime`) mapped to SQL Server **`datetime2`** and serialized as ISO-8601 UTC in DTOs. Convert `TimeProvider.GetUtcNow()` to `App:TimeZoneId` (`"Asia/Ho_Chi_Minh"`, fallback `"SE Asia Standard Time"`) via `IAppClock` when determining "today" (`BR-05`, `yearly` default).

## Consequences
* Eliminates UTC-offset boundary drift in daily/monthly/yearly aggregations while keeping audit logs unambiguously UTC and deterministically testable via `FakeTimeProvider`.
