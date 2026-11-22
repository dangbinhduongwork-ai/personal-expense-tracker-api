# ADR-0005 — ProblemDetails Errors and Plain DTO Success Bodies

* **Status:** ACCEPTED
* **Date:** 2026-11-22

## Context
Custom `{ success, data, message }` envelopes duplicate HTTP status semantics and break standard ASP.NET Core `[ApiController]` validation responses (`ValidationProblemDetails`).

## Decision
Return **plain DTOs** or `PagedResult<T>` on `2xx` responses, and return **RFC 9457 `ProblemDetails`** (`application/problem+json`) with machine-readable `code` and `traceId` extensions for all non-2xx errors via `IExceptionHandler` (`GlobalExceptionHandler`) + `AddProblemDetails()`. Validation errors use `ValidationProblemDetails` with `code: "validation_failed"`.

## Consequences
* Uniform error shape across `[ApiController]` model binding, rate limiter (`429 rate_limited`), auth failures (`401`), and domain exceptions (`400`/`403`/`404`/`409`/`500`).
* Non-Development environments never leak stack traces or internal exception details.
