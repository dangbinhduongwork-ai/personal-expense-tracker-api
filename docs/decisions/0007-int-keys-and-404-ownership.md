# ADR-0007 — `int` Identity Keys and `404` for Foreign Resources

* **Status:** ACCEPTED
* **Date:** 2026-11-22

## Context
Auto-increment `int` keys simplify SQL debugging, logs, and manual testing, whereas exposing `403 Forbidden` for another user's resource leaks whether that integer ID exists in the database (BOLA/enumeration oracle).

## Decision
Use SQL Server **`int identity`** primary keys across all four tables, and enforce single-query ownership filtering (`Where(x => x.Id == id && x.UserId == currentUserId)`) for personal resources. A missing record and another user's record both throw `NotFoundException` (`404`). Reserve `403 Forbidden` (`system_category_readonly`) exclusively for write attempts on seeded system categories (`UserId IS NULL`).

## Consequences
* Neutralizes ID-enumeration side channels across transactions and personal categories (`SEC-06`..`SEC-14`).
* Simple readable IDs in tests and Swagger UI; switching to UUIDv7 later would require an explicit schema ADR.
