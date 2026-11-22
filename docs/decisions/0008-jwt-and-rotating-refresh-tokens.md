# ADR-0008 — JWT Access Tokens and Rotating Hashed Refresh Tokens

* **Status:** ACCEPTED
* **Date:** 2026-11-22

## Context
The API needs stateless request authentication plus revocable session refresh for Swagger UI and Postman clients without building cookies/CSRF infrastructure for a browser SPA.

## Decision
Issue short-lived **HS256 JWT access tokens (`15 min`)** via `JsonWebTokenHandler` (`sub`, `email`, `jti`, `iat`, `exp`) and **`32`-byte opaque random refresh tokens (`7 days`)** transported in the JSON body. Store **only SHA-256 hex hashes** in `RefreshTokens`. Rotate refresh tokens on every `/refresh-token` call (`RevokedAt` + `ReplacedByTokenHash`), revoke all active tokens of a user on **revoked-token reuse** or **password change**, and enforce `Jwt:Key` decoded length `>= 32` bytes at startup (`ValidateOnStart`).

## Consequences
* DB breaches never expose usable refresh tokens; token theft/replay triggers full session revocation (`SEC-16`, `SEC-18`).
* Residual risk (`docs/design/security.md` §13): access tokens remain valid up to 15 minutes after logout/password change; JSON transport assumes API/mobile/Swagger clients rather than browser SPAs.
